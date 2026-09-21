using CyberXTasks.Api.Services;
using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.Data;
using CyberXTasks.Bot.Models;
using CyberXTasks.Bot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();
builder.Services.Configure<BotSettings>(builder.Configuration.GetSection("BotSettings"));
builder.Services.AddSingleton<TelegramAuthService>();
builder.Services.AddScoped<ApiUserContext>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<TaskService>();

var settings = builder.Configuration.GetSection("BotSettings").Get<BotSettings>() ?? new BotSettings();
var botToken = builder.Configuration["BOT_TOKEN"] ?? settings.BotToken;
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));
builder.Services.AddSingleton<NotificationHelper>();
builder.Services.AddScoped<TaskNotificationService>();

var dbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), settings.DatabasePath));
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var auth = ctx.RequestServices.GetRequiredService<TelegramAuthService>();
    var users = ctx.RequestServices.GetRequiredService<UserService>();
    var userCtx = ctx.RequestServices.GetRequiredService<ApiUserContext>();

    var initData = ctx.Request.Headers["X-Telegram-Init-Data"].FirstOrDefault()
        ?? ctx.Request.Query["initData"].FirstOrDefault();

    if (string.IsNullOrEmpty(initData) ||
        !auth.TryValidateInitData(initData, out var telegramId, out var firstName))
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    var user = await users.RegisterOrUpdateAsync(telegramId, null, firstName ?? "User", null);
    if (!user.IsActive)
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { error = "Not registered" });
        return;
    }

    userCtx.User = user;
    await next();
});

var api = app.MapGroup("/api");

api.MapGet("/me", (ApiUserContext ctx, UserService users) => MapProfile(ctx.User, users.IsOwnerAdmin(ctx.User)));

api.MapPatch("/profile", async (ProfileDto dto, ApiUserContext ctx, UserService users) =>
{
    if (string.IsNullOrWhiteSpace(dto.LastName) || dto.LastName.Trim().Length < 2)
        return Results.BadRequest(new { error = "Укажите фамилию" });
    if (string.IsNullOrWhiteSpace(dto.FirstName) || dto.FirstName.Trim().Length < 2)
        return Results.BadRequest(new { error = "Укажите имя" });

    var user = await users.UpdateProfileAsync(ctx.User.Id, dto.LastName, dto.FirstName, dto.MiddleName);
    return Results.Ok(MapProfile(user, users.IsOwnerAdmin(user)));
});

api.MapGet("/dashboard", async (ApiUserContext ctx, TaskService tasks) =>
{
    var isManager = ctx.User.IsManagerOrAdmin();
    return Results.Ok(await tasks.GetDayStatsAsync(ctx.User.Id, isManager));
});

api.MapGet("/tasks/today", async (ApiUserContext ctx, TaskService tasks) =>
{
    var list = await tasks.GetTodayTasksAsync(ctx.User.Id, ctx.User.IsManagerOrAdmin());
    return Results.Ok(list.Select(t => MapTask(t)));
});

api.MapGet("/tasks/{id:int}", async (int id, ApiUserContext ctx, TaskService tasks) =>
{
    var task = await tasks.GetByIdAsync(id);
    if (task is null) return Results.NotFound();
    return Results.Ok(MapTask(task));
});

api.MapPost("/tasks", async (CreateTaskDto dto, ApiUserContext ctx, TaskService tasks, TaskNotificationService notify) =>
{
    if (!ctx.User.IsManagerOrAdmin())
        return Results.Forbid();

    TimeOnly? dueTime = null;
    if (!string.IsNullOrWhiteSpace(dto.DueTime) && TimeOnly.TryParse(dto.DueTime, out var parsed))
        dueTime = parsed;

    Enum.TryParse<TaskPriority>(dto.Priority, true, out var priority);
    DateOnly? scheduledDate = null;
    if (!string.IsNullOrWhiteSpace(dto.ScheduledDate) && DateOnly.TryParse(dto.ScheduledDate, out var sd))
        scheduledDate = sd;

    var task = await tasks.CreateAsync(
        dto.Title, dto.AssignedToId, ctx.User.Id, dueTime,
        priority, dueTime.HasValue ? dto.ReminderMinutes : null,
        dto.Description, scheduledDate, mainTaskItem: dto.MainTask, checklistItems: dto.ChecklistItems);

    await notify.NotifyTaskAssignedAsync(task.Id);

    var saved = await tasks.GetByIdAsync(task.Id) ?? task;
    return Results.Ok(MapTask(saved));
});

api.MapPost("/tasks/{taskId:int}/checklist/{itemId:int}/toggle", async (
    int taskId, int itemId, ApiUserContext ctx, TaskService tasks, TaskNotificationService notify) =>
{
    var (ok, allDone, task) = await tasks.ToggleChecklistItemAsync(taskId, itemId, ctx.User.Id);
    if (!ok || task is null) return Results.BadRequest();

    if (allDone)
        await notify.NotifyTaskCompletedAsync(taskId, null);

    return Results.Ok(MapTask(task));
});

