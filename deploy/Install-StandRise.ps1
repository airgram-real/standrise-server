#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Полная автоустановка StandRise: .NET, MongoDB, RPC, Photon, plugin, firewall, autostart.
.EXAMPLE
  cd C:\StandRise\deploy
  copy config.env.example config.env
  powershell -ExecutionPolicy Bypass -File .\Install-StandRise.ps1
#>
param(
    [string]$ConfigFile = (Join-Path $PSScriptRoot "config.env")
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Write-Step([string]$msg) { Write-Host "`n========== $msg ==========" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow }

function Load-Config {
    if (-not (Test-Path $ConfigFile)) {
        Copy-Item (Join-Path $PSScriptRoot "config.env.example") $ConfigFile
        throw "Создан $ConfigFile — заполните PUBLIC_IP и MONGO_PASS, затем запустите снова."
    }
    $cfg = @{}
    Get-Content $ConfigFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { return }
        if ($line -match '^([^=]+)=(.*)$') { $cfg[$Matches[1].Trim()] = $Matches[2].Trim() }
    }
    return $cfg
}

function Get-PublicIp([hashtable]$cfg) {
    if ($cfg.PUBLIC_IP -and $cfg.PUBLIC_IP -ne "auto") { return $cfg.PUBLIC_IP.Trim() }
    foreach ($url in @("https://api.ipify.org", "https://ifconfig.me/ip")) {
        try {
            $ip = (Invoke-RestMethod -Uri $url -TimeoutSec 10).ToString().Trim()
            if ($ip -match '^\d+\.\d+\.\d+\.\d+$') { return $ip }
        } catch { }
    }
    $lan = Get-NetIPAddress -AddressFamily IPv4 | Where-Object {
        $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -ne "WellKnown"
    } | Select-Object -ExpandProperty IPAddress -First 1
    Write-Warn "Внешний IP не определён — LAN: $lan"
    return $lan
}

function Ensure-Directories([hashtable]$cfg) {
    @(
        $cfg.INSTALL_ROOT,
        "$($cfg.INSTALL_ROOT)\logs",
        $cfg.MONGO_MAIN_DATA,
        $cfg.MONGO_GAME_DATA,
        "$($cfg.INSTALL_ROOT)\bin\Release\net7.0"
    ) | ForEach-Object { New-Item -ItemType Directory -Force -Path $_ | Out-Null }
    Write-Ok "Каталоги в $($cfg.INSTALL_ROOT)"
}

function Find-ProjectRoot([string]$installRoot) {
    foreach ($c in @(
        (Resolve-Path (Join-Path $PSScriptRoot "..") -ErrorAction SilentlyContinue).Path,
        $installRoot
    )) {
        if ($c -and (Test-Path (Join-Path $c "VsCode.csproj"))) { return $c }
    }
    throw "VsCode.csproj не найден. Распакуйте архив в $installRoot"
}

function Sync-ProjectToInstallRoot([string]$sourceRoot, [hashtable]$cfg) {
    $dest = $cfg.INSTALL_ROOT
    if ((Resolve-Path $sourceRoot -ErrorAction SilentlyContinue).Path -eq (Resolve-Path $dest -ErrorAction SilentlyContinue).Path) {
        Write-Ok "Проект уже в $dest"
        return $dest
    }
    Write-Step "Копирование проекта -> $dest"
    robocopy $sourceRoot $dest /MIR /XD obj .git logs win-x64 runtimes /XF traffic_hex.log *.pdb *.bak dump*.txt *.obb *.apk /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $LASTEXITCODE" }
    Write-Ok "Проект скопирован"
    return $dest
}

function Install-DotNet7 {
    Write-Step ".NET 7 SDK"
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $ver = & dotnet --version 2>$null
        if ($ver -match '^7\.') { Write-Ok ".NET $ver"; return }
        Write-Warn ".NET $ver — нужен 7.x"
    }
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        & winget install Microsoft.DotNet.SDK.7 --accept-package-agreements --accept-source-agreements
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
    } else {
        throw "Установите .NET 7 SDK: https://dotnet.microsoft.com/download/dotnet/7.0"
    }
}

function Find-Mongod {
    Get-ChildItem "${env:ProgramFiles}\MongoDB\Server\*\bin\mongod.exe" -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}

