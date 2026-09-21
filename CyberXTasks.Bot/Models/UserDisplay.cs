namespace CyberXTasks.Bot.Models;

public static class UserDisplay
{
    public static string FullName(AppUser? user)
    {
        if (user is null) return "—";

        var parts = new[] { user.LastName, user.FirstName, user.MiddleName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        var full = string.Join(" ", parts);
        return string.IsNullOrEmpty(full) ? Fallback(user) : full;
    }

    private static string Fallback(AppUser user) =>
        string.IsNullOrWhiteSpace(user.FirstName) ? "Пользователь" : user.FirstName.Trim();
}