api.MapPost("/tasks/{id:int}/accept", async (int id, ApiUserContext ctx, TaskService tasks, TaskNotificationService notify) =>
{
    var ok = await tasks.AcceptAsync(id, ctx.User.Id);
    if (!ok) return Results.BadRequest();

    await notify.NotifyTaskAcceptedAsync(id);
    return Results.Ok();
});

api.MapPost("/tasks/{id:int}/complete", async (int id, CompleteTaskDto? dto, ApiUserContext ctx, TaskService tasks, TaskNotificationService notify) =>
{
    var ok = await tasks.CompleteAsync(id, ctx.User.Id, dto?.Comment);
    if (!ok) return Results.BadRequest();

    await notify.NotifyTaskCompletedAsync(id, dto?.Comment);
    return Results.Ok();
});

api.MapPatch("/tasks/{id:int}", async (int id, UpdateTaskDto dto, ApiUserContext ctx, TaskService tasks) =>
{
    if (!ctx.User.IsManagerOrAdmin()) return Results.Forbid();

    if (dto.DueTimeSet)
        await tasks.UpdateDueTimeAsync(id, dto.DueTime);

    DateOnly? scheduledDate = null;
    if (!string.IsNullOrWhiteSpace(dto.ScheduledDate) && DateOnly.TryParse(dto.ScheduledDate, out var sd))
        scheduledDate = sd;

    if (dto.Title is not null || dto.Description is not null || scheduledDate.HasValue)
        await tasks.UpdateDetailsAsync(id, dto.Title, dto.Description, scheduledDate, null);

    var task = await tasks.GetByIdAsync(id);
    return task is null ? Results.NotFound() : Results.Ok(MapTask(task));
});

api.MapDelete("/tasks/{id:int}", async (int id, ApiUserContext ctx, TaskService tasks) =>
{
    if (!ctx.User.IsManagerOrAdmin()) return Results.Forbid();
    var ok = await tasks.DeleteAsync(id);
    return ok ? Results.Ok() : Results.BadRequest();
});

api.MapGet("/workers", async (ApiUserContext ctx, UserService users) =>
{
    if (!ctx.User.IsManagerOrAdmin()) return Results.Forbid();
    var workers = await users.GetWorkersAsync();
    return Results.Ok(workers.Select(w => new { w.Id, w.TelegramId, displayName = UserDisplay.FullName(w) }));
});

api.MapPost("/workers", async (AddWorkerDto dto, ApiUserContext ctx, UserService users) =>
{
    if (!ctx.User.IsManagerOrAdmin()) return Results.Forbid();
    var worker = await users.AddWorkerAsync(dto.TelegramId, null, dto.FirstName ?? "Сотрудник");
    return worker is null ? Results.BadRequest(new { error = "Already manager or admin" }) : Results.Ok(new { worker.Id, worker.TelegramId });
});

api.MapGet("/users", async (ApiUserContext ctx, UserService users) =>
{
    if (!ctx.User.IsAdmin()) return Results.Forbid();
    var all = await users.GetAllUsersAsync();
    return Results.Ok(all.Select(u => new
    {
        u.Id,
        u.TelegramId,
        displayName = UserDisplay.FullName(u),
        role = u.Role.ToString(),
        u.IsActive,
        profileCompleted = u.ProfileCompleted,
        isOwner = users.IsOwnerAdmin(u)
    }));
});

api.MapPatch("/users/{id:int}/role", async (int id, SetRoleDto dto, ApiUserContext ctx, UserService users) =>
{
    if (!ctx.User.IsAdmin()) return Results.Forbid();
    if (!Enum.TryParse<UserRole>(dto.Role, true, out var role))
        return Results.BadRequest(new { error = "Invalid role" });

    var (ok, error) = await users.SetUserRoleAsync(id, role, ctx.User.Id);
    return ok ? Results.Ok() : Results.BadRequest(new { error });
});

api.MapPatch("/users/{id:int}/active", async (int id, SetActiveDto dto, ApiUserContext ctx, UserService users) =>
{
    if (!ctx.User.IsAdmin()) return Results.Forbid();
    var (ok, error) = await users.SetUserActiveAsync(id, dto.IsActive, ctx.User.Id);
    return ok ? Results.Ok() : Results.BadRequest(new { error });
});

api.MapGet("/leaderboard", async (TaskService tasks) =>
    Results.Ok(await tasks.GetWeeklyLeaderboardAsync()));

api.MapGet("/weekly-report", async (ApiUserContext ctx, TaskService tasks) =>
{
    if (!ctx.User.IsManagerOrAdmin()) return Results.Forbid();
    return Results.Ok(await tasks.GetWeeklyReportAsync());
});

