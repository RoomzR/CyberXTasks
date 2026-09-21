using System.Collections.Concurrent;
using CyberXTasks.Bot.Models;

namespace CyberXTasks.Bot.Services;

public class SessionService
{
    private readonly ConcurrentDictionary<long, UserSession> _sessions = new();

    public UserSession Get(long telegramId) =>
        _sessions.GetOrAdd(telegramId, _ => new UserSession());

    public void Reset(long telegramId) => Get(telegramId).Reset();
}
