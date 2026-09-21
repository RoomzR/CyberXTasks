using System.Net;
using CyberXTasks.Bot.Models;
using CyberXTasks.Bot.UI;
using Microsoft.Extensions.Logging;

namespace CyberXTasks.Bot.Services;

public class TaskNotificationService(
    NotificationHelper notify,
    TaskService tasks,
    ILogger<TaskNotificationService> logger)
{
    public async Task NotifyTaskAssignedAsync(int taskId, CancellationToken ct = default)
    {
        try
        {
            var full = await tasks.GetByIdAsync(taskId, ct);
            if (full?.AssignedTo is null) return;

            var manager = full.CreatedBy is null ? "Менеджер" : UserDisplay.FullName(full.CreatedBy);
            var date = full.ScheduledDate.ToString("dd.MM.yyyy");
            var time = full.DueTime.HasValue ? $" до <b>{full.DueTime:HH\\:mm}</b>" : "";
            var desc = string.IsNullOrWhiteSpace(full.Description)
                ? ""
                : $"\n\n{Truncate(Html(full.Description), 400)}";
            var ordered = full.ChecklistItems.OrderByDescending(c => c.IsMain).ThenBy(c => c.SortOrder).ToList();
            var checklist = ordered.Count > 0
                ? "\n\n" + string.Join("\n", ordered.Select(c =>
                    c.IsMain ? $"⭐ <b>Главная:</b> {Html(c.Title)}" : $"☐ {Html(c.Title)}"))
                : "";

            var text = $"""
                {NotifyTemplates.Brand} · <b>Новая задача</b>

                {NotifyTemplates.PriorityLabel(full.Priority)}
                📝 <b>{Html(full.Title)}</b>
                📅 {date}{time}
                👤 От: <b>{Html(manager)}</b>{desc}{checklist}

                <i>Откройте приложение → отметьте пункты списка</i>
                """;

            await notify.SendWithAppButtonAsync(full.AssignedTo.TelegramId, text, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send task assigned notification for task {TaskId}", taskId);
        }
    }

    public async Task NotifyTaskAcceptedAsync(int taskId, CancellationToken ct = default)
    {
        try
        {
            var full = await tasks.GetByIdAsync(taskId, ct);
            if (full?.CreatedBy is null || full.AssignedTo is null) return;
            if (full.CreatedById == full.AssignedToId) return;

            var date = full.ScheduledDate.ToString("dd.MM.yyyy");
            var time = full.DueTime.HasValue ? $" до <b>{full.DueTime:HH\\:mm}</b>" : "";

            var text = $"""
                {NotifyTemplates.Brand} · <b>Задача принята</b>

                👷 <b>{Html(UserDisplay.FullName(full.AssignedTo))}</b> взял в работу:
                📝 <b>{Html(full.Title)}</b>
                📅 {date}{time}

                <i>Откройте приложение для деталей</i>
                """;

            await notify.SendWithAppButtonAsync(full.CreatedBy.TelegramId, text, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send task accepted notification for task {TaskId}", taskId);
        }
    }

    public async Task NotifyTaskCompletedAsync(int taskId, string? comment, CancellationToken ct = default)
    {
        try
        {
            var full = await tasks.GetByIdAsync(taskId, ct);
            if (full?.CreatedBy is null || full.AssignedTo is null) return;
            if (full.CreatedById == full.AssignedToId) return;

            var worker = UserDisplay.FullName(full.AssignedTo);
            var commentBlock = string.IsNullOrWhiteSpace(comment)
                ? ""
                : $"\n\n💬 <b>Комментарий:</b>\n{Truncate(Html(comment), 500)}";

            var text = $"""
                {NotifyTemplates.Brand} · <b>Задача выполнена</b>

                ✅ <b>{Html(full.Title)}</b>
                👷 {Html(worker)}{commentBlock}

                <i>Откройте приложение для деталей</i>
                """;

            await notify.SendWithAppButtonAsync(full.CreatedBy.TelegramId, text, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send task completed notification for task {TaskId}", taskId);
        }
    }

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