api.MapGet("/schedule", async (int? weekOffset, ApiUserContext ctx, TaskService tasks) =>
{
    var schedule = await tasks.GetScheduleWeekAsync(
        ctx.User.Id, ctx.User.IsManagerOrAdmin(), weekOffset ?? 0);
    return Results.Ok(new
    {
        weekStart = schedule.WeekStart.ToString("yyyy-MM-dd"),
        weekEnd = schedule.WeekEnd.ToString("yyyy-MM-dd"),
        workers = schedule.Workers.Select(w => new { w.Id, displayName = w.DisplayName }),
        days = schedule.Days.Select(d => new
        {
            date = d.Date.ToString("yyyy-MM-dd"),
            dayLabel = d.DayLabel,
            dayNum = d.Date.Day,
            isToday = d.IsToday,
            tasks = d.Tasks.Select(t => MapTask(t))
        })
    });
});

api.MapGet("/archive", async (ApiUserContext ctx, TaskService tasks) =>
{
    if (!ctx.User.IsManagerOrAdmin()) return Results.Forbid();
    var items = await tasks.GetArchiveAsync(null, true);
    return Results.Ok(items.Select(t => MapTask(t)));
});

api.MapPatch("/settings", async (SettingsDto dto, ApiUserContext ctx, UserService users) =>
{
    if (dto.NotificationTime is not null && TimeOnly.TryParse(dto.NotificationTime, out var nt))
        await users.UpdateNotificationTimeAsync(ctx.User.Id, nt);
    if (dto.ReminderMinutes is not null)
        await users.UpdateDefaultReminderMinutesAsync(ctx.User.Id, dto.ReminderMinutes.Value);

    var user = await users.GetByTelegramIdAsync(ctx.User.TelegramId);
    return Results.Ok(new
    {
        notificationTime = user!.NotificationTime.ToString("HH:mm"),
        reminderMinutes = user.DefaultReminderMinutes
    });
});

app.MapFallbackToFile("index.html");
app.Run();

static object MapTask(WorkTask t)
{
    var checklist = t.ChecklistItems.OrderByDescending(c => c.IsMain).ThenBy(c => c.SortOrder).ToList();
    var main = checklist.FirstOrDefault(c => c.IsMain);
    var subs = checklist.Where(c => !c.IsMain).ToList();
    var done = checklist.Count(c => c.IsCompleted);
    return new
    {
        t.Id,
        t.Title,
        t.Description,
        status = t.Status.ToString(),
        priority = t.Priority.ToString(),
        dueTime = t.DueTime?.ToString("HH:mm"),
        reminderMinutes = t.ReminderMinutesBefore,
        scheduledDate = t.ScheduledDate.ToString("yyyy-MM-dd"),
        completionComment = t.CompletionComment,
        completedAt = t.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
        assignedTo = t.AssignedTo is null ? null : new { t.AssignedTo.Id, displayName = UserDisplay.FullName(t.AssignedTo) },
        createdBy = t.CreatedBy is null ? null : new { displayName = UserDisplay.FullName(t.CreatedBy) },
        checklist = checklist.Select(c => new
        {
            c.Id,
            c.Title,
            isMain = c.IsMain,
            isCompleted = c.IsCompleted,
            completedAt = c.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        }),
        checklistProgress = checklist.Count > 0 ? new
        {
            done,
            total = checklist.Count,
            subDone = subs.Count(c => c.IsCompleted),
            subTotal = subs.Count,
            mainCompleted = main?.IsCompleted ?? false,
            hasMain = main is not null
        } : null
    };
}

static object MapProfile(AppUser u, bool isOwner) => new
{
    u.Id,
    u.TelegramId,
    lastName = u.LastName ?? "",
    firstName = u.FirstName,
    middleName = u.MiddleName ?? "",
    displayName = UserDisplay.FullName(u),
    profileCompleted = u.ProfileCompleted,
    role = u.Role.ToString(),
    isOwner,
    notificationTime = u.NotificationTime.ToString("HH:mm"),
    reminderMinutes = u.DefaultReminderMinutes,
    utcOffset = u.UtcOffsetMinutes
};

record CreateTaskDto(
    string Title, int AssignedToId, string? DueTime, string Priority, int? ReminderMinutes,
    string? Description, string? ScheduledDate, string? MainTask, List<string>? ChecklistItems);
record UpdateTaskDto(
    string? Title, string? Description, string? ScheduledDate,
    TimeOnly? DueTime, bool DueTimeSet);
record CompleteTaskDto(string? Comment);
record AddWorkerDto(long TelegramId, string? FirstName);
record SetRoleDto(string Role);
record SetActiveDto(bool IsActive);
record SettingsDto(string? NotificationTime, int? ReminderMinutes);
record ProfileDto(string LastName, string FirstName, string? MiddleName);
