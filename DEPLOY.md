# Постоянный домен для CyberX Tasks

Сейчас `trycloudflare.com` меняет URL при каждом перезапуске. Ниже — варианты с **фиксированным адресом**.

## Вариант 1: Cloudflare Tunnel + свой домен (рекомендуется)

Бесплатно, если домен уже в Cloudflare.

### 1. Установка и вход

```bash
brew install cloudflared
cloudflared tunnel login
```

### 2. Создать туннель

```bash
cloudflared tunnel create cyberx-tasks
```

Сохраните **Tunnel ID** из вывода.

### 3. Конфиг

Создайте `~/.cloudflared/config.yml` (пример в `deploy/cloudflared-config.example.yml`):

```yaml
tunnel: <TUNNEL_ID>
credentials-file: /Users/ВАШ_USER/.cloudflared/<TUNNEL_ID>.json

ingress:
  - hostname: tasks.ваш-домен.by
    service: http://localhost:5021
  - service: http_status:404
```

### 4. DNS в Cloudflare Dashboard

**Zero Trust** → **Networks** → **Tunnels** → ваш туннель → **Public Hostname**

Или вручную: CNAME `tasks` → `<TUNNEL_ID>.cfargotunnel.com`

### 5. Запуск

```bash
# API
cd ~/Documents/CyberXTasks/CyberXTasks.Api
dotnet run --launch-profile https

# Туннель (всегда один и тот же URL)
cloudflared tunnel run cyberx-tasks

# Бот
cd ~/Documents/CyberXTasks/CyberXTasks.Bot
dotnet run
```

### 6. Обновить конфиг (один раз)

В `CyberXTasks.Api/appsettings.json` и `CyberXTasks.Bot/appsettings.json`:

```json
"WebAppUrl": "https://tasks.ваш-домен.by"
```

В BotFather → **Menu Button** → тот же URL.

### 7. Автозапуск macOS (опционально)

```bash
cloudflared service install
sudo launchctl load /Library/LaunchDaemons/com.cloudflare.cloudflared.plist
```

---

## Вариант 2: VPS (Linux)

Подходит для продакшена 24/7.

1. Арендуйте VPS (Ubuntu 22+)
2. Установите .NET 8, склонируйте проект
3. Nginx + Let's Encrypt на `tasks.ваш-домен.by` → `http://127.0.0.1:5021`
4. Systemd-сервисы для API и бота (примеры в `deploy/`)

```bash
# На сервере
cd CyberXTasks.Api && dotnet publish -c Release -o /opt/cyberx/api
cd ../CyberXTasks.Bot && dotnet publish -c Release -o /opt/cyberx/bot
```

`WebAppUrl`: `https://tasks.ваш-домен.by`

---

## Вариант 3: ngrok с фиксированным поддоменом (платно)

```bash
ngrok http 5021 --domain=cyberx.ngrok.app
```

URL не меняется, если подписка ngrok с reserved domain.

---

## Если туннель падает: `control stream encountered a failure`

Типичные причины и что делать:

### 1. Запущено несколько cloudflared (самое частое)

Проверка:

```bash
pgrep -fl cloudflared
```

Если видите **2 и больше** процесса — остановите все и запустите **один**:

```bash
pkill cloudflared
sleep 2
cloudflared tunnel --url http://127.0.0.1:5021 --protocol http2
```

### 2. API не запущен или другой порт

Туннель должен идти на тот же порт, где слушает API:

```bash
# Сначала API (терминал 1)
cd ~/Documents/CyberXTasks/CyberXTasks.Api
dotnet run --launch-profile http

# Проверка — должен ответить 200
curl -I http://127.0.0.1:5021/
```

Потом cloudflared (терминал 2). Профиль `http` надёжнее для туннеля, чем `https`.

### 3. Нестабильная сеть / сон Mac / VPN

Quick tunnel (`trycloudflare.com`) рвётся после сна ноутбука или смены Wi‑Fi. Решение:
- перезапустить API + один cloudflared;
- или настроить **named tunnel** (раздел выше) — URL не меняется и соединение стабильнее.

### 4. Named tunnel без настройки

Команда `cloudflared tunnel run cyberx-tasks` работает **только после**:

```bash
cloudflared tunnel login
cloudflared tunnel create cyberx-tasks
# + config.yml в ~/.cloudflared/
```

Без `~/.cloudflared/config.yml` и credentials named tunnel не поднимется.

### 5. Правильный порядок запуска (каждый день)

```bash
# 1) API
dotnet run --launch-profile http

# 2) Туннель (один процесс!)
cloudflared tunnel --url http://127.0.0.1:5021 --protocol http2

# 3) Скопировать HTTPS-URL из вывода cloudflared в WebAppUrl (оба appsettings.json)

# 4) Бот
dotnet run
```

---

## Чеклист после смены домена

- [ ] `WebAppUrl` в **обоих** appsettings.json
- [ ] Перезапуск API и бота
- [ ] BotFather Menu Button
- [ ] `/start` в Telegram — кнопка открывает новый URL
