using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.UI;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CyberXTasks.Bot.Services;

public class NotificationHelper(ITelegramBotClient bot, IOptions<BotSettings> settings)
{
    public async Task SendWithAppButtonAsync(long chatId, string text, CancellationToken ct)
    {
        var url = settings.Value.WebAppUrl;
        InlineKeyboardMarkup? keyboard = null;

        if (!string.IsNullOrWhiteSpace(url) && !url.Contains("YOUR_DOMAIN"))
        {
            keyboard = new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithWebApp("Открыть приложение", new WebAppInfo { Url = url })]
            ]);
        }

        await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: ct);
    }

    public Task SendRawAsync(long chatId, string text, InlineKeyboardMarkup? keyboard, CancellationToken ct) =>
        bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: keyboard, cancellationToken: ct);
}
