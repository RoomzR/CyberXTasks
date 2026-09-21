using System.Net;
using System.Text;
using CyberXTasks.Bot.Models;

namespace CyberXTasks.Bot.UI;

public static class AppScreens
{
    public static class Tabs
    {
        public const string Home = "🏠 Главная";
        public const string Tasks = "📋 Задачи";
        public const string Rating = "🏆 Рейтинг";
        public const string Create = "➕ Создать";
        public const string Team = "👥 Команда";
        public const string Settings = "⚙️ Настройки";
        public const string Admin = "👑 Админ";
    }

    public static string Home(AppUser user, DayStats stats) =>
        CyberXTheme.AppFrame("Главная", $"""
            Привет, <b>{E(user.FirstName)}</b> 👋
            
            {MessageTemplates.RoleBadge(user.Role)}
            
            {MessageTemplates.Dashboard(stats)}
            
            {CyberXTheme.Info("Используйте панель вкладок внизу — как в приложении")}
            """);

    public static string Tasks(AppUser user, DateOnly today, DayStats stats, IReadOnlyList<WorkTask> tasks, bool managerView) =>
        CyberXTheme.AppFrame("Задачи", $"""
            📅 <b>{today:dd.MM.yyyy}</b>
            
            {CyberXTheme.StatLine("🟡", "Ожидают", stats.Pending)}
            {CyberXTheme.StatLine("🔵", "В работе", stats.InProgress, CyberXTheme.RedMark)}
            {CyberXTheme.ProgressBar(stats.CompletedToday, Math.Max(stats.ProgressTotal, 1))}
            
            {CyberXTheme.ThinDivider}
            {(tasks.Count == 0 ? "📭 Задач нет. Нажмите кнопки ниже." : "Выберите задачу:")}
            """);

    public static string TaskDetail(WorkTask task, bool isManager) =>
        CyberXTheme.AppFrame($"Задача #{task.Id}", $"""
            {CyberXTheme.PriorityLabel(task.Priority)}
            {StatusLine(task.Status)}
            
            📝 <b>{E(task.Title)}</b>
            
            {(task.DueTime.HasValue
                ? $"{CyberXTheme.RedMark} ⏰ <b>{task.DueTime:HH\\:mm}</b>"
                : $"{CyberXTheme.WhiteMark} ⏰ без срока")}
            {(task.DueTime.HasValue && task.ReminderMinutesBefore.HasValue
                ? $"{CyberXTheme.WhiteMark} 🔔 напоминание за <b>{task.ReminderMinutesBefore}</b> мин"
                : "")}
            {(isManager && task.AssignedTo is not null
                ? $"{CyberXTheme.WhiteMark} 👤 {E(task.AssignedTo.FirstName)}"
                : "")}
            """);

    public static string Leaderboard(IReadOnlyList<WorkerRank> ranks) =>
        CyberXTheme.AppFrame("Рейтинг недели", ranks.Count == 0
            ? "📭 Пока нет выполненных задач за эту неделю."
            : string.Join("\n", ranks.Select(r =>
                $"{CyberXTheme.RankMedal(r.Place)} <b>{E(r.Name)}</b> — {r.CompletedCount} задач · {r.Score} очков")));

    public static string WeeklyReport(WeeklyReportData report) =>
        CyberXTheme.AppFrame("Еженедельный отчёт", $"""
            📆 <b>{report.WeekStart:dd.MM}</b> — <b>{report.WeekEnd:dd.MM}</b>
            
            {CyberXTheme.StatLine("✅", "Выполнено", report.TotalCompleted, CyberXTheme.RedMark)}
            {CyberXTheme.StatLine("🟡", "Ожидают", report.TotalPending)}
            {CyberXTheme.StatLine("🔵", "В работе", report.TotalInProgress, CyberXTheme.WhiteMark)}
            
            {CyberXTheme.ThinDivider}
            <b>🏆 Топ сотрудников</b>
            {(report.Leaderboard.Count == 0
                ? "— нет данных"
                : string.Join("\n", report.Leaderboard.Take(5).Select(r =>
                    $"{CyberXTheme.RankMedal(r.Place)} {E(r.Name)} · {r.Score} очков")))}
            """);

    public static string Settings(AppUser user) =>
        CyberXTheme.AppFrame("Настройки", $"""
            {CyberXTheme.WhiteMark} 🔔 Утренняя рассылка · <b>{user.NotificationTime:HH\\:mm}</b>
            {CyberXTheme.RedMark} ⏱ Напоминание · <b>за {user.DefaultReminderMinutes} мин</b> до дедлайна
            
            Нажмите кнопки ниже для изменения.
            """);

    public static string ManagerHub(DayStats stats) =>
        CyberXTheme.AppFrame("Менеджер", $"""
            {MessageTemplates.Dashboard(stats)}
            
            ⬜ Создание задач с приоритетом
            🟥 Назначение и контроль команды
            ⬜ Архив и еженедельные отчёты
            """);

    public static string Archive(IReadOnlyList<WorkTask> items) =>
        CyberXTheme.AppFrame("Архив", items.Count == 0
            ? "📭 Архив пуст."
            : string.Join("\n\n", items.Take(15).Select((t, i) =>
                $"{CyberXTheme.WhiteMark} <b>#{i + 1}</b> {CyberXTheme.PriorityLabel(t.Priority)}\n{E(t.Title)}")));

    public static string Workers(IReadOnlyList<AppUser> workers) =>
        CyberXTheme.AppFrame("Команда", workers.Count == 0
            ? "Пока нет сотрудников.\n\nОтправьте Telegram ID для добавления."
            : string.Join("\n", workers.Select(w =>
                $"⬜ <b>{E(w.FirstName)}</b> · <code>{w.TelegramId}</code>")) + "\n\n➕ Отправьте Telegram ID нового сотрудника:");

    public static string AdminPanel(IReadOnlyList<AppUser> allUsers) =>
        CyberXTheme.AppFrame("Администрирование", allUsers.Count == 0
            ? "Пользователей нет."
            : string.Join("\n\n", allUsers.Select(u =>
                $"""
                {MessageTemplates.RoleBadge(u.Role)}
                <b>{E(u.FirstName)}</b> · <code>{u.TelegramId}</code>
                {(u.IsActive ? "✅ Активен" : "⛔ Неактивен")}
                """)));

    public static string AddTaskPrompt(string step) =>
        CyberXTheme.AppFrame("Новая задача", step);

    public static string PushNotification(string title, string body) =>
        $"""
        {CyberXTheme.RedMark} <b>CyberX</b>
        {CyberXTheme.ThinDivider}
        <b>{E(title)}</b>
        {body}
        """;

    public static string ReminderPush(WorkTask task, int minutesLeft) =>
        PushNotification("Напоминание", $"""
            {CyberXTheme.PriorityLabel(task.Priority)}
            📝 {E(task.Title)}
            ⏰ через <b>{minutesLeft}</b> мин ({task.DueTime:HH\\:mm})
            """);

    private static string StatusLine(WorkTaskStatus status) => status switch
    {
        WorkTaskStatus.Pending => $"{CyberXTheme.WhiteMark} 🟡 Ожидает принятия",
        WorkTaskStatus.Accepted => $"{CyberXTheme.RedMark} 🔵 В работе",
        WorkTaskStatus.Completed => $"{CyberXTheme.WhiteMark} ✅ Выполнено",
        _ => $"{CyberXTheme.RedMark} 🗑 Удалено"
    };

    private static string E(string text) => WebUtility.HtmlEncode(text);
}
