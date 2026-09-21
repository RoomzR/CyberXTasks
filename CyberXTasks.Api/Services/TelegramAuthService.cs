using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CyberXTasks.Bot.Configuration;

namespace CyberXTasks.Api.Services;

public class TelegramAuthService(IOptions<BotSettings> settings)
{
    public bool TryValidateInitData(string initData, out long telegramId, out string? firstName)
    {
        telegramId = 0;
        firstName = null;

        if (string.IsNullOrWhiteSpace(initData))
            return false;

        var parsed = ParseQueryString(initData);
        if (!parsed.TryGetValue("hash", out var hash) || string.IsNullOrEmpty(hash))
            return false;

        var dataCheck = string.Join('\n', parsed
            .Where(kv => kv.Key != "hash")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(settings.Value.BotToken));

        var computed = HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheck));
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHex),
                Encoding.UTF8.GetBytes(hash.ToLowerInvariant())))
            return false;

        if (!parsed.TryGetValue("user", out var userJson) || string.IsNullOrEmpty(userJson))
            return false;

        using var doc = JsonDocument.Parse(userJson);
        telegramId = doc.RootElement.GetProperty("id").GetInt64();
        firstName = doc.RootElement.TryGetProperty("first_name", out var fn)
            ? fn.GetString()
            : null;

        return true;
    }

    private static Dictionary<string, string> ParseQueryString(string initData)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
        }
        return result;
    }
}
