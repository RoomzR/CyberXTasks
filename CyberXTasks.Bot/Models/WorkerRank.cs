namespace CyberXTasks.Bot.Models;

public record WorkerRank(int Place, string Name, int CompletedCount, int Score);

public record WeeklyReportData(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    int TotalCompleted,
    int TotalPending,
    int TotalInProgress,
    IReadOnlyList<WorkerRank> Leaderboard);
