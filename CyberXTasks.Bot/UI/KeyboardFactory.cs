using CyberXTasks.Bot.Models;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace CyberXTasks.Bot.UI;

public static class KeyboardFactory
{
    public static ReplyKeyboardMarkup AppTabBar(AppUser user)
    {
        if (user.IsManagerOrAdmin())
        {
            var rows = new List<KeyboardButton[]>
            {
                new[] { new KeyboardButton(AppScreens.Tabs.Home), new(AppScreens.Tabs.Tasks), new(AppScreens.Tabs.Rating) },
                new[] { new KeyboardButton(AppScreens.Tabs.Create), new(AppScreens.Tabs.Team), new(AppScreens.Tabs.Settings) }
            };
            if (user.IsAdmin())
                rows.Add(new[] { new KeyboardButton(AppScreens.Tabs.Admin) });

            return new ReplyKeyboardMarkup(rows)
            {
                ResizeKeyboard = true,
                IsPersistent = true
            };
        }

        return new ReplyKeyboardMarkup([
            [new(AppScreens.Tabs.Home), new(AppScreens.Tabs.Tasks), new(AppScreens.Tabs.Rating)],
            [new(AppScreens.Tabs.Settings)]
        ])
        {
            ResizeKeyboard = true,
            IsPersistent = true
        };
    }

