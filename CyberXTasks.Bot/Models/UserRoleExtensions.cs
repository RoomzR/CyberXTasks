namespace CyberXTasks.Bot.Models;

public static class UserRoleExtensions
{
    public static bool IsManagerOrAdmin(this UserRole role) =>
        role is UserRole.Manager or UserRole.Admin;

    public static bool IsManagerOrAdmin(this AppUser user) =>
        user.Role.IsManagerOrAdmin();

    public static bool IsAdmin(this AppUser user) =>
        user.Role == UserRole.Admin;
}
