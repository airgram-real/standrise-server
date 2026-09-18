# StandRise — инструкция для Claude Code

> **Единственный документ проекта.** Все остальные `.md`-инструкки удалены.  
> Цель: развернуть полный стек StandRise на **Windows Server 2022** (hostname `ipssystem`) одной командой.

---

## Роль агента

Ты — Claude Code на целевом сервере. Твоя задача:

1. Распаковать архив в `C:\StandRise`
2. Запустить `deploy\INSTALL.cmd` от администратора (или выполнить шаги из раздела «Ручной запуск»)
3. Проверить порты, логи и matchmaking
4. Сообщить пользователю `PublicIp` для патча APK

**Не создавай новые инструкции** — всё уже здесь. Меняй только `config.env` и код при явной ошибке.

---

## Архитектура

```
Клиент (APK 0.17.0, IP = PublicIp)
    │
    ├── TCP 2222 ──► VsCode.dll (RPC, matchmaking, auth)
    │                    │
    │                    ├── HTTP 2224 ◄── Photon MatchmakingPlugin
    │                    ├── Mongo 2077 (Main)
    │                    └── Mongo 1337 (Inventory)
    │
    └── UDP 5055/5056 ──► Photon LoadBalancing + MatchmakingPlugin.dll
```

| Компонент | Порт | Протокол | Файл / служба |
|-----------|------|----------|---------------|
| RPC | 2222 | TCP | `bin\Release\net7.0\VsCode.dll` |
| HTTP API (для Photon plugin) | 2224 | TCP | тот же процесс |
| Mongo Main | 2077 | TCP | `StandRiseMongoMain` |
| Mongo Inventory | 1337 | TCP | `StandRiseMongoGame` |
| Photon Master | 5055 | UDP | `PhotonSocketServer.exe /run LoadBalancing` |
| Photon Game | 5056 | UDP | тот же процесс |

Конфиг клиента: `bin\Release\net7.0\local.settings.json` → поле **`PublicIp`**.

---

## Быстрый старт (3 шага)

### Шаг 1 — распаковка

```powershell
# Архив StandRise-FULL-*.zip → C:\StandRise
Expand-Archive -Path "C:\Users\Administrator\Desktop\StandRise-FULL-*.zip" -DestinationPath "C:\StandRise" -Force
```

Структура после распаковки:

```
C:\StandRise\
  CLAUDE.md              ← этот файл
  VsCode.csproj
  deploy\
    INSTALL.cmd          ← ЕДИНСТВЕННАЯ точка входа
    Install-StandRise.ps1
    config.env.example
    built\MatchmakingPlugin.dll
    mongo-dump\          ← если был экспорт с старого сервера
  third-party\Photon-SDK\  ← если архив собран с -IncludePhotonSdk
  PhotonPlugin\
  bin\Release\net7.0\
```

### Шаг 2 — config.env

```powershell
cd C:\StandRise\deploy
copy config.env.example config.env
notepad config.env
```

Обязательно проверь:

| Ключ | Значение |
|------|----------|
| `PUBLIC_IP` | `auto` или белый IP сервера |
| `MONGO_PASS` | пароль MongoDB |
| `INSTALL_ROOT` | `C:\StandRise` |
| `PHOTON_INSTALL_ROOT` | `C:\PhotonServer` |
| `PHOTON_SOURCE` | `third-party\Photon-SDK` (если SDK в архиве) или путь к SDK |
| `IMPORT_MONGO_DUMP` | `true` если есть `deploy\mongo-dump` |

### Шаг 3 — установка одной командой

```cmd
:: CMD от администратора
C:\StandRise\deploy\INSTALL.cmd
```

Скрипт автоматически:

- установит .NET 7 SDK (winget)
- установит MongoDB Community (winget/choco)
- создаст службы `StandRiseMongoMain` / `StandRiseMongoGame`
- импортирует `mongo-dump` (если есть)
- сгенерирует `local.settings.json` с правильным `PublicIp`
- откроет firewall (2222, 2224, 5055, 5056)
- соберёт `VsCode.dll` и `MatchmakingPlugin.dll`
- развернёт Photon SDK → `C:\PhotonServer`
- пропатчит Photon `PublicIPAddress` и `ServerUrl=http://127.0.0.1:2224`
- запустит RPC + Photon
- создаст задачи планировщика для автозапуска

---

## Сборка архива (на СТАРОМ сервере)

Выполни **до** переноса, от администратора:

```powershell
cd C:\Users\Administrator\Desktop\test\deploy

# Базовый архив (~20 MB)
.\Create-Archive.ps1

# С дампом MongoDB
.\Create-Archive.ps1 -IncludeMongoDump

# С полным Photon SDK (~120 MB) — рекомендуется для ipssystem
.\Create-Archive.ps1 -IncludeMongoDump -IncludePhotonSdk

# Экспорт Mongo отдельно (если Create-Archive без дампа)
.\Export-MongoDump.ps1
```

ZIP появится на рабочем столе: `StandRise-FULL-YYYYMMDD-HHMM.zip`.

**Не включать в архив:** `traffic_hex.log`, `*.obb`, `*.apk` (копировать отдельно).

---

## Ручной запуск (если INSTALL.cmd не сработал)

```powershell
cd C:\StandRise\deploy
powershell -ExecutionPolicy Bypass -File .\Install-StandRise.ps1
```

Управление после установки:

```powershell
.\Start-StandRise.ps1    # RPC + Photon
.\Stop-StandRise.ps1     # остановить всё
```

---

## Проверка после установки

