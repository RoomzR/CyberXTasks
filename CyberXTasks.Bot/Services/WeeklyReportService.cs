using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CyberXTasks.Bot.Services;

public class WeeklyReportService(
    IServiceScopeFactory scopeFactory,
    IOptions<BotSettings> settings,
    ILogger<WeeklyReportService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TrySendAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Weekly report failed"); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task TrySendAsync(CancellationToken ct)
    {
        var cfg = settings.Value;
        if (!Enum.TryParse<DayOfWeek>(cfg.WeeklyReportDay, true, out var day)) day = DayOfWeek.Friday;
        if (!TimeOnly.TryParse(cfg.WeeklyReportTime, out var time)) time = new TimeOnly(17, 0);

        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var tasks = scope.ServiceProvider.GetRequiredService<TaskService>();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationHelper>();

        var report = await tasks.GetWeeklyReportAsync(ct);

        foreach (var manager in await users.GetManagementUsersAsync(ct))
        {
            var local = DateTime.UtcNow.AddMinutes(manager.UtcOffsetMinutes);
            if (local.DayOfWeek != day) continue;
            var now = TimeOnly.FromDateTime(local);
            if (now.Hour != time.Hour || now.Minute != time.Minute) continue;
            if (manager.LastWeeklyReportDate == report.WeekEnd) continue;

            var top = report.Leaderboard.FirstOrDefault();
            var body = $"Выполнено: {report.TotalCompleted}\nВ работе: {report.TotalInProgress}\nОжидают: {report.TotalPending}"
                + (top is not null ? $"\nЛидер: {top.Name} ({top.Score} очк.)" : "");

            await notify.SendWithAppButtonAsync(manager.TelegramId,
                NotifyTemplates.Push("Еженедельный отчёт", body), ct);

            await users.MarkWeeklyReportSentAsync(manager.Id, report.WeekEnd, ct);
        }
    }
}
