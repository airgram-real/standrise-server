#Requires -RunAsAdministrator
# Full reset: all player/clan/inventory/match data removed; Mongo auth and server code untouched.
# Rollback: restore deploy\backups\fullreset-*\mongo-main and mongo-game while mongod stopped.
param(
    [string]$InstallRoot = "C:\StandRise",
    [switch]$SkipBackup
)

$ErrorActionPreference = "Stop"
$Log = Join-Path $PSScriptRoot "fullreset-log.txt"
function L([string]$m) {
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $m
    Write-Host $line
    Add-Content -LiteralPath $Log -Value $line -Encoding UTF8
}

Set-Content -LiteralPath $Log -Value "=== Full DB reset $(Get-Date) ===" -Encoding UTF8
$env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")

$user = "eliseyy22"
$pass = "eliseyy22!"
$passEsc = [uri]::EscapeDataString($pass)
$uriMain = "mongodb://${user}:${passEsc}@127.0.0.1:2077/?authSource=admin"
$uriGame = "mongodb://${user}:${passEsc}@127.0.0.1:1337/?authSource=admin"

L "--- 1. Stop RPC + Photon ---"
& (Join-Path $InstallRoot "deploy\Stop-StandRise.ps1")

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $InstallRoot "deploy\backups\fullreset-$stamp"
$mainData = Join-Path $InstallRoot "data\mongo-main"
$gameData = Join-Path $InstallRoot "data\mongo-game"

if (-not $SkipBackup) {
    L "--- 2. Backup Mongo data directories (rollback copy) ---"
    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
    foreach ($svc in @("StandRiseMongoMain", "StandRiseMongoGame")) {
        $s = Get-Service $svc -ErrorAction SilentlyContinue
        if ($s -and $s.Status -eq "Running") {
            L "  stopping $svc for consistent backup..."
            Stop-Service $svc -Force -ErrorAction SilentlyContinue
        }
    }
    $waited = 0
    while ((@(Get-Process mongod -ErrorAction SilentlyContinue).Count -gt 0) -and ($waited -lt 90)) {
        Start-Sleep -Seconds 3
        $waited += 3
    }
    if (@(Get-Process mongod -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "mongod did not stop cleanly - aborting (no data touched after stop RPC)"
    }
    foreach ($pair in @(@($mainData, "mongo-main"), @($gameData, "mongo-game"))) {
        $src, $name = $pair
        if (Test-Path $src) {
            $dest = Join-Path $backupRoot $name
            robocopy $src $dest /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $name" }
            L "  backed up $src -> $dest"
        }
    }
    foreach ($svc in @("StandRiseMongoMain", "StandRiseMongoGame")) {
        Start-Service $svc -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 6
} else {
    L "--- 2. SkipBackup ---"
}

L "--- 3. Ensure Mongo up ---"
foreach ($pair in @(@(2077, "StandRiseMongoMain"), @(1337, "StandRiseMongoGame"))) {
    $port = $pair[0]; $svc = $pair[1]
    if (-not (Test-NetConnection 127.0.0.1 -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded) {
        Start-Service $svc -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 5
    }
    if (-not (Test-NetConnection 127.0.0.1 -Port $port -WarningAction SilentlyContinue).TcpTestSucceeded) {
        throw "Mongo port $port is DOWN"
    }
    L "  mongo port $port OK"
}

$mongosh = (Get-ChildItem "${env:ProgramFiles}\MongoDB\Server\*\bin\mongosh.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if (-not $mongosh) { throw "mongosh not found" }

function Invoke-MongoJs([string]$uri, [string]$jsPath, [string]$tag) {
    L "  $tag"
    & $mongosh $uri --quiet --file $jsPath 2>&1 | ForEach-Object { L "    $_" }
}

$dropMain = Join-Path $env:TEMP "sr-drop-main.js"
@'
var names = db.getMongo().getDBNames();
if (names.indexOf("StandRise") >= 0) {
  db.getSiblingDB("StandRise").dropDatabase();
  print("dropped StandRise on MAIN (2077)");
} else {
  print("StandRise not present on MAIN");
}
'@ | Set-Content -Path $dropMain -Encoding UTF8

$dropGame = Join-Path $env:TEMP "sr-drop-game.js"
@'
["StandRise", "Inventory"].forEach(function(n) {
  var names = db.getMongo().getDBNames();
  if (names.indexOf(n) >= 0) {
    db.getSiblingDB(n).dropDatabase();
    print("dropped " + n + " on GAME (1337)");
  }
});
'@ | Set-Content -Path $dropGame -Encoding UTF8

L "--- 4. Drop game + main StandRise databases ---"
Invoke-MongoJs $uriGame $dropGame "GAME drop"
Invoke-MongoJs $uriMain $dropMain "MAIN drop"

$seedPath = Join-Path $PSScriptRoot "fullreset-seed-game.js"
if (-not (Test-Path $seedPath)) { throw "Missing $seedPath" }
L "--- 5. Seed game defaults ---"
Invoke-MongoJs $uriGame $seedPath "GAME seed"

$dumpRoot = Join-Path $PSScriptRoot "mongo-dump"
$mongorestore = Get-ChildItem "${env:ProgramFiles}\MongoDB\Tools\*\bin\mongorestore.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($mongorestore -and (Test-Path (Join-Path $dumpRoot "Main"))) {
    L "--- 6. Optional mongorestore from deploy\mongo-dump ---"
    & $mongorestore.FullName --uri=$uriMain --drop (Join-Path $dumpRoot "Main") 2>&1 | ForEach-Object { L "    $_" }
    if (Test-Path (Join-Path $dumpRoot "Inventory")) {
        & $mongorestore.FullName --uri=$uriGame --drop (Join-Path $dumpRoot "Inventory") 2>&1 | ForEach-Object { L "    $_" }
    }
} else {
    L "--- 6. No mongo-dump - fresh empty MAIN (new registrations) ---"
}

L "--- 7. Restart RPC + Photon ---"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $InstallRoot "deploy\Build-Restart.ps1")
$photon = "C:\PhotonServer\deploy\bin_Win64\PhotonSocketServer.exe"
if (Test-Path $photon) {
    Get-Process PhotonSocketServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process $photon -ArgumentList "/run LoadBalancing" -WorkingDirectory (Split-Path $photon) -WindowStyle Minimized
    Start-Sleep -Seconds 6
}

foreach ($p in 2222, 2224, 2077, 1337) {
    $ok = (Test-NetConnection 127.0.0.1 -Port $p -WarningAction SilentlyContinue).TcpTestSucceeded
    L ("  port {0}: {1}" -f $p, $(if ($ok) { "OK" } else { "DOWN" }))
}

L "=== FULL RESET DONE backup=$backupRoot ==="
L "Rollback: stop mongo services, delete data\mongo-main + mongo-game, rename backup folders back, start services."
