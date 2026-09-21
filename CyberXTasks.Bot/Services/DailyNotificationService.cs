using CyberXTasks.Bot.Models;
using CyberXTasks.Bot.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberXTasks.Bot.Services;

public class DailyNotificationService(
    IServiceScopeFactory scopeFactory,
    ILogger<DailyNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SendDueNotificationsAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Daily notification failed"); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task SendDueNotificationsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var tasks = scope.ServiceProvider.GetRequiredService<TaskService>();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationHelper>();

        foreach (var user in await users.GetUsersForNotificationAsync(ct))
        {
            var localNow = DateTime.UtcNow.AddMinutes(user.UtcOffsetMinutes);
            var today = DateOnly.FromDateTime(localNow);
            if (user.LastNotificationDate == today) continue;

            var time = TimeOnly.FromDateTime(localNow);
            if (time.Hour != user.NotificationTime.Hour || time.Minute != user.NotificationTime.Minute)
                continue;

            var isManager = user.IsManagerOrAdmin();
            var list = await tasks.GetTodayTasksAsync(user.Id, isManager, ct);
            var stats = await tasks.GetDayStatsAsync(user.Id, isManager, ct);

            var body = list.Count == 0
                ? "На сегодня задач нет. Откройте приложение, чтобы добавить новую."
                : $"Задач на сегодня: {list.Count}\n\n{MessageTemplates.FormatTaskList(list)}";

            var text = $"""
                {MessageTemplates.TaskListHeader(today, stats, isDailyDigest: true)}

                {body}

                <i>Нажмите кнопку ниже, чтобы открыть приложение</i>
                """;

            await notify.SendWithAppButtonAsync(user.TelegramId, text, ct);
            await users.MarkNotifiedAsync(user.Id, today, ct);
        }
    }
}
