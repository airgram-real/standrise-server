# StandRise: recover the game database (match history) from the folder that was
# set aside as "broken" on 2026-09-14 at 11:19, without destroying the current one.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads .ps1 in the ANSI codepage.
#
# Safety: nothing is deleted and nothing is moved. The current live folder is COPIED
# to a timestamped backup first, the old folder is COPIED to a scratch dir and repaired
# THERE, and only a successful repair is swapped in. If anything fails, the live folder
# is left exactly as it was.
#
# Report: C:\StandRise\deploy\restore_report.txt

$ErrorActionPreference = "Continue"
$report = "C:\StandRise\deploy\restore_report.txt"
Start-Transcript -Path $report -Force | Out-Null

$root     = "C:\StandRise\data"
$live     = Join-Path $root "mongo-game"
$old      = Join-Path $root "mongo-game.broken-20260914-111952"
$stamp    = Get-Date -Format "yyyyMMdd-HHmmss"
$backup   = Join-Path $root ("mongo-game.autobackup-" + $stamp)
$scratch  = Join-Path $root ("mongo-game.repair-" + $stamp)

function Find-Mongod {
    $candidates = @(
        "C:\Program Files\MongoDB\Server\7.0\bin\mongod.exe",
        "C:\Program Files\MongoDB\Server\6.0\bin\mongod.exe",
        "C:\Program Files\MongoDB\Server\5.0\bin\mongod.exe",
        "C:\Program Files\MongoDB\Server\8.0\bin\mongod.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $found = Get-ChildItem "C:\Program Files\MongoDB\Server" -Recurse -Filter mongod.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }
    $cmd = Get-Command mongod -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

Write-Host "=== 1. CHECKS ==="
if (-not (Test-Path $old)) { Write-Host "[FAIL] not found: $old"; Stop-Transcript | Out-Null; exit 1 }
$mongod = Find-Mongod
if (-not $mongod) { Write-Host "[FAIL] mongod.exe not found"; Stop-Transcript | Out-Null; exit 1 }
Write-Host "mongod: $mongod"
Write-Host ("old data size: " + [math]::Round((Get-ChildItem $old -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1MB, 1) + " MB")

Write-Host ""
Write-Host "=== 2. STOP RPC + GAME MONGO ==="
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*VsCode.dll*" } |
    ForEach-Object { Write-Host ("  stopping RPC PID " + $_.ProcessId); Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
$svc = Get-Service StandRiseMongoGame -ErrorAction SilentlyContinue
if ($svc) { Write-Host "  stopping service StandRiseMongoGame"; Stop-Service StandRiseMongoGame -Force -ErrorAction SilentlyContinue }
else {
    Get-CimInstance Win32_Process -Filter "Name='mongod.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like "*mongo-game*" -or $_.CommandLine -like "*1337*" } |
        ForEach-Object { Write-Host ("  stopping mongod PID " + $_.ProcessId); Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
Start-Sleep -Seconds 5

Write-Host ""
Write-Host "=== 3. BACKUP CURRENT ==="
Copy-Item $live $backup -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $backup) { Write-Host "[OK] current data copied to $backup" }
else { Write-Host "[FAIL] backup failed - aborting, nothing changed"; Stop-Transcript | Out-Null; exit 1 }

Write-Host ""
Write-Host "=== 4. REPAIR OLD DATA IN SCRATCH ==="
Copy-Item $old $scratch -Recurse -Force
Get-ChildItem $scratch -Filter "mongod.lock" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
& $mongod --dbpath $scratch --repair
$repairExit = $LASTEXITCODE
Write-Host ("repair exit code: " + $repairExit)

if ($repairExit -ne 0) {
    Write-Host "[STOP] repair failed - live data untouched, scratch left at $scratch for inspection."
    Stop-Transcript | Out-Null
    exit $repairExit
}

Write-Host ""
Write-Host "=== 5. SWAP IN ==="
Rename-Item $live ($live + ".replaced-" + $stamp) -ErrorAction SilentlyContinue
if (Test-Path $live) { Write-Host "[FAIL] could not move live folder aside - aborting"; Stop-Transcript | Out-Null; exit 1 }
Rename-Item $scratch $live
Write-Host "[OK] repaired data is now $live"

Write-Host ""
Write-Host "=== 6. START MONGO + RPC ==="
if ($svc) { Start-Service StandRiseMongoGame -ErrorAction SilentlyContinue; Start-Sleep -Seconds 6 }
else { Write-Host "[WARN] service StandRiseMongoGame not found - start mongod the way you normally do" }

$dotnet = "$env:ProgramFiles\dotnet\dotnet.exe"
$dll = "C:\StandRise\bin\Release\net7.0\VsCode.dll"
if ((Test-Path $dotnet) -and (Test-Path $dll)) {
    Start-Process -FilePath $dotnet -ArgumentList "`"$dll`"" -WorkingDirectory (Split-Path $dll) -WindowStyle Minimized
    Start-Sleep -Seconds 8
}

Write-Host ""
Write-Host "=== 7. VERIFY ==="
if (Get-NetTCPConnection -LocalPort 1337 -State Listen -ErrorAction SilentlyContinue) { Write-Host "[OK] mongo game port 1337 listening" }
else { Write-Host "[FAIL] port 1337 NOT listening" }
if (Get-NetTCPConnection -LocalPort 2222 -State Listen -ErrorAction SilentlyContinue) { Write-Host "[OK] RPC port 2222 listening" }
else { Write-Host "[FAIL] RPC port 2222 NOT listening" }

$mongosh = Get-Command mongosh -ErrorAction SilentlyContinue
if ($mongosh) {
    $uri = "mongodb://eliseyy22:eliseyy22!@127.0.0.1:1337/?authSource=admin&authMechanism=SCRAM-SHA-256"
    Write-Host "--- document counts ---"
    & $mongosh.Source $uri --quiet --eval "db=db.getSiblingDB('Inventory'); print('player_match_history=' + db.player_match_history.countDocuments({})); print('player_last_matches=' + db.player_last_matches.countDocuments({}));"
} else {
    Write-Host "[INFO] mongosh not installed - check counts in the RPC log (db=N in [Matches] lines)"
}

Write-Host ""
Write-Host "Rollback: stop mongo, delete $live, rename $backup back to mongo-game."
Stop-Transcript | Out-Null
