namespace CyberXTasks.Bot.UI;

/// <summary>
/// Визуальная система CyberX: белый Cyber + красный X.
/// Telegram не поддерживает цвет текста — используем ⬜/🟥 и Unicode-блоки.
/// </summary>
public static class CyberXTheme
{
    public const string WhiteMark = "⬜";
    public const string RedMark = "🟥";
    public const string Accent = "⚡";
    public const string Dark = "⬛";

    public static string Logo =>
        $"{WhiteMark}<b>Cyber</b>{RedMark}<b>X</b>";

    public static string LogoWithTagline =>
        $"{Logo} <i>Tasks</i>";

    public static string Banner =>
        $"""
        {Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}
        {Accent} {LogoWithTagline}
        {ThinDivider}
        """;

    public static string ThinDivider => "┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄";

    public static string ThickDivider => "▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰";

    public static string Header(string title) =>
        $"""
        {ThickDivider}
        {Accent} {Logo} · <b>{title}</b>
        {ThickDivider}
        """;

    public static string Card(string title, string body) =>
        $"""
        {WhiteMark} <b>{title}</b>
        {ThinDivider}
        {body}
        """;

    public static string ProgressBar(int done, int total, int width = 12)
    {
        if (total <= 0)
            return $"{WhiteMark}{new string('░', width)} <i>0%</i>";

        var filled = (int)Math.Round((double)done / total * width);
        filled = Math.Clamp(filled, 0, width);
        var bar = $"{RedMark}{new string('█', filled)}{WhiteMark}{new string('░', width - filled)}";
        var percent = (int)Math.Round((double)done / total * 100);
        return $"{bar} <b>{percent}%</b>";
    }

    public static string StatLine(string icon, string label, int value, string accent = WhiteMark) =>
        $"{accent} {icon} <b>{label}:</b> {value}";

    public static string Success(string text) =>
        $"{WhiteMark}✅ <b>{text}</b>";

    public static string Error(string text) =>
        $"{RedMark}❌ <b>{text}</b>";

    public static string Warning(string text) =>
        $"{RedMark}⚠️ <b>{text}</b>";

    public static string Info(string text) =>
        $"{WhiteMark}ℹ️ {text}";

    public static string Footer =>
        $"""
        
        {ThinDivider}
        <i>{Logo} · корпоративная система задач</i>
        """;

    public static string AppFrame(string screenTitle, string body) =>
        $"""
        {Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}{Dark}
        {Accent} {Logo} <b>APP</b>
        {ThinDivider}
        <b>📱 {screenTitle}</b>
        {ThinDivider}
        
        {body}
        {Footer}
        """;

    public static string PriorityLabel(Models.TaskPriority priority) => priority switch
    {
        Models.TaskPriority.Urgent => "🟥 Срочно",
        Models.TaskPriority.High => "🟧 Высокий",
        Models.TaskPriority.Normal => "⬜ Обычный",
        _ => "⬜ Низкий"
    };

    public static string RankMedal(int place) => place switch
    {
        1 => "🥇",
        2 => "🥈",
        3 => "🥉",
        _ => $"#{place}"
    };
}
