using System.Net;
using System.Text;
using CyberXTasks.Bot.Models;

namespace CyberXTasks.Bot.UI;

public static class MessageTemplates
{
    public static string Welcome(AppUser user, DayStats? stats = null) =>
        $"""
        {CyberXTheme.Banner}
        
        {CyberXTheme.Card("Добро пожаловать", $"""
            Привет, <b>{Escape(user.FirstName)}</b>! {WhiteHand()}
            
            {RoleBadge(user.Role)}
            
            {CyberXTheme.ThinDivider}
            🔔 Уведомления · <b>{user.NotificationTime:HH\\:mm}</b>
            🌍 Часовой пояс · <b>UTC{(user.UtcOffsetMinutes >= 0 ? "+" : "")}{user.UtcOffsetMinutes / 60}</b>
            """)}
        
        {(stats is not null ? Dashboard(stats) + "\n" : "")}
        {CyberXTheme.Info("Выберите действие в меню ниже")} ↓
        {CyberXTheme.Footer}
        """;

    public static string Dashboard(DayStats stats) =>
        CyberXTheme.Card("Дашборд на сегодня", $"""
            {CyberXTheme.StatLine("🟡", "Ожидают", stats.Pending)}
            {CyberXTheme.StatLine("🔵", "В работе", stats.InProgress, CyberXTheme.WhiteMark)}
            {CyberXTheme.StatLine("✅", "Выполнено", stats.CompletedToday, CyberXTheme.RedMark)}
            
            {CyberXTheme.ThinDivider}
            📊 Прогресс дня:
            {CyberXTheme.ProgressBar(stats.CompletedToday, Math.Max(stats.ProgressTotal, 1))}
            """);

    public static string NotRegistered() =>
        $"""
        {CyberXTheme.Header("Доступ ограничен")}
        
        {CyberXTheme.Warning("Вы не зарегистрированы")}
        
        {CyberXTheme.Card("Что делать", """
            Попросите менеджера добавить вас в систему.
            Сообщите свой Telegram ID — его можно узнать у @userinfobot
            """)}
        {CyberXTheme.Footer}
        """;

    public static string TaskListHeader(DateOnly date, DayStats? stats = null, bool isDailyDigest = false)
    {
        var title = isDailyDigest ? "Утренняя рассылка" : "Мои задачи";
        var sb = new StringBuilder();
        sb.AppendLine(CyberXTheme.Header(title));
        sb.AppendLine();
        sb.AppendLine($"{CyberXTheme.RedMark} 📅 <b>{date:dd.MM.yyyy}</b> · {DayName(date)}");

        if (stats is not null)
        {
            sb.AppendLine();
            sb.AppendLine(Dashboard(stats));
        }

        if (isDailyDigest)
            sb.AppendLine($"{CyberXTheme.WhiteMark} Доброе утро! Ваш список на сегодня:");

        return sb.ToString().TrimEnd();
    }

    public static string NoTasksToday() =>
        $"""
        {CyberXTheme.Card("Свободный день", """
            📭 На сегодня задач нет.
            
            Отдохните или создайте новую задачу.
            """)}
        """;