    public static InlineKeyboardMarkup MainMenuButtons(AppUser user, string? webAppUrl)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("📋 Мои задачи", "nav:tasks") }
        };

        if (user.IsManagerOrAdmin())
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить задачу", "nav:add") });

        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "nav:settings") });

        if (!string.IsNullOrWhiteSpace(webAppUrl) && !webAppUrl.Contains("YOUR_DOMAIN"))
            rows.Add(new[] { InlineKeyboardButton.WithWebApp("📱 Открыть приложение", new WebAppInfo { Url = webAppUrl }) });

        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup AdminUserButtons(IReadOnlyList<AppUser> users, int actorId) =>
        new(users
            .Where(u => u.Id != actorId && u.Role != UserRole.Admin)
            .SelectMany(u => new[]
            {
                u.Role == UserRole.Worker
                    ? new[] { InlineKeyboardButton.WithCallbackData($"⬆️ Менеджер · {u.FirstName}", $"admin:promote:{u.Id}") }
                    : new[] { InlineKeyboardButton.WithCallbackData($"⬇️ Сотрудник · {u.FirstName}", $"admin:demote:{u.Id}") },
                u.IsActive
                    ? new[] { InlineKeyboardButton.WithCallbackData($"⛔ Деактивировать · {u.FirstName}", $"admin:deactivate:{u.Id}") }
                    : new[] { InlineKeyboardButton.WithCallbackData($"✅ Активировать · {u.FirstName}", $"admin:activate:{u.Id}") }
            })
            .Concat([[InlineKeyboardButton.WithCallbackData("🔄 Обновить", "nav:admin")]])
            .ToArray());

    public static InlineKeyboardMarkup TaskListButtons(IReadOnlyList<WorkTask> tasks) =>
        new(tasks.Select(t => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"{PriorityIcon(t.Priority)} {TrimTitle(t.Title, 28)}",
                $"nav:task:{t.Id}")
        }).Concat([[InlineKeyboardButton.WithCallbackData("🔄 Обновить", "nav:tasks")]]).ToArray());

    public static InlineKeyboardMarkup TaskDetailButtons(WorkTask task, bool isManager)
    {
        var rows = new List<InlineKeyboardButton[]>();

        if (!isManager)
        {
            if (task.Status == WorkTaskStatus.Pending)
                rows.Add([InlineKeyboardButton.WithCallbackData("🟥 🤝 Принять", $"task:accept:{task.Id}")]);
            if (task.Status is WorkTaskStatus.Pending or WorkTaskStatus.Accepted)
                rows.Add([InlineKeyboardButton.WithCallbackData("⬜ ✅ Выполнить", $"task:complete:{task.Id}")]);
        }
        else
        {
            rows.Add(
            [
                InlineKeyboardButton.WithCallbackData("⬜ ✏️ Изменить", $"task:edit:{task.Id}"),
                InlineKeyboardButton.WithCallbackData("🟥 🗑 Удалить", $"task:delete:{task.Id}")
            ]);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("◀️ К списку", "nav:tasks")]);
        return new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup PriorityPicker() =>
        new([
            [InlineKeyboardButton.WithCallbackData("⬜ Низкий", "flow:prio:0")],
            [InlineKeyboardButton.WithCallbackData("⬜ Обычный", "flow:prio:1")],
            [InlineKeyboardButton.WithCallbackData("🟧 Высокий", "flow:prio:2")],
            [InlineKeyboardButton.WithCallbackData("🟥 Срочно", "flow:prio:3")],
            [InlineKeyboardButton.WithCallbackData("❌ Отмена", "flow:cancel")]
        ]);

    public static InlineKeyboardMarkup YesNoTime() =>
        new([
            [
                InlineKeyboardButton.WithCallbackData("🟥 ✅ Указать", "flow:time:yes"),
                InlineKeyboardButton.WithCallbackData("⬜ ⏭ Пропустить", "flow:time:no")
            ],
            [InlineKeyboardButton.WithCallbackData("❌ Отмена", "flow:cancel")]
        ]);

    public static InlineKeyboardMarkup ReminderPicker() =>
        new([
            [
                InlineKeyboardButton.WithCallbackData("5 мин", "flow:rem:5"),
                InlineKeyboardButton.WithCallbackData("15 мин", "flow:rem:15"),
                InlineKeyboardButton.WithCallbackData("30 мин", "flow:rem:30")
            ],
            [
                InlineKeyboardButton.WithCallbackData("60 мин", "flow:rem:60"),
                InlineKeyboardButton.WithCallbackData("120 мин", "flow:rem:120")
            ],
            [InlineKeyboardButton.WithCallbackData("❌ Отмена", "flow:cancel")]
        ]);

    public static InlineKeyboardMarkup WorkerPicker(IReadOnlyList<AppUser> workers) =>
        new(workers
            .Select(w => new[]
            {
                InlineKeyboardButton.WithCallbackData($"⬜ {w.FirstName}", $"flow:assign:{w.Id}")
            })
            .Concat([[InlineKeyboardButton.WithCallbackData("❌ Отмена", "flow:cancel")]])
            .ToArray());

    public static InlineKeyboardMarkup SettingsButtons() =>
        new([
            [InlineKeyboardButton.WithCallbackData("🟥 🔔 Время рассылки", "settings:notify_time")],
            [InlineKeyboardButton.WithCallbackData("⬜ ⏱ Напоминание (N мин)", "settings:reminder")],
            [InlineKeyboardButton.WithCallbackData("📦 Архив", "nav:archive")],
            [InlineKeyboardButton.WithCallbackData("🛠 Панель менеджера", "nav:manager")]
        ]);

    public static InlineKeyboardMarkup WorkerSettingsButtons() =>
        new([
            [InlineKeyboardButton.WithCallbackData("🟥 🔔 Время рассылки", "settings:notify_time")],
            [InlineKeyboardButton.WithCallbackData("⬜ ⏱ Напоминание (N мин)", "settings:reminder")]
        ]);

    public static InlineKeyboardMarkup EditTaskOptions(int taskId) =>
        new([
            [InlineKeyboardButton.WithCallbackData("⬜ 📝 Текст", $"task:edit_text:{taskId}")],
            [InlineKeyboardButton.WithCallbackData("🟥 ⏰ Время", $"task:edit_time:{taskId}")],
            [InlineKeyboardButton.WithCallbackData("◀️ Назад", $"nav:task:{taskId}")]
        ]);

    public static InlineKeyboardMarkup ConfirmDelete(int taskId) =>
        new([
            [
                InlineKeyboardButton.WithCallbackData("🟥 Удалить", $"task:delete_confirm:{taskId}"),
                InlineKeyboardButton.WithCallbackData("⬜ Отмена", $"nav:task:{taskId}")
            ]
        ]);

    public static InlineKeyboardMarkup ManagerHubButtons() =>
        new([
            [InlineKeyboardButton.WithCallbackData("➕ Новая задача", "nav:add")],
            [InlineKeyboardButton.WithCallbackData("👥 Команда", "nav:workers")],
            [InlineKeyboardButton.WithCallbackData("📦 Архив", "nav:archive")],
            [InlineKeyboardButton.WithCallbackData("📊 Отчёт недели", "nav:report")]
        ]);

    private static string PriorityIcon(TaskPriority p) => p switch
    {
        TaskPriority.Urgent => "🟥",
        TaskPriority.High => "🟧",
        TaskPriority.Normal => "⬜",
        _ => "⬜"
    };

    private static string TrimTitle(string title, int max) =>
        title.Length <= max ? title : title[..(max - 1)] + "…";
}
