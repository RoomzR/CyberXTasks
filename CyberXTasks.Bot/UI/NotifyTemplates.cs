using System.Net;
using CyberXTasks.Bot.Models;

namespace CyberXTasks.Bot.UI;

public static class NotifyTemplates
{
    // Telegram HTML: только b, i, u, code — без span/style
    public static string Brand => "<b>Cyber</b><b>X</b>";

    public static string Welcome(AppUser user) =>
        $"""
        {Brand} Tasks
        
        Привет, <b>{Escape(user.FirstName)}</b>.
        
        Нажмите кнопку ниже — откроется приложение CyberX Tasks.
        
        В приложении: задачи, создание, команда, рейтинг, настройки.
        Уведомления утром приходят сюда в чат.
        """;

    public static string NotRegistered() =>
        $"""
        {Brand}
        
        Вы не зарегистрированы. Попросите менеджера добавить вас по Telegram ID.
        """;

    public static string ConfigRequired() =>
        $"""
        {Brand}
        
        Приложение ещё не настроено. Укажите WebAppUrl в appsettings.json и запустите API.
        """;

    public static string Push(string title, string body) =>
        $"""
        {Brand} · {Escape(title)}
        
        {body}
        
        <i>Откройте приложение для действий</i>
        """;

    public static string PriorityLabel(TaskPriority priority) => priority switch
    {
        TaskPriority.Urgent => "Срочный",
        TaskPriority.High => "Высокий",
        TaskPriority.Normal => "Обычный",
        _ => "Низкий"
    };

    public static string Escape(string text) => WebUtility.HtmlEncode(text);
}
