namespace CyberXTasks.Bot.Models;

public class TaskChecklistItem
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public WorkTask Task { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public bool IsMain { get; set; }
    public bool IsCompleted { get; set; }
    public int SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }
}
