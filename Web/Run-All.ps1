# Полная установка и запуск сайта projectre.work одним проходом.
# Без пауз и без ожидания клавиш: результат целиком уходит в лог,
# который потом читается с диска.

$ErrorActionPreference = "Continue"
$log = "C:\StandRise\Web\runall.log"
$done = "C:\StandRise\Web\runall.done"

Remove-Item $done -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path "C:\StandRise\Web" -Force | Out-Null

function Log($t) {
    $line = "$(Get-Date -Format 'HH:mm:ss')  $t"
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding utf8
}

Set-Content -Path $log -Value "==== RUN ALL $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ====" -Encoding utf8

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Log "Администратор: $isAdmin"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# ---------------- 1. Node.js ----------------
# Ставим из zip, а не из msi: msi требует прав администратора, а zip просто
# распаковывается. Ничего в системе не регистрируется, PATH не трогается.
Log ""
Log "=== ШАГ 1: Node.js ==="
$nodeHome = "C:\StandRise\Web\node"
$script:npmCmd = $null
$script:nodeExe = $null

$sysNode = "C:\Program Files\nodejs\node.exe"
if (Test-Path $sysNode) {
    $script:nodeExe = $sysNode
    $script:npmCmd  = "C:\Program Files\nodejs\npm.cmd"
    Log "уже стоит в системе: $(& $sysNode --version 2>&1)"
} else {
    $found = Get-ChildItem $nodeHome -Filter "node.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        $script:nodeExe = $found.FullName
        $script:npmCmd  = Join-Path $found.DirectoryName "npm.cmd"
        Log "уже распакован: $(& $script:nodeExe --version 2>&1)"
    } else {
        try {
            $index = Invoke-RestMethod -Uri "https://nodejs.org/dist/index.json" -UseBasicParsing -TimeoutSec 120
            $lts = $index | Where-Object { $_.lts -and $_.lts -ne $false } | Select-Object -First 1
            $ver = $lts.version
            $url = "https://nodejs.org/dist/$ver/node-$ver-win-x64.zip"
            $zip = Join-Path $env:TEMP "node-$ver-win-x64.zip"
            Log "качаю $url"
            Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -TimeoutSec 1800
            Log ("скачано {0:N1} МБ, распаковываю" -f ((Get-Item $zip).Length / 1MB))
            New-Item -ItemType Directory -Path $nodeHome -Force | Out-Null
            Expand-Archive -Path $zip -DestinationPath $nodeHome -Force
            $found = Get-ChildItem $nodeHome -Filter "node.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($found) {
                $script:nodeExe = $found.FullName
                $script:npmCmd  = Join-Path $found.DirectoryName "npm.cmd"
                Log "ok: $(& $script:nodeExe --version 2>&1)"
            } else { Log "ОШИБКА: node.exe не нашёлся после распаковки" }
        } catch { Log "ОШИБКА Node: $_" }
    }
}