    public static string FormatTaskList(IReadOnlyList<WorkTask> tasks)
    {
        if (tasks.Count == 0)
            return NoTasksToday();

        var sb = new StringBuilder();
        for (var i = 0; i < tasks.Count; i++)
        {
            sb.AppendLine(FormatTaskLine(i + 1, tasks[i]));
            if (i < tasks.Count - 1)
                sb.AppendLine(CyberXTheme.ThinDivider);
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatTaskCard(int number, WorkTask task, bool showAssignee = false)
    {
        var status = StatusBlock(task.Status);
        var time = task.DueTime.HasValue
            ? $"{CyberXTheme.RedMark} ⏰ <b>{task.DueTime:HH\\:mm}</b>"
            : $"{CyberXTheme.WhiteMark} ⏰ без срока";

        var assignee = showAssignee && task.AssignedTo is not null
            ? $"\n{CyberXTheme.WhiteMark} 👤 <b>{Escape(task.AssignedTo.FirstName)}</b>"
            : "";

        return $"""
            {CyberXTheme.Card($"Задача #{number}", $"""
                {status}
                {CyberXTheme.WhiteMark} 📝 {Escape(task.Title)}
                {time}{assignee}
                """)}
            """;
    }

    public static string FormatTaskLine(int number, WorkTask task)
    {
        var status = task.Status switch
        {
            WorkTaskStatus.Pending => "🟡 Ожидает",
            WorkTaskStatus.Accepted => "🔵 В работе",
            WorkTaskStatus.Completed => "✅ Готово",
            WorkTaskStatus.Deleted => "🗑 Удалено",
            _ => "❓"
        };

        var time = task.DueTime.HasValue ? $" · ⏰ <b>{task.DueTime:HH\\:mm}</b>" : "";
        return $"{CyberXTheme.WhiteMark} <b>#{number}</b> {status} — {Escape(task.Title)}{time}";
    }

    public static string ArchiveHeader() =>
        $"{CyberXTheme.Header("Архив")}\n\n{CyberXTheme.RedMark} 📦 <b>Последние 30 записей</b>";

    public static string Settings(AppUser user) =>
        $"""
        {CyberXTheme.Header("Настройки")}
        
        {CyberXTheme.Card("Уведомления", $"""
            {CyberXTheme.WhiteMark} 🔔 Время · <b>{user.NotificationTime:HH\\:mm}</b>
            {CyberXTheme.WhiteMark} 🌍 Часовой пояс · <b>UTC{(user.UtcOffsetMinutes >= 0 ? "+" : "")}{user.UtcOffsetMinutes / 60}</b>
            """)}
        
        {CyberXTheme.Info("Нажмите кнопку, чтобы изменить время рассылки")}
        {CyberXTheme.Footer}
        """;

    public static string ManagerPanel(DayStats? stats = null) =>
        $"""
        {CyberXTheme.Header("Панель менеджера")}
        
        {CyberXTheme.Card("Управление", """
            ⬜ Создание и редактирование задач
            🟥 Назначение сотрудникам
            ⬜ Добавление в команду
            🟥 Просмотр архива и статистики
            """)}
        {(stats is not null ? "\n" + Dashboard(stats) : "")}
        {CyberXTheme.Footer}
        """;

    public static string WorkerPanel(DayStats? stats = null) =>
        $"""
        {CyberXTheme.Header("Панель сотрудника")}
        
        {CyberXTheme.Card("Инструменты", """
            ⬜ Просмотр задач на сегодня
            🟥 Принятие в работу
            ⬜ Отметка о выполнении
            """)}
        {(stats is not null ? "\n" + Dashboard(stats) : "")}
        {CyberXTheme.Footer}
        """;

    public static string TaskAccepted(string title) =>
        $"""
        {CyberXTheme.Success("Задача принята в работу")}
        
        {CyberXTheme.Card("Детали", $"{CyberXTheme.RedMark} 📝 <b>{Escape(title)}</b>")}
        {CyberXTheme.Info("Удачи! Когда закончите — нажмите ✅ Выполнить")}
        """;

    public static string TaskCompleted(string title, DayStats stats) =>
        $"""
        {CyberXTheme.Header("Миссия выполнена")}
        
        🎉🟥🎉
        
        {CyberXTheme.Success("Отличная работа!")}
        {CyberXTheme.Card("Задача", $"{CyberXTheme.WhiteMark} {Escape(title)}")}
        
        {Dashboard(stats)}
        {CyberXTheme.Footer}
        """;

    public static string TaskCreated(WorkTask task) =>
        $"""
        {CyberXTheme.Success("Задача создана")}
        
        {FormatTaskCard(1, task, showAssignee: true)}
        {CyberXTheme.Footer}
        """;

    public static string NewTaskAssigned(WorkTask task) =>
        $"""
        {CyberXTheme.Header("Новое задание")}
        
        {CyberXTheme.RedMark} 📥 Вам назначена задача!
        
        {FormatTaskCard(1, task)}
        {CyberXTheme.Footer}
        """;

    public static string WorkerAdded(long telegramId) =>
        $"""
        {CyberXTheme.Success("Сотрудник добавлен")}
        
        {CyberXTheme.Card("Данные", $"{CyberXTheme.WhiteMark} ID · <code>{telegramId}</code>")}
        {CyberXTheme.Footer}
        """;

    public static string WorkerWelcome() =>
        $"""
        {CyberXTheme.Banner}
        
        {CyberXTheme.Success("Вы в команде CyberX!")}
        
        {CyberXTheme.Info("Нажмите /start чтобы открыть главное меню")}
        {CyberXTheme.Footer}
        """;

    public static string Prompt(string title, string hint) =>
        $"""
        {CyberXTheme.Header(title)}
        
        {CyberXTheme.Info(hint)}
        """;

    public static string Header(string title) => CyberXTheme.Header(title);

    public static string RoleBadge(UserRole role) => role switch
    {
        UserRole.Admin => $"{CyberXTheme.RedMark} 🛡 <b>Роль</b> · Администратор",
        UserRole.Manager => $"{CyberXTheme.RedMark} 👑 <b>Роль</b> · Менеджер",
        _ => $"{CyberXTheme.WhiteMark} 👷 <b>Роль</b> · Сотрудник"
    };

    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Admin => "Админ",
        UserRole.Manager => "Менеджер",
        _ => "Сотрудник"
    };

    public static string Escape(string text) => WebUtility.HtmlEncode(text);

    private static string StatusBlock(WorkTaskStatus status) => status switch
    {
        WorkTaskStatus.Pending => $"{CyberXTheme.WhiteMark} 🟡 <b>Ожидает принятия</b>",
        WorkTaskStatus.Accepted => $"{CyberXTheme.RedMark} 🔵 <b>В работе</b>",
        WorkTaskStatus.Completed => $"{CyberXTheme.WhiteMark} ✅ <b>Выполнено</b>",
        WorkTaskStatus.Deleted => $"{CyberXTheme.RedMark} 🗑 <b>Удалено</b>",
        _ => "❓"
    };

    private static string WhiteHand() => "👋";

    private static string DayName(DateOnly date)
    {
        var day = date.DayOfWeek switch
        {
            DayOfWeek.Monday => "Понедельник",
            DayOfWeek.Tuesday => "Вторник",
            DayOfWeek.Wednesday => "Среда",
            DayOfWeek.Thursday => "Четверг",
            DayOfWeek.Friday => "Пятница",
            DayOfWeek.Saturday => "Суббота",
            DayOfWeek.Sunday => "Воскресенье",
            _ => ""
        };
        return day;
    }
}
