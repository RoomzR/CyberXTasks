using CyberXTasks.Bot.Data;
using CyberXTasks.Bot.Models;
using Microsoft.EntityFrameworkCore;

namespace CyberXTasks.Bot.Services;

public class TaskService(AppDbContext db)
{
    private IQueryable<WorkTask> TasksWithDetails() =>
        db.Tasks
            .Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy)
            .Include(t => t.ChecklistItems.OrderBy(c => c.SortOrder));

    public async Task<List<WorkTask>> GetTodayTasksAsync(int userId, bool managerView = false, CancellationToken ct = default)
    {
        var today = GetUserToday(userId);
        var query = TasksWithDetails()
            .Where(t => t.ScheduledDate == today
                        && (t.Status == WorkTaskStatus.Pending || t.Status == WorkTaskStatus.Accepted));

        if (!managerView)
            query = query.Where(t => t.AssignedToId == userId);

        return await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueTime)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<WorkTask>> GetTasksForReminderAsync(CancellationToken ct = default) =>
        await db.Tasks
            .Include(t => t.AssignedTo)
            .Where(t => t.DueTime.HasValue
                        && !t.ReminderSent
                        && (t.Status == WorkTaskStatus.Pending || t.Status == WorkTaskStatus.Accepted))
            .ToListAsync(ct);

    public async Task<List<WorkTask>> GetArchiveAsync(int? userId, bool managerView, CancellationToken ct = default)
    {
        var query = TasksWithDetails()
            .Where(t => t.Status == WorkTaskStatus.Completed || t.Status == WorkTaskStatus.Deleted);

        if (!managerView && userId.HasValue)
            query = query.Where(t => t.AssignedToId == userId.Value);

        return await query
            .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
            .Take(30)
            .ToListAsync(ct);
    }

    public async Task<WorkTask?> GetByIdAsync(int taskId, CancellationToken ct = default) =>
        await TasksWithDetails().FirstOrDefaultAsync(t => t.Id == taskId, ct);

    public async Task<WorkTask> CreateAsync(
        string title,
        int assignedToId,
        int createdById,
        TimeOnly? dueTime,
        TaskPriority priority,
        int? reminderMinutesBefore,
        string? description = null,
        DateOnly? scheduledDate = null,
        TaskCategory category = TaskCategory.General,
        string? mainTaskItem = null,
        IReadOnlyList<string>? checklistItems = null,
        CancellationToken ct = default)
    {
        var task = new WorkTask
        {
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Category = category,
            AssignedToId = assignedToId,
            CreatedById = createdById,
            ScheduledDate = scheduledDate ?? GetUserToday(assignedToId),
            DueTime = dueTime,
            Priority = priority,
            ReminderMinutesBefore = dueTime.HasValue ? reminderMinutesBefore : null,
            Status = WorkTaskStatus.Pending
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        var order = 0;
        if (!string.IsNullOrWhiteSpace(mainTaskItem))
        {
            db.TaskChecklistItems.Add(new TaskChecklistItem
            {
                TaskId = task.Id,
                Title = mainTaskItem.Trim(),
                IsMain = true,
                SortOrder = order++
            });
        }

        if (checklistItems is { Count: > 0 })
        {
            foreach (var itemTitle in checklistItems.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                db.TaskChecklistItems.Add(new TaskChecklistItem
                {
                    TaskId = task.Id,
                    Title = itemTitle.Trim(),
                    SortOrder = order++
                });
            }
        }

        if (order > 0)
            await db.SaveChangesAsync(ct);

        return (await GetByIdAsync(task.Id, ct))!;
    }

    public async Task<ScheduleWeek> GetScheduleWeekAsync(
        int userId,
        bool managerView,
        int weekOffset = 0,
        CancellationToken ct = default)
    {
        var (weekStart, weekEnd) = GetWeekRange(weekOffset);
        var query = TasksWithDetails()
            .Where(t => t.ScheduledDate >= weekStart && t.ScheduledDate <= weekEnd
                        && (t.Status == WorkTaskStatus.Pending || t.Status == WorkTaskStatus.Accepted));

        if (!managerView)
            query = query.Where(t => t.AssignedToId == userId);

        var tasks = await query.ToListAsync(ct);
        var today = GetUserToday(userId);

        var days = new List<ScheduleDay>();
        for (var d = weekStart; d <= weekEnd; d = d.AddDays(1))
        {
            var dayTasks = tasks
                .Where(t => t.ScheduledDate == d)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.DueTime)
                .ThenBy(t => t.AssignedTo.LastName ?? t.AssignedTo.FirstName)
                .ToList();

            days.Add(new ScheduleDay(
                d,
                DayShortLabel(d.DayOfWeek),
                d == today,
                dayTasks));
        }

        List<ScheduleWorker> workers = [];
        if (managerView)
        {
            workers = tasks
                .Select(t => t.AssignedTo)
                .DistinctBy(u => u.Id)
                .OrderBy(u => u.LastName ?? u.FirstName)
                .ThenBy(u => u.FirstName)
                .Select(u => new ScheduleWorker(u.Id, UserDisplay.FullName(u)))
                .ToList();
        }

        return new ScheduleWeek(weekStart, weekEnd, days, workers);
    }

    public async Task<(bool Ok, bool AllCompleted, WorkTask? Task)> ToggleChecklistItemAsync(
        int taskId, int itemId, int userId, CancellationToken ct = default)
    {
        var task = await db.Tasks
            .Include(t => t.ChecklistItems)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.AssignedToId == userId, ct);

        if (task is null || task.Status is WorkTaskStatus.Deleted)
            return (false, false, null);

        var item = task.ChecklistItems.FirstOrDefault(c => c.Id == itemId);
        if (item is null)
            return (false, false, null);

        if (task.Status == WorkTaskStatus.Pending)
        {
            task.Status = WorkTaskStatus.Accepted;
            task.AcceptedAt = DateTime.UtcNow;
        }

        item.IsCompleted = !item.IsCompleted;
        item.CompletedAt = item.IsCompleted ? DateTime.UtcNow : null;

        var allDone = ShouldAutoCompleteChecklist(task);
        if (allDone)
        {
            task.Status = WorkTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
        }
        else if (task.Status == WorkTaskStatus.Completed)
        {
            task.Status = WorkTaskStatus.Accepted;
            task.CompletedAt = null;
            task.CompletionComment = null;
        }

        await db.SaveChangesAsync(ct);
        var full = await GetByIdAsync(taskId, ct);
        return (true, allDone, full);
    }

    private static bool ShouldAutoCompleteChecklist(WorkTask task)
    {
        var items = task.ChecklistItems.ToList();
        if (items.Count == 0) return false;

        var main = items.FirstOrDefault(c => c.IsMain);
        var subs = items.Where(c => !c.IsMain).ToList();

        if (main?.IsCompleted == true)
            return true;

        if (subs.Count > 0 && subs.All(c => c.IsCompleted))
            return true;

        return main is null && items.All(c => c.IsCompleted);
    }

    public async Task<bool> AcceptAsync(int taskId, int userId, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(
            t => t.Id == taskId && t.AssignedToId == userId
                 && (t.Status == WorkTaskStatus.Pending || t.Status == WorkTaskStatus.Accepted), ct);

        if (task is null || task.Status == WorkTaskStatus.Accepted)
            return false;

        task.Status = WorkTaskStatus.Accepted;
        task.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CompleteAsync(int taskId, int userId, string? comment = null, CancellationToken ct = default)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(
            t => t.Id == taskId && t.AssignedToId == userId
                 && t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Deleted, ct);

        if (task is null)
            return false;

        task.Status = WorkTaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletionComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateTitleAsync(int taskId, string title, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([taskId], ct);
        if (task is null || task.Status is WorkTaskStatus.Completed or WorkTaskStatus.Deleted)
            return false;

        task.Title = title.Trim();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateDueTimeAsync(int taskId, TimeOnly? dueTime, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([taskId], ct);
        if (task is null || task.Status is WorkTaskStatus.Completed or WorkTaskStatus.Deleted)
            return false;

        task.DueTime = dueTime;
        task.ReminderSent = false;
        if (!dueTime.HasValue)
            task.ReminderMinutesBefore = null;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateDetailsAsync(
        int taskId,
        string? title,
        string? description,
        DateOnly? scheduledDate,
        TaskCategory? category,
        CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([taskId], ct);
        if (task is null || task.Status is WorkTaskStatus.Completed or WorkTaskStatus.Deleted)
            return false;

        if (title is not null)
            task.Title = title.Trim();
        if (description is not null)
            task.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (scheduledDate.HasValue)
            task.ScheduledDate = scheduledDate.Value;
        if (category.HasValue)
            task.Category = category.Value;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int taskId, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([taskId], ct);
        if (task is null || task.Status is WorkTaskStatus.Completed or WorkTaskStatus.Deleted)
            return false;

        task.Status = WorkTaskStatus.Deleted;
        task.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkReminderSentAsync(int taskId, CancellationToken ct = default)
    {
        var task = await db.Tasks.FindAsync([taskId], ct);
        if (task is null) return;

        task.ReminderSent = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<DayStats> GetDayStatsAsync(int userId, bool managerView = false, CancellationToken ct = default)
    {
        var today = GetUserToday(userId);
        var query = db.Tasks.Where(t => t.ScheduledDate == today);

        if (!managerView)
            query = query.Where(t => t.AssignedToId == userId);

        var tasks = await query.ToListAsync(ct);

        return new DayStats(
            Pending: tasks.Count(t => t.Status == WorkTaskStatus.Pending),
            InProgress: tasks.Count(t => t.Status == WorkTaskStatus.Accepted),
            CompletedToday: tasks.Count(t => t.Status == WorkTaskStatus.Completed),
            TotalActive: tasks.Count(t => t.Status is WorkTaskStatus.Pending or WorkTaskStatus.Accepted));
    }

    public async Task<List<WorkerRank>> GetWeeklyLeaderboardAsync(CancellationToken ct = default)
    {
        var (weekStart, weekEnd) = GetCurrentWeekRange();
        var startUtc = weekStart.ToDateTime(TimeOnly.MinValue);
        var endUtc = weekEnd.ToDateTime(new TimeOnly(23, 59, 59));

        var completed = await db.Tasks
            .Include(t => t.AssignedTo)
            .Where(t => t.Status == WorkTaskStatus.Completed
                        && t.CompletedAt >= startUtc
                        && t.CompletedAt <= endUtc)
            .ToListAsync(ct);

        var ranks = completed
            .GroupBy(t => t.AssignedTo)
            .Select(g => new WorkerRank(
                Place: 0,
                Name: UserDisplay.FullName(g.Key),
                CompletedCount: g.Count(),
                Score: g.Sum(t => PriorityScore(t.Priority))))
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.CompletedCount)
            .ToList();

        return ranks
            .Select((r, i) => r with { Place = i + 1 })
            .ToList();
    }

    public async Task<WeeklyReportData> GetWeeklyReportAsync(CancellationToken ct = default)
    {
        var (weekStart, weekEnd) = GetCurrentWeekRange();
        var startUtc = weekStart.ToDateTime(TimeOnly.MinValue);
        var endUtc = weekEnd.ToDateTime(new TimeOnly(23, 59, 59));

        var weekTasks = await db.Tasks
            .Include(t => t.AssignedTo)
            .Where(t => t.ScheduledDate >= weekStart && t.ScheduledDate <= weekEnd
                        || (t.CompletedAt >= startUtc && t.CompletedAt <= endUtc))
            .ToListAsync(ct);

        var leaderboard = await GetWeeklyLeaderboardAsync(ct);

        return new WeeklyReportData(
            WeekStart: weekStart,
            WeekEnd: weekEnd,
            TotalCompleted: weekTasks.Count(t => t.Status == WorkTaskStatus.Completed),
            TotalPending: weekTasks.Count(t => t.Status == WorkTaskStatus.Pending),
            TotalInProgress: weekTasks.Count(t => t.Status == WorkTaskStatus.Accepted),
            Leaderboard: leaderboard);
    }

    public static int PriorityScore(TaskPriority priority) => priority switch
    {
        TaskPriority.Urgent => 4,
        TaskPriority.High => 3,
        TaskPriority.Normal => 2,
        _ => 1
    };

    private static (DateOnly Start, DateOnly End) GetCurrentWeekRange() =>
        GetWeekRange(0);

    private static (DateOnly Start, DateOnly End) GetWeekRange(int weekOffset)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var diff = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var start = today.AddDays(-diff).AddDays(weekOffset * 7);
        return (start, start.AddDays(6));
    }

    private static string DayShortLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Пн",
        DayOfWeek.Tuesday => "Вт",
        DayOfWeek.Wednesday => "Ср",
        DayOfWeek.Thursday => "Чт",
        DayOfWeek.Friday => "Пт",
        DayOfWeek.Saturday => "Сб",
        DayOfWeek.Sunday => "Вс",
        _ => ""
    };

    private DateOnly GetUserToday(int userId)
    {
        var user = db.Users.Find(userId);
        var offset = TimeSpan.FromMinutes(user?.UtcOffsetMinutes ?? 180);
        return DateOnly.FromDateTime(DateTime.UtcNow.Add(offset));
    }
}
