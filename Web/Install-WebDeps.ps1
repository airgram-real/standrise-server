# Ставит то, без чего сайт не собрать и не отдать наружу:
#   Node.js LTS  - сборка фронтенда (vite build)
#   Caddy        - https для projectre.work с автоматическим сертификатом
# Без Start-Transcript: он падал молча и окно закрывалось.

$log = "C:\StandRise\Web\install_deps.txt"
$lines = New-Object System.Collections.Generic.List[string]

function Say($text, $color = "Gray") {
    Write-Host $text -ForegroundColor $color
    $lines.Add($text)
}

function Flush() {
    try {
        New-Item -ItemType Directory -Path (Split-Path $log) -Force | Out-Null
        $lines | Out-File -FilePath $log -Encoding utf8
    } catch { Write-Host "не смог записать лог: $_" -ForegroundColor Red }
}

Say "==== УСТАНОВКА ЗАВИСИМОСТЕЙ САЙТА ====" "Cyan"
Say (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Say ""

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---------------- Node.js ----------------
Say "--- Node.js ---" "Cyan"
$nodeExe = "C:\Program Files\nodejs\node.exe"
if (Test-Path $nodeExe) {
    $v = & $nodeExe --version 2>&1
    Say "Уже стоит: $v" "Green"
} else {
    try {
        Say "Ищу актуальную LTS-версию..."
        $index = Invoke-RestMethod -Uri "https://nodejs.org/dist/index.json" -UseBasicParsing -TimeoutSec 60
        $lts = $index | Where-Object { $_.lts -and $_.lts -ne $false } | Select-Object -First 1
        if (-not $lts) { throw "не нашёл LTS в index.json" }
        $ver = $lts.version
        $url = "https://nodejs.org/dist/$ver/node-$ver-x64.msi"
        $msi = Join-Path $env:TEMP "node-$ver-x64.msi"
        Say "Версия: $ver"
        Say "Качаю: $url"
        Invoke-WebRequest -Uri $url -OutFile $msi -UseBasicParsing -TimeoutSec 900
        Say ("Скачано: {0:N1} МБ" -f ((Get-Item $msi).Length / 1MB))
        Say "Ставлю (тихая установка, может занять минуту)..."
        $p = Start-Process msiexec.exe -ArgumentList "/i", "`"$msi`"", "/qn", "/norestart" -Wait -PassThru
        Say "msiexec код выхода: $($p.ExitCode)"
        if (Test-Path $nodeExe) {
            $v = & $nodeExe --version 2>&1
            Say "Node поставлен: $v" "Green"
        } else {
            Say "Node не появился по пути $nodeExe" "Red"
        }
    } catch {
        Say "ОШИБКА установки Node: $_" "Red"
    }
}
Say ""

# ---------------- Caddy ----------------
Say "--- Caddy ---" "Cyan"
$caddyDir = "C:\StandRise\Web\caddy"
$caddyExe = Join-Path $caddyDir "caddy.exe"
if (Test-Path $caddyExe) {
    Say "Уже стоит: $caddyExe" "Green"
} else {
    try {
        New-Item -ItemType Directory -Path $caddyDir -Force | Out-Null
        $url = "https://caddyserver.com/api/download?os=windows&arch=amd64"
        Say "Качаю: $url"
        Invoke-WebRequest -Uri $url -OutFile $caddyExe -UseBasicParsing -TimeoutSec 900
        Say ("Скачано: {0:N1} МБ" -f ((Get-Item $caddyExe).Length / 1MB))
        if ((Get-Item $caddyExe).Length -lt 1MB) { throw "файл подозрительно мал — скорее всего скачалась страница с ошибкой" }
        Say "Caddy поставлен" "Green"
    } catch {
        Say "ОШИБКА установки Caddy: $_" "Red"
    }
}
Say ""

# ---------------- Проверка dotnet ----------------
Say "--- .NET ---" "Cyan"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (Test-Path $dotnet) {
    Say "dotnet: $(& $dotnet --version 2>&1)"
    $rt = & $dotnet --list-runtimes 2>&1 | Select-String "Microsoft.AspNetCore.App 7"
    if ($rt) { Say "ASP.NET Core 7 есть — сайт соберётся" "Green" }
    else { Say "ASP.NET Core 7 НЕ найден. Нужен SDK с веб-компонентами." "Red" }
} else {
    Say "dotnet не найден" "Red"
}

Say ""
Say "==== ГОТОВО ====" "Cyan"
Flush
Write-Host ""
Write-Host "Лог: $log"
Write-Host "Нажми любую клавишу..."
try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { Start-Sleep -Seconds 20 }
