using CyberXTasks.Bot.Configuration;
using CyberXTasks.Bot.Data;
using CyberXTasks.Bot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CyberXTasks.Bot.Services;

public class UserService(AppDbContext db, IOptions<BotSettings> settings)
{
    private readonly BotSettings _settings = settings.Value;

    public async Task<AppUser?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default) =>
        await db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId && u.IsActive, ct);

    public async Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await db.Users.FindAsync([id], ct);

    public async Task<List<AppUser>> GetWorkersAsync(CancellationToken ct = default) =>
        await db.Users
            .Where(u => u.Role == UserRole.Worker && u.IsActive)
            .OrderBy(u => u.LastName ?? u.FirstName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(ct);

    public async Task<List<AppUser>> GetAllUsersAsync(CancellationToken ct = default) =>
        await db.Users
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.LastName ?? u.FirstName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(ct);

    public async Task<AppUser> RegisterOrUpdateAsync(
        long telegramId,
        string? username,
        string firstName,
        string? lastName,
        CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);
        if (user is not null)
        {
            user.Username = username;
            if (!user.ProfileCompleted)
            {
                user.FirstName = firstName;
                user.LastName = lastName;
            }

            if (_settings.BootstrapAdminIds.Contains(telegramId) && user.Role != UserRole.Admin)
            {
                user.Role = UserRole.Admin;
                user.IsActive = true;
            }

            await db.SaveChangesAsync(ct);
            return user;
        }

        var isBootstrapAdmin = _settings.BootstrapAdminIds.Contains(telegramId);
        var isBootstrapManager = _settings.BootstrapManagerIds.Contains(telegramId);

        UserRole role;
        bool isActive;
        if (isBootstrapAdmin)
        {
            role = UserRole.Admin;
            isActive = true;
        }
        else if (isBootstrapManager)
        {
            role = UserRole.Manager;
            isActive = true;
        }
        else
        {
            role = UserRole.Worker;
            isActive = false;
        }

        user = new AppUser
        {
            TelegramId = telegramId,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            NotificationTime = new TimeOnly(9, 10),
            DefaultReminderMinutes = _settings.DefaultReminderMinutes,
            UtcOffsetMinutes = _settings.DefaultUtcOffsetMinutes,
            IsActive = isActive
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<AppUser?> AddWorkerAsync(long telegramId, string? username, string firstName, CancellationToken ct = default)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);
        if (existing is not null)
        {
            if (existing.Role is UserRole.Manager or UserRole.Admin)
                return null;

            existing.IsActive = true;
            existing.Username = username ?? existing.Username;
            existing.FirstName = string.IsNullOrWhiteSpace(firstName) ? existing.FirstName : firstName;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var worker = new AppUser
        {
            TelegramId = telegramId,
            Username = username,
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "Сотрудник" : firstName,
            Role = UserRole.Worker,
            NotificationTime = new TimeOnly(9, 10),
            DefaultReminderMinutes = _settings.DefaultReminderMinutes,
            UtcOffsetMinutes = _settings.DefaultUtcOffsetMinutes,
            IsActive = true
        };

        db.Users.Add(worker);
        await db.SaveChangesAsync(ct);
        return worker;
    }

    public bool IsOwnerAdmin(AppUser user) =>
        _settings.BootstrapAdminIds.Contains(user.TelegramId);

    public async Task<AppUser> UpdateProfileAsync(
        int userId,
        string lastName,
        string firstName,
        string? middleName,
        CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found");

        user.LastName = lastName.Trim();
        user.FirstName = firstName.Trim();
        user.MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim();
        user.ProfileCompleted = true;
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<(bool Ok, string? Error)> SetUserRoleAsync(int targetUserId, UserRole newRole, int actorId, CancellationToken ct = default)
    {
        var actor = await db.Users.FindAsync([actorId], ct);
        var target = await db.Users.FindAsync([targetUserId], ct);
        if (actor is null || target is null) return (false, "Пользователь не найден");
        if (target.Id == actorId) return (false, "Нельзя изменить свою роль");

        var actorIsOwner = IsOwnerAdmin(actor);
        var targetIsOwner = IsOwnerAdmin(target);

        if (!actorIsOwner)
        {
            if (targetIsOwner)
                return (false, "Нельзя изменить роль главного администратора");
            if (target.Role == UserRole.Admin)
                return (false, "Нельзя изменить роль администратора");
            if (newRole == UserRole.Admin)
                return (false, "Только главный администратор может назначать админов");
        }

        target.Role = newRole;
        if (newRole is UserRole.Manager or UserRole.Admin)
            target.IsActive = true;

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetUserActiveAsync(int targetUserId, bool isActive, int actorId, CancellationToken ct = default)
    {
        var actor = await db.Users.FindAsync([actorId], ct);
        var target = await db.Users.FindAsync([targetUserId], ct);
        if (actor is null || target is null) return (false, "Пользователь не найден");
        if (target.Id == actorId) return (false, "Нельзя деактивировать себя");

        var actorIsOwner = IsOwnerAdmin(actor);
        var targetIsOwner = IsOwnerAdmin(target);

        if (!actorIsOwner)
        {
            if (targetIsOwner)
                return (false, "Нельзя деактивировать главного администратора");
            if (target.Role == UserRole.Admin)
                return (false, "Нельзя деактивировать администратора");
        }
        else if (targetIsOwner)
        {
            return (false, "Нельзя деактивировать главного администратора");
        }

        target.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task UpdateNotificationTimeAsync(int userId, TimeOnly time, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found");

        user.NotificationTime = time;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AppUser>> GetUsersForNotificationAsync(CancellationToken ct = default) =>
        await db.Users.Where(u => u.IsActive).ToListAsync(ct);

    public async Task MarkNotifiedAsync(int userId, DateOnly date, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return;

        user.LastNotificationDate = date;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateDefaultReminderMinutesAsync(int userId, int minutes, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found");

        user.DefaultReminderMinutes = minutes;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AppUser>> GetManagersAsync(CancellationToken ct = default) =>
        await db.Users.Where(u => u.Role == UserRole.Manager && u.IsActive).ToListAsync(ct);

    public async Task<List<AppUser>> GetManagementUsersAsync(CancellationToken ct = default) =>
        await db.Users
            .Where(u => u.IsActive && (u.Role == UserRole.Manager || u.Role == UserRole.Admin))
            .ToListAsync(ct);

    public async Task MarkWeeklyReportSentAsync(int userId, DateOnly weekEnd, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return;

        user.LastWeeklyReportDate = weekEnd;
        await db.SaveChangesAsync(ct);
    }
}
