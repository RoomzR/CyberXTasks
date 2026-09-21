namespace CyberXTasks.Bot.Models;

public record DayStats(int Pending, int InProgress, int CompletedToday, int TotalActive)
{
    public int DoneToday => CompletedToday;
    public int ProgressTotal => TotalActive + CompletedToday;
}
