using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.Models;
using CyberXTasks.Bot.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CyberXTasks.Bot.Services;

public class AppNavigationService(
    ITelegramBotClient bot,
    UserService users,
    TaskService tasks,
    SessionService sessions,
    IOptions<BotSettings> settings,
    ILogger<AppNavigationService> logger)
{
    public async Task LaunchAppAsync(
        long chatId,
        long telegramId,
        string? username,
        string firstName,
        string? lastName,
        CancellationToken ct)
    {
        sessions.Reset(telegramId);
        var user = await users.RegisterOrUpdateAsync(telegramId, username, firstName, lastName, ct);

        if (!user.IsActive)
        {
            await bot.SendMessage(chatId, MessageTemplates.NotRegistered(), parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId, "⬛", replyMarkup: KeyboardFactory.AppTabBar(user), cancellationToken: ct);

        var stats = await tasks.GetDayStatsAsync(user.Id, user.IsManagerOrAdmin(), ct);
        var welcome = MessageTemplates.Welcome(user, stats);
        var menu = KeyboardFactory.MainMenuButtons(user, settings.Value.WebAppUrl);
        await bot.SendMessage(chatId, welcome, parseMode: ParseMode.Html, replyMarkup: menu, cancellationToken: ct);

        await NavigateAsync(chatId, user, AppScreen.Home, ct, forceNew: true);
    }

    public async Task HandleTabAsync(long chatId, AppUser user, string tab, CancellationToken ct)
    {
        switch (tab)
        {
            case AppScreens.Tabs.Home:
                await NavigateAsync(chatId, user, AppScreen.Home, ct);
                break;
            case AppScreens.Tabs.Tasks:
                await NavigateAsync(chatId, user, AppScreen.Tasks, ct);
                break;
            case AppScreens.Tabs.Rating:
                await NavigateAsync(chatId, user, AppScreen.Leaderboard, ct);
                break;
            case AppScreens.Tabs.Create:
                await BeginAddTaskAsync(chatId, user, ct);
                break;
            case AppScreens.Tabs.Team:
                await NavigateAsync(chatId, user, AppScreen.Workers, ct);
                break;
            case AppScreens.Tabs.Settings:
                await NavigateAsync(chatId, user, AppScreen.Settings, ct);
                break;
            case AppScreens.Tabs.Admin:
                await NavigateAsync(chatId, user, AppScreen.AdminPanel, ct);
                break;
            default:
                await NavigateAsync(chatId, user, AppScreen.Home, ct);
                break;
        }
    }

    public async Task HandleNavAsync(long chatId, AppUser user, string action, int? id, CancellationToken ct)
    {
        switch (action)
        {
            case "home":
                await NavigateAsync(chatId, user, AppScreen.Home, ct);
                break;
            case "tasks":
                await NavigateAsync(chatId, user, AppScreen.Tasks, ct);
                break;
            case "task" when id.HasValue:
                await NavigateAsync(chatId, user, AppScreen.TaskDetail, ct, id);
                break;
            case "add":
                await BeginAddTaskAsync(chatId, user, ct);
                break;
            case "workers":
                await NavigateAsync(chatId, user, AppScreen.Workers, ct);
                break;
            case "archive":
                await NavigateAsync(chatId, user, AppScreen.Archive, ct);
                break;
            case "manager":
                await NavigateAsync(chatId, user, AppScreen.ManagerHub, ct);
                break;
            case "report":
                await ShowWeeklyReportAsync(chatId, user, ct);
                break;
            case "leaderboard":
                await NavigateAsync(chatId, user, AppScreen.Leaderboard, ct);
                break;
            case "admin":
                await NavigateAsync(chatId, user, AppScreen.AdminPanel, ct);
                break;
            case "settings":
                await NavigateAsync(chatId, user, AppScreen.Settings, ct);
                break;
            default:
                await NavigateAsync(chatId, user, AppScreen.Home, ct);
                break;
        }
    }

    public async Task NavigateAsync(
        long chatId,
        AppUser user,
        AppScreen screen,
        CancellationToken ct,
        int? taskId = null,
        bool forceNew = false)
    {
        var session = sessions.Get(user.TelegramId);
        session.Screen = screen;
        session.ResetFlow();
        if (taskId.HasValue)
            session.TargetTaskId = taskId;

        var (text, inline) = await BuildScreenAsync(user, screen, taskId, ct);
        await RenderPanelAsync(chatId, user.TelegramId, text, inline, forceNew, ct);
    }

    public async Task BeginAddTaskAsync(long chatId, AppUser user, CancellationToken ct)
    {
        if (!user.IsManagerOrAdmin())
        {
            await SendPushAsync(chatId, CyberXTheme.Error("Только менеджер может создавать задачи"), ct);
            return;
        }

        var session = sessions.Get(user.TelegramId);
        session.ResetFlow();
        session.Step = ConversationStep.AddTaskTitle;
        session.Screen = AppScreen.AddTask;

        await RenderPanelAsync(chatId, user.TelegramId,
            AppScreens.AddTaskPrompt("📝 <b>Шаг 1/4</b>\nВведите текст задачи в чат:"),
            CancelFlow(), false, ct);
    }

    public async Task HandleAddTitleAsync(long chatId, AppUser user, string text, CancellationToken ct)
    {
        if (text.Length < 2)
        {
            await SendPushAsync(chatId, CyberXTheme.Warning("Текст слишком короткий"), ct);
            return;
        }

        var session = sessions.Get(user.TelegramId);
        session.DraftTitle = text;
        session.Step = ConversationStep.None;

        await RenderPanelAsync(chatId, user.TelegramId,
            AppScreens.AddTaskPrompt("🏷 <b>Шаг 2/4</b>\nВыберите приоритет:"),
            KeyboardFactory.PriorityPicker(), false, ct);
    }

    public async Task HandlePriorityAsync(long chatId, AppUser user, TaskPriority priority, CancellationToken ct)
    {
        var session = sessions.Get(user.TelegramId);
        session.DraftPriority = priority;

        await RenderPanelAsync(chatId, user.TelegramId,
            AppScreens.AddTaskPrompt("⏰ <b>Шаг 3/4</b>\nУказать время выполнения?"),
            KeyboardFactory.YesNoTime(), false, ct);
    }

    public async Task HandleTimeChoiceAsync(long chatId, AppUser user, bool withTime, CancellationToken ct)
    {
        var session = sessions.Get(user.TelegramId);
        if (withTime)
        {
            session.Step = ConversationStep.AddTaskDueTime;
            await RenderPanelAsync(chatId, user.TelegramId,
                AppScreens.AddTaskPrompt("Введите время <b>ЧЧ:ММ</b> (например 14:30):"),
                CancelFlow(), false, ct);
        }
        else
        {
            session.DraftDueTime = null;
            session.DraftReminderMinutes = null;
            await PromptAssigneeAsync(chatId, user, ct);
        }
    }

    public async Task HandleDueTimeAsync(long chatId, AppUser user, TimeOnly time, CancellationToken ct)
    {
        var session = sessions.Get(user.TelegramId);
        session.DraftDueTime = time;
        session.Step = ConversationStep.None;

        await RenderPanelAsync(chatId, user.TelegramId,
            AppScreens.AddTaskPrompt($"🔔 <b>Шаг 4/4</b>\nНапоминание за N минут до <b>{time:HH\\:mm}</b>:"),
            KeyboardFactory.ReminderPicker(), false, ct);
    }

    public async Task HandleReminderChoiceAsync(long chatId, AppUser user, int minutes, CancellationToken ct)
    {
        var session = sessions.Get(user.TelegramId);
        session.DraftReminderMinutes = minutes;
        await PromptAssigneeAsync(chatId, user, ct);
    }

    public async Task HandleAssigneeAsync(long chatId, AppUser user, int workerId, CancellationToken ct)
    {
        var session = sessions.Get(user.TelegramId);
        if (string.IsNullOrWhiteSpace(session.DraftTitle))
        {
            session.ResetFlow();
            await NavigateAsync(chatId, user, AppScreen.Home, ct);
            return;
        }

        var task = await tasks.CreateAsync(
            session.DraftTitle, workerId, user.Id,
            session.DraftDueTime, session.DraftPriority,
            session.DraftDueTime.HasValue ? session.DraftReminderMinutes ?? user.DefaultReminderMinutes : null,
            description: null, scheduledDate: null, category: TaskCategory.General, ct: ct);

        session.ResetFlow();
        var saved = await tasks.GetByIdAsync(task.Id, ct) ?? task;

        await NavigateAsync(chatId, user, AppScreen.Tasks, ct);

        if (saved.AssignedTo is not null)
        {
            await SendPushAsync(saved.AssignedTo.TelegramId,
                AppScreens.PushNotification("Новая задача",
                    $"{CyberXTheme.PriorityLabel(saved.Priority)}\n📝 {saved.Title}\nОткройте вкладку 📋 Задачи"), ct);
        }
    }

    public async Task HandleTaskActionAsync(long chatId, AppUser user, string action, int taskId, CancellationToken ct)
    {
        var task = await tasks.GetByIdAsync(taskId, ct);
        if (task is null)
        {
            await SendPushAsync(chatId, CyberXTheme.Error("Задача не найдена"), ct);
            return;
        }

        switch (action)
        {
            case "accept":
                if (await tasks.AcceptAsync(taskId, user.Id, ct))
                    await NavigateAsync(chatId, user, AppScreen.TaskDetail, ct, taskId);
                else
                    await SendPushAsync(chatId, CyberXTheme.Error("Не удалось принять"), ct);
                break;

            case "complete":
                if (await tasks.CompleteAsync(taskId, user.Id, ct: ct))
                {
                    var stats = await tasks.GetDayStatsAsync(user.Id, false, ct);
                    await SendPushAsync(chatId,
                        AppScreens.PushNotification("Выполнено!", $"🎉 {task.Title}\n{CyberXTheme.ProgressBar(stats.CompletedToday, Math.Max(stats.ProgressTotal, 1))}"), ct);
                    await NavigateAsync(chatId, user, AppScreen.Tasks, ct);
                }
                else
                    await SendPushAsync(chatId, CyberXTheme.Error("Не удалось отметить"), ct);
                break;

            case "edit":
                await RenderPanelAsync(chatId, user.TelegramId, AppScreens.TaskDetail(task, true),
                    KeyboardFactory.EditTaskOptions(taskId), false, ct);
                break;

            case "edit_text":
                var s = sessions.Get(user.TelegramId);
                s.Step = ConversationStep.EditTaskTitle;
                s.TargetTaskId = taskId;
                await SendPushAsync(chatId, CyberXTheme.Info("Введите новый текст задачи:"), ct);
                break;

            case "edit_time":
                var es = sessions.Get(user.TelegramId);
                es.Step = ConversationStep.EditTaskDueTime;
                es.TargetTaskId = taskId;
                await SendPushAsync(chatId, CyberXTheme.Info("Введите время ЧЧ:ММ или 00:00 чтобы убрать:"), ct);
                break;

            case "delete":
                await RenderPanelAsync(chatId, user.TelegramId,
                    CyberXTheme.AppFrame("Удаление", $"Удалить?\n<b>{task.Title}</b>"),
                    KeyboardFactory.ConfirmDelete(taskId), false, ct);
                break;

            case "delete_confirm":
                if (await tasks.DeleteAsync(taskId, ct))
                    await NavigateAsync(chatId, user, AppScreen.Tasks, ct);
                break;
        }
    }

    public async Task HandleSettingsAsync(long chatId, AppUser user, string action, CancellationToken ct)
    {
        var session = sessions.Get(user.TelegramId);
        if (action == "notify_time")
        {
            session.Step = ConversationStep.SetNotificationTime;
            await SendPushAsync(chatId, CyberXTheme.Info("Введите время утренней рассылки (ЧЧ:ММ):"), ct);
        }
        else if (action == "reminder")
        {
            session.Step = ConversationStep.SetReminderMinutes;
            await RenderPanelAsync(chatId, user.TelegramId,
                AppScreens.Settings(user) + "\n\nВыберите за сколько минут напоминать:",
                KeyboardFactory.ReminderPicker(), false, ct);
        }
    }

    public async Task HandleTimeInputAsync(long chatId, AppUser user, string text, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(text.Trim(), "HH:mm", out var time)
            && !TimeOnly.TryParseExact(text.Trim(), "H:mm", out time))
        {
            await SendPushAsync(chatId, CyberXTheme.Error("Формат: ЧЧ:ММ"), ct);
            return;
        }

        var session = sessions.Get(user.TelegramId);
        switch (session.Step)
        {
            case ConversationStep.AddTaskDueTime:
                await HandleDueTimeAsync(chatId, user, time, ct);
                break;
            case ConversationStep.EditTaskDueTime when session.TargetTaskId.HasValue:
                var due = time == TimeOnly.MinValue ? (TimeOnly?)null : time;
                await tasks.UpdateDueTimeAsync(session.TargetTaskId.Value, due, ct);
                session.ResetFlow();
                await NavigateAsync(chatId, user, AppScreen.TaskDetail, ct, session.TargetTaskId);
                break;
            case ConversationStep.SetNotificationTime:
                await users.UpdateNotificationTimeAsync(user.Id, time, ct);
                session.ResetFlow();
                user = (await users.GetByTelegramIdAsync(user.TelegramId, ct))!;
                await NavigateAsync(chatId, user, AppScreen.Settings, ct);
                break;
        }
    }

    public async Task HandleReminderSettingAsync(long chatId, AppUser user, int minutes, CancellationToken ct)
    {
        await users.UpdateDefaultReminderMinutesAsync(user.Id, minutes, ct);
        sessions.Get(user.TelegramId).ResetFlow();
        user = (await users.GetByTelegramIdAsync(user.TelegramId, ct))!;
        await NavigateAsync(chatId, user, AppScreen.Settings, ct);
    }

    public async Task HandleEditTitleAsync(long chatId, AppUser user, string text, CancellationToken ct)
    {
        var session = sessions.Get(user.TelegramId);
        if (!session.TargetTaskId.HasValue || text.Length < 2) return;

        await tasks.UpdateTitleAsync(session.TargetTaskId.Value, text, ct);
        var taskId = session.TargetTaskId.Value;
        session.ResetFlow();
        await NavigateAsync(chatId, user, AppScreen.TaskDetail, ct, taskId);
    }

    public async Task HandleAdminRoleChangeAsync(long chatId, AppUser admin, int targetId, UserRole role, CancellationToken ct)
    {
        var (ok, error) = await users.SetUserRoleAsync(targetId, role, admin.Id, ct);
        if (!ok)
        {
            await SendPushAsync(chatId, CyberXTheme.Error(error ?? "Ошибка"), ct);
            return;
        }

        await NavigateAsync(chatId, admin, AppScreen.AdminPanel, ct);
    }

    public async Task HandleAdminActiveChangeAsync(long chatId, AppUser admin, int targetId, bool active, CancellationToken ct)
    {
        var (ok, error) = await users.SetUserActiveAsync(targetId, active, admin.Id, ct);
        if (!ok)
        {
            await SendPushAsync(chatId, CyberXTheme.Error(error ?? "Ошибка"), ct);
            return;
        }

        await NavigateAsync(chatId, admin, AppScreen.AdminPanel, ct);
    }

    public async Task HandleAddWorkerAsync(long chatId, AppUser manager, string text, CancellationToken ct)
    {
        if (!long.TryParse(text.Trim(), out var workerTelegramId))
        {
            await SendPushAsync(chatId, CyberXTheme.Error("Введите числовой Telegram ID"), ct);
            return;
        }

        var worker = await users.AddWorkerAsync(workerTelegramId, null, "Сотрудник", ct);
        sessions.Get(manager.TelegramId).ResetFlow();

        if (worker is null)
        {
            await SendPushAsync(chatId, CyberXTheme.Error("Этот пользователь уже менеджер"), ct);
            return;
        }

        await NavigateAsync(chatId, manager, AppScreen.Workers, ct);
        await SendPushAsync(workerTelegramId, MessageTemplates.WorkerWelcome(), ct);
        await bot.SendMessage(workerTelegramId, "⬛", replyMarkup: KeyboardFactory.AppTabBar(worker), cancellationToken: ct);
    }

    public async Task CancelFlowAsync(long chatId, AppUser user, CancellationToken ct)
    {
        sessions.Get(user.TelegramId).ResetFlow();
        await NavigateAsync(chatId, user, AppScreen.Home, ct);
    }

    public async Task ShowWeeklyReportAsync(long chatId, AppUser user, CancellationToken ct)
    {
        var report = await tasks.GetWeeklyReportAsync(ct);
        await RenderPanelAsync(chatId, user.TelegramId, AppScreens.WeeklyReport(report),
            KeyboardFactory.ManagerHubButtons(), false, ct);
    }

    public async Task SendPushAsync(long chatId, string text, CancellationToken ct) =>
        await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ct);

    private async Task PromptAssigneeAsync(long chatId, AppUser user, CancellationToken ct)
    {
        var workers = await users.GetWorkersAsync(ct);
        if (workers.Count == 0)
        {
            sessions.Get(user.TelegramId).ResetFlow();
            await SendPushAsync(chatId, CyberXTheme.Warning("Сначала добавьте сотрудников"), ct);
            await NavigateAsync(chatId, user, AppScreen.Workers, ct);
            return;
        }

        await RenderPanelAsync(chatId, user.TelegramId,
            AppScreens.AddTaskPrompt("👤 Выберите сотрудника:"),
            KeyboardFactory.WorkerPicker(workers), false, ct);
    }

    private async Task RenderPanelAsync(
        long chatId, long telegramId, string text, InlineKeyboardMarkup? inline, bool forceNew, CancellationToken ct)
    {
        var session = sessions.Get(telegramId);

        if (!forceNew && session.PanelMessageId.HasValue)
        {
            try
            {
                await bot.EditMessageText(chatId, session.PanelMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: inline, cancellationToken: ct);
                return;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Panel edit failed");
                session.PanelMessageId = null;
            }
        }

        var msg = await bot.SendMessage(chatId, text, parseMode: ParseMode.Html,
            replyMarkup: inline, cancellationToken: ct);
        session.PanelMessageId = msg.MessageId;
    }

    private async Task<(string Text, InlineKeyboardMarkup? Inline)> BuildScreenAsync(
        AppUser user, AppScreen screen, int? taskId, CancellationToken ct)
    {
        var isManager = user.IsManagerOrAdmin();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(user.UtcOffsetMinutes));

        if (screen == AppScreen.TaskDetail && taskId.HasValue)
        {
            var task = await tasks.GetByIdAsync(taskId.Value, ct);
            if (task is null)
                return (CyberXTheme.Error("Задача не найдена"),
                    new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("◀️ К списку", "nav:tasks")]]));

            return (AppScreens.TaskDetail(task, isManager), KeyboardFactory.TaskDetailButtons(task, isManager));
        }

        return screen switch
        {
            AppScreen.Home => (
                AppScreens.Home(user, await tasks.GetDayStatsAsync(user.Id, isManager, ct)),
                isManager ? KeyboardFactory.ManagerHubButtons() : null),

            AppScreen.Tasks => await BuildTasksScreen(user, today, isManager, ct),

            AppScreen.Leaderboard => (
                AppScreens.Leaderboard(await tasks.GetWeeklyLeaderboardAsync(ct)), null),

            AppScreen.Settings => (
                AppScreens.Settings(user),
                isManager ? KeyboardFactory.SettingsButtons() : KeyboardFactory.WorkerSettingsButtons()),

            AppScreen.ManagerHub => (
                AppScreens.ManagerHub(await tasks.GetDayStatsAsync(user.Id, true, ct)),
                KeyboardFactory.ManagerHubButtons()),

            AppScreen.Archive => (
                AppScreens.Archive(await tasks.GetArchiveAsync(null, true, ct)),
                KeyboardFactory.ManagerHubButtons()),

            AppScreen.Workers => await BuildWorkersScreen(user, ct),

            AppScreen.AdminPanel => await BuildAdminScreen(user, ct),

            _ => (
                AppScreens.Home(user, await tasks.GetDayStatsAsync(user.Id, isManager, ct)),
                isManager ? KeyboardFactory.ManagerHubButtons() : null)
        };
    }

    private async Task<(string, InlineKeyboardMarkup?)> BuildTasksScreen(
        AppUser user, DateOnly today, bool isManager, CancellationToken ct)
    {
        var list = await tasks.GetTodayTasksAsync(user.Id, isManager, ct);
        var stats = await tasks.GetDayStatsAsync(user.Id, isManager, ct);
        return (AppScreens.Tasks(user, today, stats, list, isManager), KeyboardFactory.TaskListButtons(list));
    }

    private async Task<(string, InlineKeyboardMarkup?)> BuildWorkersScreen(AppUser user, CancellationToken ct)
    {
        sessions.Get(user.TelegramId).Step = ConversationStep.AddWorkerTelegramId;
        return (
            AppScreens.Workers(await users.GetWorkersAsync(ct)),
            KeyboardFactory.ManagerHubButtons());
    }

    private async Task<(string, InlineKeyboardMarkup?)> BuildAdminScreen(AppUser user, CancellationToken ct)
    {
        var all = await users.GetAllUsersAsync(ct);
        return (AppScreens.AdminPanel(all), KeyboardFactory.AdminUserButtons(all, user.Id));
    }

    private static InlineKeyboardMarkup CancelFlow() =>
        new([[InlineKeyboardButton.WithCallbackData("❌ Отмена", "flow:cancel")]]);
}
