using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace CyberXTasks.Bot.Services;

public class TelegramBotHostedService(
    ITelegramBotClient bot,
    ScopedBotUpdateHandler handler,
    IOptions<BotSettings> settings,
    ILogger<TelegramBotHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Value.BotToken) || settings.Value.BotToken == "YOUR_BOT_TOKEN_HERE")
        {
            logger.LogCritical("Bot token is not configured. Set BotSettings:BotToken in appsettings.json or BOT_TOKEN env variable.");
            return;
        }

        var me = await bot.GetMe(stoppingToken);
        logger.LogInformation("CyberX Tasks bot started as @{Username}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = []
        };

        bot.StartReceiving(handler, receiverOptions, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Bot stopping...");
        }
    }
}
