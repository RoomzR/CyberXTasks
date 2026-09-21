namespace CyberXTasks.Bot.Configuration;

public class BotSettings
{
    public string BotToken { get; set; } = string.Empty;
    public List<long> BootstrapAdminIds { get; set; } = [];
    public List<long> BootstrapManagerIds { get; set; } = [];
    public string DatabasePath { get; set; } = "data/cyberx.db";
    public int DefaultUtcOffsetMinutes { get; set; } = 180;
    public int DefaultReminderMinutes { get; set; } = 15;
    public string WeeklyReportDay { get; set; } = "Friday";
    public string WeeklyReportTime { get; set; } = "17:00";
    public string WebAppUrl { get; set; } = string.Empty;
}
