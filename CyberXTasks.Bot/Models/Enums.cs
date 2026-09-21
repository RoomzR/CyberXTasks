namespace CyberXTasks.Bot.Models;

public enum UserRole
{
    Worker = 0,
    Manager = 1,
    Admin = 2
}

public enum WorkTaskStatus
{
    Pending = 0,
    Accepted = 1,
    Completed = 2,
    Deleted = 3
}

public enum TaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum TaskCategory
{
    General = 0,
    Client = 1,
    Field = 2,
    Report = 3,
    Meeting = 4,
    Maintenance = 5
}

public enum AppScreen
{
    Home = 0,
    Tasks = 1,
    TaskDetail = 2,
    AddTask = 3,
    Settings = 4,
    Workers = 5,
    Archive = 6,
    Leaderboard = 7,
    ManagerHub = 8,
    AdminPanel = 9
}

public enum ConversationStep
{
    None = 0,
    AddTaskTitle = 1,
    AddTaskDueTime = 2,
    AddTaskAssignee = 3,
    EditTaskTitle = 4,
    EditTaskDueTime = 5,
    AddWorkerTelegramId = 6,
    SetNotificationTime = 7,
    SetReminderMinutes = 8
}
