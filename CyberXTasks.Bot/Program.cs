using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.Data;
using CyberXTasks.Bot.Handlers;
using CyberXTasks.Bot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var botSection = context.Configuration.GetSection("BotSettings");
        services.Configure<BotSettings>(botSection);

        var settings = botSection.Get<BotSettings>() ?? new BotSettings();
        var token = context.Configuration["BOT_TOKEN"] ?? settings.BotToken;
        var dbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), settings.DatabasePath));

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));

        services.AddScoped<UserService>();
        services.AddScoped<TaskService>();
        services.AddSingleton<NotificationHelper>();
        services.AddScoped<BotUpdateHandler>();
        services.AddSingleton<ScopedBotUpdateHandler>();

        services.AddHostedService<TelegramBotHostedService>();
        services.AddHostedService<DailyNotificationService>();
        services.AddHostedService<ReminderService>();
        services.AddHostedService<WeeklyReportService>();
    })
    .Build();

var dbSettings = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<BotSettings>>().Value;
var fullDbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), dbSettings.DatabasePath));
Directory.CreateDirectory(Path.GetDirectoryName(fullDbPath)!);

await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
}

await host.RunAsync();
