# CyberX Tasks

Корпоративное приложение задач для Telegram: **Mini App** (отдельное окно) + бот для уведомлений.

## Архитектура

```
CyberXTasks/
├── CyberXTasks.Api/     ← Веб-приложение (Mini App) + REST API
├── CyberXTasks.Bot/     ← Telegram-бот (уведомления, кнопка «Открыть»)
└── data/cyberx.db       ← Общая база SQLite
```

**Бот** — только `/start` и push-уведомления (утро, дедлайн, отчёт).  
**Приложение** — весь интерфейс в отдельном окне Telegram с брендингом Cyber**X** (белый + красный).

## Возможности приложения

- Дашборд, задачи с приоритетами, принятие и выполнение
- Напоминание за N минут до дедлайна
- Рейтинг сотрудников за неделю
- Еженедельный отчёт (менеджер)
- Команда, архив, настройки, график на неделю
- Push в Telegram при назначении задачи и при выполнении (с комментарием)

## Быстрый старт

### 1. Создайте бота у [@BotFather](https://t.me/BotFather)

### 2. Настройте `appsettings.json` (в Bot и Api — одинаково)

```json
{
  "BotSettings": {
    "BotToken": "ВАШ_ТОКЕН",
    "BootstrapManagerIds": [123456789],
    "DatabasePath": "../data/cyberx.db",
    "WebAppUrl": "https://ВАШ_HTTPS_АДРЕС"
  }
}
```

### 3. HTTPS (обязательно для Mini App)

Telegram открывает приложение только по **HTTPS**. Для локальной разработки:

```bash
# Терминал 1 — API
cd CyberXTasks.Api
dotnet run --launch-profile https

# Терминал 2 — туннель (пример с ngrok)
ngrok http https://localhost:7247
```

Скопируйте HTTPS-URL из туннеля в `WebAppUrl` в **обоих** appsettings.

**Постоянный домен** (не меняется после перезагрузки): см. [DEPLOY.md](DEPLOY.md).

### 4. Запуск

```bash
# Терминал 1 — API (приложение)
cd CyberXTasks.Api
dotnet run --launch-profile https

# Терминал 2 — Бот (уведомления)
cd CyberXTasks.Bot
dotnet run
```

### 5. BotFather — кнопка меню (опционально)

@BotFather → ваш бот → **Bot Settings** → **Menu Button** → **Configure** → URL вашего Mini App.

Тогда приложение открывается и из кнопки слева от поля ввода.

### 6. Первый вход

1. Менеджер (ID в `BootstrapManagerIds`) → `/start` → **Открыть CyberX Tasks**
2. В приложении: **Настройки** → **Команда** → добавить Telegram ID сотрудников
3. Сотрудники → `/start` → открыть приложение

## Дизайн

- Тёмная тема, шрифт Inter
- Логотип: **Cyber** (белый `#f4f4f6`) + **X** (красный `#e8272a`)
- Нижняя навигация как в мобильном приложении
- Без emoji-оформления в интерфейсе

## Команды бота

| Команда | Действие |
|---------|----------|
| `/start` | Кнопка открытия Mini App |
| `/app` | То же самое |

Все действия с задачами — только в приложении.
