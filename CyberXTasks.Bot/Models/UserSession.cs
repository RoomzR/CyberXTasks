namespace CyberXTasks.Bot.Models;

public class UserSession
{
    public AppScreen Screen { get; set; } = AppScreen.Home;
    public int? PanelMessageId { get; set; }
    public ConversationStep Step { get; set; } = ConversationStep.None;
    public int? TargetTaskId { get; set; }
    public string? DraftTitle { get; set; }
    public TimeOnly? DraftDueTime { get; set; }
    public TaskPriority DraftPriority { get; set; } = TaskPriority.Normal;
    public int? DraftReminderMinutes { get; set; }

    public void ResetFlow()
    {
        Step = ConversationStep.None;
        TargetTaskId = null;
        DraftTitle = null;
        DraftDueTime = null;
        DraftPriority = TaskPriority.Normal;
        DraftReminderMinutes = null;
    }

    public void Reset()
    {
        Screen = AppScreen.Home;
        PanelMessageId = null;
        ResetFlow();
    }
}
