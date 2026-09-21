namespace CyberXTasks.Bot.Models;

public class AppUser
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string? Username { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? MiddleName { get; set; }
    public bool ProfileCompleted { get; set; }
    public UserRole Role { get; set; } = UserRole.Worker;
    public TimeOnly NotificationTime { get; set; } = new(9, 10);
    public int DefaultReminderMinutes { get; set; } = 15;
    public int UtcOffsetMinutes { get; set; } = 180;
    public DateOnly? LastNotificationDate { get; set; }
    public DateOnly? LastWeeklyReportDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WorkTask> AssignedTasks { get; set; } = [];
    public ICollection<WorkTask> CreatedTasks { get; set; } = [];
}
