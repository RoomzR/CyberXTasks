namespace CyberXTasks.Bot.Models;

public class WorkTask
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskCategory Category { get; set; } = TaskCategory.General;
    public int AssignedToId { get; set; }
    public AppUser AssignedTo { get; set; } = null!;
    public int CreatedById { get; set; }
    public AppUser CreatedBy { get; set; } = null!;
    public DateOnly ScheduledDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public int? ReminderMinutesBefore { get; set; }
    public bool ReminderSent { get; set; }
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletionComment { get; set; }
    public ICollection<TaskChecklistItem> ChecklistItems { get; set; } = [];
}
