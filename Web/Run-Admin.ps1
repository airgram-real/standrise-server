# Сборка и запуск всего после добавления админ-API:
#   0) игровой сервер (там появился AdminApi + мост к методам бота)
#   1) серверная часть сайта (появился GameApi.cs)
#   2) фронтенд (подарок, промокод, админ-панель)
#   3) перезапуск задач планировщика
#   4) проверка: health, каталог, админ-API игры

$log  = "C:\StandRise\Web\runadmin.log"
$done = "C:\StandRise\Web\runadmin.done"
Remove-Item $done -ErrorAction SilentlyContinue
Set-Content -Path $log -Value "==== RUN ADMIN $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ====" -Encoding utf8
function Log($t) {
    $line = "$(Get-Date -Format 'HH:mm:ss')  $t"
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding utf8
}

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Log "Администратор: $isAdmin"

$dotnet = "C:\Program Files\dotnet\dotnet.exe"

# ---------------- 0. Игровой сервер ----------------
Log ""
Log "=== ШАГ 0: пересборка игрового сервера ==="
try {
    $out0 = & powershell -NoProfile -ExecutionPolicy Bypass -File "C:\StandRise\deploy\Build-Restart.ps1" 2>&1
    $out0 | Select-Object -Last 25 | ForEach-Object { Log "  $_" }
} catch { Log "ОШИБКА сборки сервера: $_" }

$report = "C:\StandRise\deploy\build_report.txt"
$gameOk = $false
if (Test-Path $report) {
    $txt = Get-Content $report -Raw
    if ($txt -match "(?i)build succeeded") { $gameOk = $true; Log "игровой сервер: СОБРАЛСЯ" }
    else {
        Log "игровой сервер: СБОРКА УПАЛА — вот ошибки:"
        ($txt -split "`r?`n") | Where-Object { $_ -match "error CS" } |
            Select-Object -First 25 | ForEach-Object { Log "    $_" }
    }
}

# ---------------- 1. Серверная часть сайта ----------------
Log ""
Log "=== ШАГ 1: сборка ProjectRework.Web ==="
# Останавливаем сайт, иначе dll занята и сборка молча падает.
try { Stop-ScheduledTask -TaskName "ProjectReworkWeb" -ErrorAction SilentlyContinue } catch { }
# В прошлый заход сборка упала: exe оставался запущен и был занят. Гасим и по
# имени процесса (apphost), и по командной строке dotnet — Get-Process в PS 5.1
# командную строку не знает, поэтому берём её из CIM.
Get-Process "ProjectRework.Web" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
try {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*ProjectRework.Web*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
} catch { }
Start-Sleep -Seconds 3

$lockedExe = "C:\StandRise\Web\bin\Release\net7.0\ProjectRework.Web.exe"
if (Test-Path $lockedExe) {
    try {
        $fs = [System.IO.File]::Open($lockedExe, 'Open', 'ReadWrite', 'None')
        $fs.Close()
        Log "старый exe свободен — можно собирать"
    } catch { Log "ВНИМАНИЕ: exe всё ещё занят, сборка может упасть" }
}

$webOk = $false
try {
    $outw = & $dotnet build "C:\StandRise\Web\ProjectRework.Web.csproj" -c Release --nologo 2>&1
    $outw | Select-Object -Last 30 | ForEach-Object { Log "  $_" }
    if ($LASTEXITCODE -eq 0) { $webOk = $true; Log "сайт (бэкенд): СОБРАЛСЯ" }
    else { Log "сайт (бэкенд): СБОРКА УПАЛА exit=$LASTEXITCODE" }
} catch { Log "ОШИБКА сборки сайта: $_" }

