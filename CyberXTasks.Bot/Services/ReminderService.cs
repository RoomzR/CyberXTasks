using CyberXTasks.Bot.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CyberXTasks.Bot.Services;

public class ReminderService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckRemindersAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Reminder failed"); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task CheckRemindersAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TaskService>();
        var notify = scope.ServiceProvider.GetRequiredService<NotificationHelper>();

        foreach (var task in await tasks.GetTasksForReminderAsync(ct))
        {
            if (!task.DueTime.HasValue || task.AssignedTo is null) continue;

            var offset = TimeSpan.FromMinutes(task.AssignedTo.UtcOffsetMinutes);
            var localNow = DateTime.UtcNow.Add(offset);
            var dueLocal = task.ScheduledDate.ToDateTime(task.DueTime.Value);
            var mins = task.ReminderMinutesBefore ?? task.AssignedTo.DefaultReminderMinutes;
            var remindAt = dueLocal.AddMinutes(-mins);

            if (localNow < remindAt || localNow >= dueLocal) continue;

            var left = Math.Max(1, (int)Math.Ceiling((dueLocal - localNow).TotalMinutes));
            var text = $"""
                {NotifyTemplates.Brand} · <b>Напоминание</b>

                {NotifyTemplates.PriorityLabel(task.Priority)}
                📝 <b>{System.Net.WebUtility.HtmlEncode(task.Title)}</b>
                ⏰ Дедлайн через <b>{left} мин</b> ({task.DueTime:HH\\:mm})

                <i>Откройте приложение → Сегодня</i>
                """;

            await notify.SendWithAppButtonAsync(task.AssignedTo.TelegramId, text, ct);

            await tasks.MarkReminderSentAsync(task.Id, ct);
            logger.LogInformation("Reminder sent for task {Id}", task.Id);
        }
    }
}
