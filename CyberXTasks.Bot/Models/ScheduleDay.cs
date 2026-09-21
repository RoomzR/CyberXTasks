namespace CyberXTasks.Bot.Models;

public record ScheduleDay(DateOnly Date, string DayLabel, bool IsToday, List<WorkTask> Tasks);

public record ScheduleWeek(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    List<ScheduleDay> Days,
    List<ScheduleWorker> Workers);

public record ScheduleWorker(int Id, string DisplayName);