function Install-MongoDB {
    Write-Step "MongoDB"
    $mongod = Find-Mongod
    if (-not $mongod) {
        Write-Warn "MongoDB не найден — установка..."
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            & winget install MongoDB.Server --accept-package-agreements --accept-source-agreements
        } elseif (Get-Command choco -ErrorAction SilentlyContinue) {
            & choco install mongodb -y
        } else {
            throw "Установите MongoDB Community: https://www.mongodb.com/try/download/community"
        }
        Start-Sleep -Seconds 5
        $mongod = Find-Mongod
    }
    if (-not $mongod) { throw "mongod.exe не найден" }
    Write-Ok "mongod: $mongod"
    return $mongod
}

function Write-MongoConfigs([hashtable]$cfg) {
    $mongoDir = Join-Path $cfg.INSTALL_ROOT "deploy\mongo"
    New-Item -ItemType Directory -Force -Path $mongoDir | Out-Null
    @(
        @{ File = "mongod-main.cfg"; Data = $cfg.MONGO_MAIN_DATA; Port = $cfg.MONGO_MAIN_PORT; Log = "mongod-main.log" },
        @{ File = "mongod-game.cfg"; Data = $cfg.MONGO_GAME_DATA; Port = $cfg.MONGO_GAME_PORT; Log = "mongod-game.log" }
    ) | ForEach-Object {
        $content = @"
storage:
  dbPath: $($_.Data)
systemLog:
  destination: file
  path: $($cfg.INSTALL_ROOT)\logs\$($_.Log)
net:
  port: $($_.Port)
  bindIp: 127.0.0.1
security:
  authorization: enabled
"@
        Set-Content (Join-Path $mongoDir $_.File) $content -Encoding UTF8
    }
    Write-Ok "Mongo configs -> $mongoDir"
}

function Install-MongoServices([hashtable]$cfg, [string]$mongodPath) {
    # Службы/процессы MongoDB поднимаются на шаге Bootstrap2.ps1; здесь только мягкая проверка.
    foreach ($name in @("StandRiseMongoMain", "StandRiseMongoGame")) {
        $existing = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($existing) {
            if ($existing.Status -ne 'Running') { Start-Service $name -ErrorAction SilentlyContinue }
            Write-Ok "Служба $name"
        } else {
            Write-Warn "Службы $name нет - MongoDB работает как процесс"
        }
    }
    foreach ($port in @($cfg.MONGO_MAIN_PORT, $cfg.MONGO_GAME_PORT)) {
        $r = Test-NetConnection 127.0.0.1 -Port ([int]$port) -WarningAction SilentlyContinue
        if ($r.TcpTestSucceeded) { Write-Ok "Mongo :$port отвечает" } else { Write-Warn "Mongo :$port не слушается" }
    }
    Start-Sleep -Seconds 2
}

