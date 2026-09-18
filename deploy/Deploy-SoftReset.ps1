#Requires -RunAsAdministrator
# Deploy patched sources to C:\StandRise, soft-reset game DB (no account wipe), rebuild RPC.
param(
    [string]$SourceRoot = "C:\Users\Administrator\Desktop\StandRise-FULL-20260910-2203",
    [string]$InstallRoot = "C:\StandRise"
)

$ErrorActionPreference = "Stop"
$Log = Join-Path $PSScriptRoot "deploy-softreset-log.txt"
function L([string]$m) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $m
    Write-Host $line
    Add-Content -LiteralPath $Log -Value $line -Encoding UTF8
}

Set-Content -LiteralPath $Log -Value "=== Deploy + soft reset $(Get-Date) ===" -Encoding UTF8
$env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")

$files = @(
    "ProtoFiless\StubMessages.cs",
    "RpcServer\Api\MatchmakingRemoteService.cs",
    "RpcServer\Api\InventoryRemoteService.cs",
    "RpcServer\Api\MatchesRemoteService.cs",
    "RpcServer\Api\ClanRemoteService.cs",
    "MongoDB\BoltGameDatabaseProvider.cs",
    "MongoDB\BoltMainDatabaseProvider.cs"
)

L "--- 1. Copy sources -> $InstallRoot ---"
foreach ($rel in $files) {
    $from = Join-Path $SourceRoot $rel
    $to = Join-Path $InstallRoot $rel
    if (-not (Test-Path $from)) { throw "Missing source: $from" }
    $dir = Split-Path $to -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Copy-Item -LiteralPath $from -Destination $to -Force
    L "  copied $rel"
}

L "--- 2. Stop RPC + Photon ---"
& (Join-Path $InstallRoot "deploy\Stop-StandRise.ps1")

$user = "eliseyy22"
$pass = "eliseyy22!"
$passEsc = [uri]::EscapeDataString($pass)
$uriMain = "mongodb://${user}:${passEsc}@127.0.0.1:2077/?authSource=admin"
$uriGame = "mongodb://${user}:${passEsc}@127.0.0.1:1337/?authSource=admin"

L "--- 3. Ensure Mongo listening ---"
foreach ($pair in @(@(2077, "StandRiseMongoMain"), @(1337, "StandRiseMongoGame"))) {
    $port = $pair[0]; $svc = $pair[1]
    if (-not (Test-NetConnection 127.0.0.1 -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded) {
        L "  starting service $svc"
        Start-Service $svc -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 5
    }
    if ((Test-NetConnection 127.0.0.1 -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded) {
        L "  OK mongo port $port"
    } else {
        throw "Mongo port $port is DOWN"
    }
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $InstallRoot "deploy\backups\pre-softreset-$stamp"
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

L "--- 4. Backup StandRise DB (both instances) ---"
$mongodump = Get-ChildItem "${env:ProgramFiles}\MongoDB\Tools\*\bin\mongodump.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($mongodump) {
    & $mongodump.FullName --uri=$uriMain --db=StandRise --out=(Join-Path $backupRoot "main-2077") 2>&1 | ForEach-Object { L "  dump-main $_" }
    & $mongodump.FullName --uri=$uriGame --db=StandRise --out=(Join-Path $backupRoot "game-1337") 2>&1 | ForEach-Object { L "  dump-game $_" }
    L "  backup -> $backupRoot"
} else {
    L "  WARN mongodump not found - skipping file backup (soft reset only)"
}

$mongosh = (Get-ChildItem "${env:ProgramFiles}\MongoDB\Server\*\bin\mongosh.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if (-not $mongosh) { throw "mongosh not found" }

L "--- 5. Soft reset game DB (1337) ---"
$jsFile = Join-Path $PSScriptRoot "softreset-game.js"
if (-not (Test-Path $jsFile)) { $jsFile = Join-Path $SourceRoot "deploy\softreset-game.js" }
& $mongosh $uriGame --quiet --file $jsFile 2>&1 | ForEach-Object { L "  $_" }

L "--- 6. Main DB (2077): no destructive changes (accounts/clans/invites kept) ---"

L "--- 7. Build + restart RPC ---"
$buildScript = Join-Path $InstallRoot "deploy\Build-Restart.ps1"
if (-not (Test-Path $buildScript)) { throw "Missing $buildScript" }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript
if ($LASTEXITCODE -ne 0) { L "WARN build exit code $LASTEXITCODE" }

L "--- 8. Start Photon ---"
$photon = "C:\PhotonServer\deploy\bin_Win64\PhotonSocketServer.exe"
if (Test-Path $photon) {
    Get-Process PhotonSocketServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process $photon -ArgumentList "/run LoadBalancing" -WorkingDirectory (Split-Path $photon) -WindowStyle Minimized
    Start-Sleep -Seconds 8
    L "  Photon started"
}

foreach ($p in 2222, 2224, 2077, 1337, 5055) {
    $ok = (Test-NetConnection 127.0.0.1 -Port $p -WarningAction SilentlyContinue).TcpTestSucceeded
    L ("  port {0}: {1}" -f $p, $(if ($ok) { "OK" } else { "DOWN" }))
}

L "=== DONE backup=$backupRoot ==="
