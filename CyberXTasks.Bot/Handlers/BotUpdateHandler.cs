using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.Models;
using CyberXTasks.Bot.Services;
using CyberXTasks.Bot.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CyberXTasks.Bot.Handlers;

/// <summary>
/// Бот — только лаунчер Mini App и push-уведомления.
/// Весь функционал (задачи, команда, админ) — в Telegram Mini App.
/// </summary>
public class BotUpdateHandler(
    ITelegramBotClient bot,
    UserService users,
    IOptions<BotSettings> settings,
    ILogger<BotUpdateHandler> logger) : IUpdateHandler
{
    public async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Message is { Text: not null } message)
                await HandleMessageAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling update");
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        logger.LogError(exception, "Telegram polling error from {Source}", source);
        return Task.CompletedTask;
    }

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        if (message.From is null || message.Text is null)
            return;

        var text = message.Text.Trim();
        if (!text.StartsWith('/'))
            return;

        var command = text.Split(' ')[0].Split('@')[0].ToLowerInvariant();
        if (command is not ("/start" or "/app"))
            return;

        var chatId = message.Chat.Id;
        var user = await users.RegisterOrUpdateAsync(
            message.From.Id, message.From.Username, message.From.FirstName, message.From.LastName, ct);

        if (!user.IsActive)
        {
            await bot.SendMessage(chatId, NotifyTemplates.NotRegistered(), parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        var webAppUrl = settings.Value.WebAppUrl?.TrimEnd('/') ?? "";
        if (string.IsNullOrWhiteSpace(webAppUrl) || webAppUrl.Contains("YOUR_DOMAIN"))
        {
            await bot.SendMessage(chatId, NotifyTemplates.ConfigRequired(), parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        var keyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithWebApp("Открыть CyberX Tasks", new WebAppInfo { Url = webAppUrl })]
        ]);

        await bot.SendMessage(
            chatId,
            NotifyTemplates.Welcome(user),
            parseMode: ParseMode.Html,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }
}