function Initialize-MongoUsers([hashtable]$cfg) {
    Write-Step "MongoDB пользователь"
    $mongosh = Get-ChildItem "${env:ProgramFiles}\MongoDB\Server\*\bin\mongosh.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $mongosh) { Write-Warn "mongosh не найден — пропуск"; return }

    foreach ($port in @($cfg.MONGO_MAIN_PORT, $cfg.MONGO_GAME_PORT)) {
        $js = @"
try {
  db = db.getSiblingDB('admin');
  db.createUser({ user: '$($cfg.MONGO_USER)', pwd: '$($cfg.MONGO_PASS)', roles: [{ role: 'root', db: 'admin' }] });
  print('user created on $port');
} catch(e) {
  if (e.codeName === 'DuplicateKey') print('user exists on $port');
  else print(e);
}
"@
        $tmp = [System.IO.Path]::GetTempFileName() + ".js"
        Set-Content $tmp $js -Encoding UTF8
        try { & $mongosh.FullName "mongodb://127.0.0.1:$port/admin" --file $tmp 2>&1 | Out-Null } catch { }
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

function Import-MongoRawDataIfPresent([hashtable]$cfg) {
    $rawRoot = Join-Path $PSScriptRoot "mongo-data-raw"
    if (-not (Test-Path $rawRoot)) { return $false }

    Write-Step "Импорт raw MongoDB data"
    foreach ($pair in @(
        @("main", $cfg.MONGO_MAIN_DATA),
        @("inv", $cfg.MONGO_GAME_DATA)
    )) {
        $name, $dest = $pair
        $src = Join-Path $rawRoot $name
        if (-not (Test-Path $src)) { Write-Warn "Нет $src"; continue }
        foreach ($svc in @("StandRiseMongoMain", "StandRiseMongoGame")) {
            Stop-Service $svc -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        robocopy $src $dest /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $name" }
        Write-Ok "Raw $name -> $dest"
    }
    foreach ($svc in @("StandRiseMongoMain", "StandRiseMongoGame")) {
        Start-Service $svc -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 3
    return $true
}

function Import-MongoDumpIfPresent([hashtable]$cfg) {
    if ($cfg.IMPORT_MONGO_DUMP -ne "true") { return }
    if (Import-MongoRawDataIfPresent $cfg) { return }
    $dumpRoot = Join-Path $PSScriptRoot "mongo-dump"
    if (-not (Test-Path $dumpRoot)) { Write-Warn "Нет mongo-dump / mongo-data-raw — пропуск"; return }

    Write-Step "Импорт MongoDB"
    $userEsc = [uri]::EscapeDataString($cfg.MONGO_USER)
    $passEsc = [uri]::EscapeDataString($cfg.MONGO_PASS)
    $mongorestore = Get-ChildItem "${env:ProgramFiles}\MongoDB\Tools\*\bin\mongorestore.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $mongorestore) { $mongorestore = Get-Command mongorestore -ErrorAction SilentlyContinue }
    if (-not $mongorestore) { Write-Warn "mongorestore не найден"; return }

    $exe = if ($mongorestore.Source) { $mongorestore.Source } else { $mongorestore.Path }
    $mainUri = "mongodb://${userEsc}:${passEsc}@127.0.0.1:$($cfg.MONGO_MAIN_PORT)/?authSource=admin"
    $gameUri = "mongodb://${userEsc}:${passEsc}@127.0.0.1:$($cfg.MONGO_GAME_PORT)/?authSource=admin"

    if (Test-Path (Join-Path $dumpRoot "Main")) {
        & $exe --uri=$mainUri --drop (Join-Path $dumpRoot "Main")
        Write-Ok "Main imported"
    }
    if (Test-Path (Join-Path $dumpRoot "Inventory")) {
        & $exe --uri=$gameUri --drop (Join-Path $dumpRoot "Inventory")
        Write-Ok "Inventory imported"
    }
}

function Write-LocalSettings([hashtable]$cfg, [string]$publicIp, [string]$projectRoot) {
    $outDir = Join-Path $projectRoot "bin\Release\net7.0"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $userEsc = [uri]::EscapeDataString($cfg.MONGO_USER)
    $passEsc = [uri]::EscapeDataString($cfg.MONGO_PASS)
    $settings = @{
        PublicIp = $publicIp
        RpcPort = [int]$cfg.RPC_PORT
        HttpApiPort = [int]$cfg.HTTP_API_PORT
        PhotonMasterPort = [int]$cfg.PHOTON_MASTER_PORT
        PhotonGamePort = [int]$cfg.PHOTON_GAME_PORT
        MongoMain = @{
            DatabaseName = "Main"
            Address = "127.0.0.1"
            Port = "$($cfg.MONGO_MAIN_PORT)"
            Uri = "mongodb://${userEsc}:${passEsc}@127.0.0.1:$($cfg.MONGO_MAIN_PORT)/?authSource=admin&authMechanism=SCRAM-SHA-256"
        }
        MongoGame = @{
            DatabaseName = "Inventory"
            Address = "127.0.0.1"
            Port = "$($cfg.MONGO_GAME_PORT)"
            Uri = "mongodb://${userEsc}:${passEsc}@127.0.0.1:$($cfg.MONGO_GAME_PORT)/?authSource=admin&authMechanism=SCRAM-SHA-256"
        }
    } | ConvertTo-Json -Depth 5
    Set-Content (Join-Path $outDir "local.settings.json") $settings -Encoding UTF8
    Write-Ok "local.settings.json -> PublicIp=$publicIp"
}

function Set-FirewallRules([hashtable]$cfg) {
    Write-Step "Firewall"
    @(
        @{ Name = "StandRise RPC"; Port = $cfg.RPC_PORT; Protocol = "TCP" },
        @{ Name = "StandRise HTTP API"; Port = $cfg.HTTP_API_PORT; Protocol = "TCP" },
        @{ Name = "Photon Game UDP"; Port = $cfg.PHOTON_GAME_PORT; Protocol = "UDP" },
        @{ Name = "Photon Master UDP"; Port = $cfg.PHOTON_MASTER_PORT; Protocol = "UDP" }
    ) | ForEach-Object {
        if (-not (Get-NetFirewallRule -DisplayName $_.Name -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $_.Name -Direction Inbound -Action Allow -Protocol $_.Protocol -LocalPort $_.Port | Out-Null
        }
        Write-Ok "$($_.Name) :$($_.Port)/$($_.Protocol)"
    }
}

function Build-Server([string]$projectRoot) {
    Write-Step "Сборка VsCode Release"
    Push-Location $projectRoot
    & dotnet build (Join-Path $projectRoot "VsCode.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "dotnet build failed" }
    Pop-Location
    Write-Ok "VsCode.dll собран"
}

function Get-MatchmakingPluginDll([string]$projectRoot) {
    $built = Join-Path $projectRoot "PhotonPlugin\bin\Release\net472\MatchmakingPlugin.dll"
    $prebuilt = Join-Path $PSScriptRoot "built\MatchmakingPlugin.dll"
    if (Test-Path $built) { return $built }
    if (Test-Path $prebuilt) { return $prebuilt }
    return $null
}

function Build-PhotonPlugin([string]$projectRoot) {
    Write-Step "Сборка MatchmakingPlugin"
    $hive = Join-Path $projectRoot "PhotonPlugin\PhotonHivePlugin.dll"
    if (-not (Test-Path $hive)) {
        Write-Warn "PhotonHivePlugin.dll не найден — используем prebuilt"
        return Get-MatchmakingPluginDll $projectRoot
    }
    Push-Location (Join-Path $projectRoot "PhotonPlugin")
    & dotnet build MatchmakingPlugin.csproj -c Release --nologo
    Pop-Location
    $dll = Get-MatchmakingPluginDll $projectRoot
    if ($dll) { Write-Ok "Plugin: $dll" } else { Write-Warn "MatchmakingPlugin.dll не найден" }
    return $dll
}

function Resolve-PhotonSource([hashtable]$cfg, [string]$projectRoot) {
    $candidates = @()
    if ($cfg.PHOTON_SOURCE) {
        $candidates += if ([System.IO.Path]::IsPathRooted($cfg.PHOTON_SOURCE)) {
            $cfg.PHOTON_SOURCE
        } else {
            Join-Path $projectRoot $cfg.PHOTON_SOURCE
        }
    }
    $candidates += @(
        (Join-Path $projectRoot "third-party\Photon-SDK"),
        "C:\Users\Administrator\Desktop\standoff-server-84.21.173.237\Photon-OnPremise-Server-SDK_v4-0-29-11263"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c "deploy\bin_Win64\PhotonSocketServer.exe"))) {
            return (Resolve-Path $c).Path
        }
    }
    return $null
}

function Install-PhotonSdk([hashtable]$cfg, [string]$projectRoot) {
    Write-Step "Photon Server"
    $dest = $cfg.PHOTON_INSTALL_ROOT
    if (-not $dest) { $dest = "C:\PhotonServer" }

    $exe = Join-Path $dest "deploy\bin_Win64\PhotonSocketServer.exe"
    if (Test-Path $exe) {
        Write-Ok "Photon уже установлен: $dest"
        return $dest
    }

    $source = Resolve-PhotonSource $cfg $projectRoot
    if (-not $source) {
        Write-Warn "Photon SDK не найден. Укажите PHOTON_SOURCE в config.env или положите SDK в third-party\Photon-SDK"
        return $null
    }

    Write-Host "Копирование Photon SDK: $source -> $dest"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    robocopy $source $dest /E /XD log /XF *.log plugin_debug.log /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Photon robocopy failed" }
    Write-Ok "Photon SDK -> $dest"
    return $dest
}

function Configure-Photon([hashtable]$cfg, [string]$photonRoot, [string]$publicIp, [string]$pluginDll) {
    if (-not $photonRoot) { return }

    Write-Step "Настройка Photon"
    $pluginDir = Join-Path $photonRoot "deploy\Plugins\MatchmakingPlugin\bin"
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

    if ($pluginDll -and (Test-Path $pluginDll)) {
        Copy-Item $pluginDll (Join-Path $pluginDir "MatchmakingPlugin.dll") -Force
        Write-Ok "Plugin -> $pluginDir"
    }

    $httpPort = $cfg.HTTP_API_PORT
    $configs = Get-ChildItem -Path (Join-Path $photonRoot "deploy") -Recurse -Filter "Photon.LoadBalancing.dll.config" -ErrorAction SilentlyContinue
    foreach ($config in $configs) {
        $xml = Get-Content $config.FullName -Raw -Encoding UTF8
        $xml = $xml -replace '(<setting name="PublicIPAddress" serializeAs="String">\s*<value>)[^<]*(</value>)', "`${1}$publicIp`${2}"
        $xml = $xml -replace 'ServerUrl="http://127\.0\.0\.1:\d+"', "ServerUrl=`"http://127.0.0.1:$httpPort`""
        Set-Content $config.FullName $xml -Encoding UTF8 -NoNewline
        Write-Ok "Patched: $($config.FullName)"
    }

    Get-ChildItem -LiteralPath $photonRoot -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
}

function Install-ScheduledTask([string]$name, [string]$exe, [string]$args, [string]$workDir, [hashtable]$cfg) {
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    $action = New-ScheduledTaskAction -Execute $exe -Argument $args -WorkingDirectory $workDir
    $trigger = if ($cfg.AUTO_START_AT_BOOT -eq "true") { New-ScheduledTaskTrigger -AtStartup } else { New-ScheduledTaskTrigger -AtLogOn }
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -RunLevel Highest -Force | Out-Null
    Write-Ok "Задача: $name"
}

function Install-Autostart([hashtable]$cfg, [string]$projectRoot, [string]$photonRoot) {
    Write-Step "Автозапуск"
    if ($cfg.AUTO_START_RPC -eq "true") {
        $dll = Join-Path $projectRoot "bin\Release\net7.0\VsCode.dll"
        Install-ScheduledTask "StandRise-RPC" (Get-Command dotnet).Source "`"$dll`"" (Split-Path $dll) $cfg
    }
    if ($cfg.AUTO_START_PHOTON -eq "true" -and $photonRoot) {
        $photonExe = Join-Path $photonRoot "deploy\bin_Win64\PhotonSocketServer.exe"
        $photonDir = Split-Path $photonExe
        Install-ScheduledTask "StandRise-Photon" $photonExe "/run LoadBalancing" $photonDir $cfg
    }
}

function Stop-StandRiseProcesses {
    Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*VsCode.dll*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Get-Process PhotonSocketServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Start-ServicesNow([hashtable]$cfg, [string]$projectRoot, [string]$photonRoot) {
    Write-Step "Запуск сервисов"
    Stop-StandRiseProcesses
    Start-Sleep -Seconds 2

    $dll = Join-Path $projectRoot "bin\Release\net7.0\VsCode.dll"
    Start-Process (Get-Command dotnet).Source -ArgumentList "`"$dll`"" -WorkingDirectory (Split-Path $dll) -WindowStyle Minimized
    Write-Ok "RPC запущен"

    if ($photonRoot) {
        $photonExe = Join-Path $photonRoot "deploy\bin_Win64\PhotonSocketServer.exe"
        $photonDir = Split-Path $photonExe
        foreach ($logDir in @(
            (Join-Path $photonRoot "deploy\log"),
            (Join-Path $photonDir "log")
        )) { New-Item -ItemType Directory -Force -Path $logDir | Out-Null }
        Start-Process $photonExe -ArgumentList "/run LoadBalancing" -WorkingDirectory $photonDir -WindowStyle Minimized
        Write-Ok "Photon запущен"
    }
}

function Test-Deployment([hashtable]$cfg, [string]$publicIp, [string]$photonRoot) {
    Write-Step "Проверка"
    Start-Sleep -Seconds 5
    foreach ($t in @(
        @{ Name = "Mongo Main"; Port = [int]$cfg.MONGO_MAIN_PORT },
        @{ Name = "Mongo Game"; Port = [int]$cfg.MONGO_GAME_PORT },
        @{ Name = "RPC"; Port = [int]$cfg.RPC_PORT },
        @{ Name = "HTTP API"; Port = [int]$cfg.HTTP_API_PORT }
    )) {
        $r = Test-NetConnection 127.0.0.1 -Port $t.Port -WarningAction SilentlyContinue
        if ($r.TcpTestSucceeded) { Write-Ok "$($t.Name) :$($t.Port)" }
        else { Write-Warn "$($t.Name) :$($t.Port) — не слушается" }
    }
    if ($photonRoot) {
        $udp = Test-NetConnection 127.0.0.1 -Port ([int]$cfg.PHOTON_MASTER_PORT) -WarningAction SilentlyContinue
        if ($udp.TcpTestSucceeded) { Write-Ok "Photon Master UDP :$($cfg.PHOTON_MASTER_PORT)" }
    }
    Write-Host "`n=== ENDPOINTS ===" -ForegroundColor Yellow
    Write-Host "PublicIp:  $publicIp"
    Write-Host "RPC:       ${publicIp}:$($cfg.RPC_PORT)"
    Write-Host "Photon:    ${publicIp}:$($cfg.PHOTON_MASTER_PORT) / $($cfg.PHOTON_GAME_PORT)"
    Write-Host "Plugin:    ServerUrl=http://127.0.0.1:$($cfg.HTTP_API_PORT)"
}

# ========== MAIN ==========
Write-Host @"

 StandRise — полная установка
 Hostname: $env:COMPUTERNAME
 См. CLAUDE.md

"@ -ForegroundColor White

$cfg = Load-Config
if (-not $cfg.INSTALL_ROOT)       { $cfg.INSTALL_ROOT = "C:\StandRise" }
if (-not $cfg.PHOTON_INSTALL_ROOT){ $cfg.PHOTON_INSTALL_ROOT = "C:\PhotonServer" }
if (-not $cfg.MONGO_MAIN_PORT)     { $cfg.MONGO_MAIN_PORT = "2077" }
if (-not $cfg.MONGO_GAME_PORT)     { $cfg.MONGO_GAME_PORT = "1337" }
if (-not $cfg.MONGO_MAIN_DATA)     { $cfg.MONGO_MAIN_DATA = "$($cfg.INSTALL_ROOT)\data\mongo-main" }
if (-not $cfg.MONGO_GAME_DATA)     { $cfg.MONGO_GAME_DATA = "$($cfg.INSTALL_ROOT)\data\mongo-game" }
if (-not $cfg.RPC_PORT)            { $cfg.RPC_PORT = "2222" }
if (-not $cfg.HTTP_API_PORT)       { $cfg.HTTP_API_PORT = "2224" }
if (-not $cfg.PHOTON_MASTER_PORT)  { $cfg.PHOTON_MASTER_PORT = "5055" }
if (-not $cfg.PHOTON_GAME_PORT)    { $cfg.PHOTON_GAME_PORT = "5056" }
if (-not $cfg.AUTO_START_PHOTON)   { $cfg.AUTO_START_PHOTON = "true" }

$publicIp = Get-PublicIp $cfg
Write-Ok "PUBLIC_IP = $publicIp"

Ensure-Directories $cfg
$sourceRoot = Find-ProjectRoot $cfg.INSTALL_ROOT
$projectRoot = Sync-ProjectToInstallRoot $sourceRoot $cfg

Install-DotNet7
$mongod = Install-MongoDB
Write-MongoConfigs $cfg
Install-MongoServices $cfg $mongod
Initialize-MongoUsers $cfg
Import-MongoDumpIfPresent $cfg
Write-LocalSettings $cfg $publicIp $projectRoot
Set-FirewallRules $cfg
Build-Server $projectRoot
$pluginDll = Build-PhotonPlugin $projectRoot
$photonRoot = Install-PhotonSdk $cfg $projectRoot
Configure-Photon $cfg $photonRoot $publicIp $pluginDll
Install-Autostart $cfg $projectRoot $photonRoot
Start-ServicesNow $cfg $projectRoot $photonRoot
Test-Deployment $cfg $publicIp $photonRoot

Write-Host @"

========================================
 УСТАНОВКА ЗАВЕРШЕНА
========================================
 Проект:     $($cfg.INSTALL_ROOT)
 Photon:     $($cfg.PHOTON_INSTALL_ROOT)
 PublicIp:   $publicIp
 RPC:        ${publicIp}:$($cfg.RPC_PORT)
 Photon:     ${publicIp}:$($cfg.PHOTON_MASTER_PORT) / $($cfg.PHOTON_GAME_PORT)

 Управление:
   deploy\Start-StandRise.ps1
   deploy\Stop-StandRise.ps1

 Документация: CLAUDE.md

"@ -ForegroundColor Green