```powershell
# Порты
Test-NetConnection 127.0.0.1 -Port 2222
Test-NetConnection 127.0.0.1 -Port 2224
Test-NetConnection 127.0.0.1 -Port 2077
Test-NetConnection 127.0.0.1 -Port 1337

# Процессы
Get-Process dotnet, PhotonSocketServer -ErrorAction SilentlyContinue

# RPC лог (последние строки)
Get-Content C:\StandRise\bin\Release\net7.0\logs\*.log -Tail 30 -ErrorAction SilentlyContinue

# Photon plugin
Get-Content C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\plugin_debug.log -Tail 20 -ErrorAction SilentlyContinue
```

### Чеклист «всё работает»

- [ ] `local.settings.json` → `PublicIp` = белый IP (не 127.0.0.1 на проде)
- [ ] RPC слушает `:2222`, HTTP API `:2224`
- [ ] Mongo 2077/1337 отвечают
- [ ] Photon слушает UDP 5055/5056
- [ ] В Photon log: `ServerUrl=http://127.0.0.1:2224`
- [ ] Клиент заходит, ranked/allies поиск находит матч
- [ ] Кнопка **ПОДТВЕРДИТЬ** появляется (не залипает на «СОЮЗНИКИ»)
- [ ] История матчей не пустая

---

## Клиент (APK 0.17.0)

APK/OBB **не в архиве**. IP сервера в клиенте должен совпадать с `PublicIp`:

1. Пропатчить бинарник (Il2Cpp / networking) на IP из `local.settings.json`
2. Или использовать готовый mod APK с правильным IP

Photon в клиенте: Master **5055**, Game **5056**, IP = `PublicIp`.

---

## Устранение неполадок

### Confirm UI залипает на «СОЮЗНИКИ»

Причина: сервер повторно шлёт `Confirmation` после disconnect search-TCP.

Проверь в `RpcServer\Api\MatchmakingManager.cs`:
- resend/keep-alive шлёт только `Done`, не полный bundle
- `ConfirmMatch by playerId` появляется в логах при нажатии

```powershell
Select-String -Path "C:\StandRise\bin\Release\net7.0\logs\*.log" -Pattern "ConfirmMatch|SearchDone|Confirmation" | Select-Object -Last 30
```

После правки: `dotnet build -c Release`, перезапуск RPC.

### Photon plugin не грузится

```powershell
# DLL на месте?
Test-Path C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\MatchmakingPlugin.dll

# ServerUrl в конфиге
Select-String -Path "C:\PhotonServer\deploy\Loadbalancing\GameServer\bin\Photon.LoadBalancing.dll.config" -Pattern "ServerUrl|PublicIPAddress"
```

Пересборка plugin:

```powershell
cd C:\StandRise\PhotonPlugin
dotnet build MatchmakingPlugin.csproj -c Release
Copy-Item bin\Release\net472\MatchmakingPlugin.dll C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\ -Force
```

Перезапуск Photon: `deploy\Stop-StandRise.ps1` → `deploy\Start-StandRise.ps1`

### MongoDB auth failed

```powershell
# Сброс пароля (если auth ещё не включён)
mongosh mongodb://127.0.0.1:2077/admin
# db.createUser({ user: "eliseyy22", pwd: "...", roles: ["root"] })
```

Обнови пароль в `config.env` и перезапусти `Install-StandRise.ps1` (пересоздаст `local.settings.json`).

### Порт занят

```powershell
netstat -ano | findstr ":2222"
netstat -ano | findstr ":5055"
taskkill /PID <pid> /F
```

---

## Переменные config.env (полный список)

```ini
SERVER_HOSTNAME=ipssystem
INSTALL_ROOT=C:\StandRise
PUBLIC_IP=auto

MONGO_MAIN_PORT=2077
MONGO_GAME_PORT=1337
MONGO_USER=eliseyy22
MONGO_PASS=ваш_пароль
MONGO_MAIN_DATA=C:\StandRise\data\mongo-main
MONGO_GAME_DATA=C:\StandRise\data\mongo-game

RPC_PORT=2222
HTTP_API_PORT=2224
PHOTON_GAME_PORT=5056
PHOTON_MASTER_PORT=5055

PHOTON_INSTALL_ROOT=C:\PhotonServer
PHOTON_SOURCE=third-party\Photon-SDK
AUTO_START_RPC=true
AUTO_START_PHOTON=true
AUTO_START_AT_BOOT=true
IMPORT_MONGO_DUMP=true
```

---

## Задачи планировщика

| Имя | Что запускает |
|-----|---------------|
| `StandRise-RPC` | `dotnet VsCode.dll` |
| `StandRise-Photon` | `PhotonSocketServer.exe /run LoadBalancing` |

---

## Важные пути

| Путь | Назначение |
|------|------------|
| `C:\StandRise\bin\Release\net7.0\VsCode.dll` | RPC-сервер |
| `C:\StandRise\bin\Release\net7.0\local.settings.json` | PublicIp, Mongo, порты |
| `C:\StandRise\bin\Release\net7.0\logs\` | Логи RPC |
| `C:\PhotonServer\deploy\bin_Win64\PhotonSocketServer.exe` | Photon |
| `C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\` | Plugin DLL |
| `C:\StandRise\deploy\mongo-dump\` | Дамп для импорта |
| `C:\StandRise\RpcServer\Api\MatchmakingManager.cs` | Matchmaking + confirm fix |

---

## Порядок действий для Claude Code (кратко)

```
1. Прочитай CLAUDE.md (этот файл)
2. Проверь OS: Windows Server 2019+ / 2022, hostname ipssystem
3. Распакуй архив → C:\StandRise
4. Скопируй config.env.example → config.env, задай PUBLIC_IP и MONGO_PASS
5. Запусти deploy\INSTALL.cmd от администратора
6. Выполни чеклист проверки
7. Выведи пользователю PublicIp, RPC и Photon endpoints
8. При ошибках — раздел «Устранение неполадок», правь код, rebuild, restart
```