# ---------------- 2. Caddy ----------------
Log ""
Log "=== ШАГ 2: Caddy ==="
$caddyDir = "C:\StandRise\Web\caddy"
$caddyExe = Join-Path $caddyDir "caddy.exe"
if (Test-Path $caddyExe) {
    Log "уже стоит"
} else {
    try {
        New-Item -ItemType Directory -Path $caddyDir -Force | Out-Null
        Log "качаю caddy"
        Invoke-WebRequest -Uri "https://caddyserver.com/api/download?os=windows&arch=amd64" `
            -OutFile $caddyExe -UseBasicParsing -TimeoutSec 1800
        $sz = (Get-Item $caddyExe).Length
        Log ("скачано {0:N1} МБ" -f ($sz / 1MB))
        if ($sz -lt 1MB) { Log "ОШИБКА: файл слишком мал, это не caddy" }
    } catch { Log "ОШИБКА Caddy: $_" }
}

# ---------------- 3. Бэкенд ----------------
Log ""
Log "=== ШАГ 3: сборка бэкенда ==="
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
Log "dotnet: $(& $dotnet --version 2>&1)"
$rt = & $dotnet --list-runtimes 2>&1 | Select-String "Microsoft.AspNetCore.App 7"
if ($rt) { Log "ASP.NET Core 7: есть" } else { Log "ASP.NET Core 7: НЕ НАЙДЕН" }

$out = & $dotnet build "C:\StandRise\Web\ProjectRework.Web.csproj" -c Release --nologo 2>&1
$rc = $LASTEXITCODE
$out | ForEach-Object { Log "  $_" }
Log "сборка бэкенда exit=$rc"

# ---------------- 4. Фронтенд ----------------
Log ""
Log "=== ШАГ 4: сборка сайта ==="
$src = "C:\Users\Administrator\Downloads\ProjectRework\ProjectRework"
$npm = $script:npmCmd
if (-not $npm -or -not (Test-Path $npm)) { Log "ОШИБКА: npm не найден, сайт не собрать"; $npm = $null }
if (-not $npm) {
    Log "ПРОПУЩЕНО: нет npm"
} elseif (-not (Test-Path $src)) {
    Log "ОШИБКА: нет папки сайта $src"
} else {
    Push-Location $src
    $out2 = & cmd /c "`"$npm`" run build 2>&1"
    $rc2 = $LASTEXITCODE
    Pop-Location
    $out2 | ForEach-Object { Log "  $_" }
    Log "сборка сайта exit=$rc2"

    if (Test-Path "$src\dist\index.html") {
        $dst = "C:\StandRise\Web\wwwroot"
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        Copy-Item "$src\dist\*" $dst -Recurse -Force
        Log "dist скопирован в wwwroot: $((Get-ChildItem $dst -Recurse -File).Count) файлов"
    } else {
        Log "ОШИБКА: dist\index.html не собрался"
    }
}

# ---------------- 5. Домен ----------------
Log ""
Log "=== ШАГ 5: домен и службы ==="
if (-not $isAdmin) {
    Log "ПРОПУЩЕНО: нужен запуск от администратора"
} else {
    foreach ($p in 80, 443) {
        $name = "ProjectRework HTTP $p"
        if (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue) { Log "порт $p уже открыт" }
        else {
            New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $p -Profile Any | Out-Null
            Log "открыт порт $p"
        }
    }

    try {
        $ips = (Resolve-DnsName projectre.work -Type A -ErrorAction Stop | Where-Object { $_.IPAddress }).IPAddress
        Log "projectre.work -> $($ips -join ', ')"
        $mine = (Invoke-RestMethod -Uri "https://api.ipify.org?format=json" -TimeoutSec 60).ip
        Log "внешний адрес сервера: $mine"
        if ($ips -contains $mine) { Log "DNS сходится" } else { Log "ВНИМАНИЕ: DNS не указывает на этот сервер, сертификат не выпустится" }
    } catch { Log "DNS проверить не смог: $_" }

    function Ensure-Service($name, $binPath, $display) {
        if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
            sc.exe stop $name | Out-Null; Start-Sleep -Seconds 2
            sc.exe delete $name | Out-Null; Start-Sleep -Seconds 2
        }
        sc.exe create $name binPath= $binPath start= auto DisplayName= $display | Out-Null
        sc.exe failure $name reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
        Start-Service -Name $name -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
        $s = Get-Service -Name $name -ErrorAction SilentlyContinue
        Log "$name : $($s.Status)"
    }

    $siteDll = "C:\StandRise\Web\bin\Release\net7.0\ProjectRework.Web.dll"
    if (Test-Path $siteDll) { Ensure-Service "ProjectReworkWeb" "`"$dotnet`" `"$siteDll`"" "Project Rework Web" }
    else { Log "ПРОПУЩЕНО: нет $siteDll" }

    if (Test-Path $caddyExe) { Ensure-Service "ProjectReworkCaddy" "`"$caddyExe`" run --config `"C:\StandRise\Web\Caddyfile`"" "Project Rework Caddy" }
    else { Log "ПРОПУЩЕНО: нет caddy.exe" }
}

# ---------------- 6. Проверка ----------------
Log ""
Log "=== ШАГ 6: проверка ==="
Start-Sleep -Seconds 5
try {
    $h = Invoke-RestMethod -Uri "http://127.0.0.1:8080/api/health" -TimeoutSec 20
    Log "health: ok=$($h.ok) botToken=$($h.botToken) site=$($h.site) links=$($h.links)"
} catch { Log "health не ответил: $_" }
try {
    $c = Invoke-RestMethod -Uri "http://127.0.0.1:8080/api/catalog" -TimeoutSec 20
    Log "catalog: голды=$($c.gold.Count) пропуск=$($c.pass.Count)"
} catch { Log "catalog не ответил: $_" }

Log ""
Log "==== ВСЁ ===="
Set-Content -Path $done -Value "done $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Encoding utf8