# ---------------- 2. Фронтенд ----------------
Log ""
Log "=== ШАГ 2: сборка фронтенда ==="
$nodeDir = (Get-ChildItem "C:\StandRise\Web\node" -Filter "node.exe" -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1).DirectoryName
if (-not $nodeDir) { Log "ОШИБКА: node.exe не найден" }
else {
    $env:Path = "$nodeDir;$env:Path"
    $src = "C:\Users\Administrator\Downloads\ProjectRework\ProjectRework"
    Push-Location $src
    $out = & cmd /c "`"$nodeDir\npm.cmd`" run build 2>&1"
    $rc = $LASTEXITCODE
    Pop-Location
    $out | Select-Object -Last 25 | ForEach-Object { Log "  $_" }
    Log "сборка фронтенда exit=$rc"

    if ($rc -eq 0 -and (Test-Path "$src\dist\index.html")) {
        $dst = "C:\StandRise\Web\wwwroot"
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        Copy-Item "$src\dist\*" $dst -Recurse -Force
        Log "в wwwroot файлов: $((Get-ChildItem $dst -Recurse -File).Count)"
    } else { Log "ОШИБКА: dist не собрался — витрина осталась прежней" }
}

# ---------------- 3. Запуск ----------------
Log ""
Log "=== ШАГ 3: запуск ==="
if ($isAdmin) {
    foreach ($t in "ProjectReworkWeb", "ProjectReworkCaddy") {
        if (Get-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue) {
            Stop-ScheduledTask -TaskName $t -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
            Start-ScheduledTask -TaskName $t
            Start-Sleep -Seconds 3
            $info = Get-ScheduledTaskInfo -TaskName $t
            Log "$t : LastResult=$($info.LastTaskResult)"
        } else { Log "нет задачи $t" }
    }
} else {
    $siteDll = "C:\StandRise\Web\bin\Release\net7.0\ProjectRework.Web.dll"
    if (Test-Path $siteDll) {
        Start-Process -FilePath $dotnet -ArgumentList "`"$siteDll`"" `
            -WorkingDirectory "C:\StandRise\Web\bin\Release\net7.0" -WindowStyle Hidden
        Log "сайт запущен процессом"
    }
}

# ---------------- 4. Проверка ----------------
Log ""
Log "=== ШАГ 4: проверка ==="
Start-Sleep -Seconds 8

try {
    $h = Invoke-RestMethod -Uri "http://127.0.0.1:8080/api/health" -TimeoutSec 20
    Log "health: ok=$($h.ok) botToken=$($h.botToken) site=$($h.site) links=$($h.links)"
} catch { Log "health не ответил: $($_.Exception.Message)" }

try {
    $c = Invoke-RestMethod -Uri "http://127.0.0.1:8080/api/catalog" -TimeoutSec 20
    Log "catalog: голды=$($c.gold.Count) пропуск=$($c.pass.Count)"
} catch { Log "catalog не ответил: $($_.Exception.Message)" }

# Админ-API игры. Ключ — SHA-256 от токена бота, тот же, что считает сайт.
try {
    $settings = Get-Content "C:\StandRise\bin\Release\net7.0\local.settings.json" -Raw | ConvertFrom-Json
    $token = $settings.TelegramBotToken
    if ([string]::IsNullOrEmpty($token)) { Log "в local.settings.json нет TelegramBotToken" }
    else {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($token))
        $key = -join ($bytes | ForEach-Object { $_.ToString("x2") })
        Log "ключ админ-API собран (длина $($key.Length))"

        try {
            $t = Invoke-RestMethod -Uri "http://127.0.0.1:2224/api/admin/toggles?key=$key" -TimeoutSec 20
            Log "админ-API игры: ok=$($t.ok) рынок закрыт=$($t.marketClosed) подкрутка=$($t.arcane) whitelist=$($t.whitelist) союзники=$($t.alliesRequiredPlayers)"
        } catch { Log "админ-API игры не ответил: $($_.Exception.Message)" }

        # Без ключа он обязан отказать — проверяем, что порт 2224 не открыт наружу.
        try {
            Invoke-RestMethod -Uri "http://127.0.0.1:2224/api/admin/toggles" -TimeoutSec 10 | Out-Null
            Log "ВНИМАНИЕ: админ-API ответил БЕЗ ключа — это дыра, надо чинить"
        } catch { Log "без ключа админ-API отказывает — правильно" }
    }
} catch { Log "проверка админ-API не прошла: $_" }

Log ""
Log "=== ИТОГ ==="
Log "игровой сервер собрался: $gameOk"
Log "бэкенд сайта собрался: $webOk"
Log ""
Log "==== ВСЁ ===="
Set-Content -Path $done -Value "done" -Encoding utf8
