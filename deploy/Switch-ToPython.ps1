# Switch StandRise RPC from C# to Python.
#
# Runs the readiness preflight first. If it fails, nothing is touched.
# Photon is NOT restarted: running matches keep going, only the RPC process changes.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads .ps1 in the ANSI codepage,
# and non-ASCII text without a BOM breaks the parser.
#
# Report: C:\StandRise\deploy\switch_report.txt

$ErrorActionPreference = "Continue"
$report = "C:\StandRise\deploy\switch_report.txt"
Start-Transcript -Path $report -Force | Out-Null

$py = "C:\Program Files\Python312\python.exe"
$env:STANDRISE_SETTINGS = "C:\StandRise\bin\Release\net7.0\local.settings.json"

Write-Host "=== 1. PREFLIGHT ==="
if (-not (Test-Path $py)) {
    Write-Host "[FAIL] Python not found: $py"
    Stop-Transcript | Out-Null
    exit 1
}
Push-Location "C:\StandRise\python_server"
& $py -m standrise.preflight
$preflight = $LASTEXITCODE
Pop-Location
if ($preflight -ne 0) {
    Write-Host ""
    Write-Host "[STOP] preflight failed - engine NOT switched, C# keeps running."
    Stop-Transcript | Out-Null
    exit 1
}

Write-Host ""
Write-Host "=== 2. STOP C# RPC ==="
$csharp = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*VsCode.dll*" }
if ($csharp) {
    foreach ($p in $csharp) {
        Write-Host ("  stopping dotnet PID " + $p.ProcessId)
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "  no C# process found (already stopped)"
}

$oldpy = Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*standrise.main*" }
foreach ($p in $oldpy) {
    Write-Host ("  stopping old python PID " + $p.ProcessId)
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 2

Write-Host ""
Write-Host "=== 3. START PYTHON RPC ==="
Start-Process -FilePath $py -ArgumentList "-m standrise.main" -WorkingDirectory "C:\StandRise\python_server" -WindowStyle Minimized
Start-Sleep -Seconds 8

Write-Host ""
Write-Host "=== 4. POST-START CHECKS ==="
$running = Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*standrise.main*" }
if ($running) {
    foreach ($p in $running) { Write-Host ("[OK] python PID " + $p.ProcessId) }
} else {
    Write-Host "[FAIL] python process did not start - see log below"
}

if (Get-NetTCPConnection -LocalPort 2222 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host "[OK] port 2222 is listening"
} else {
    Write-Host "[FAIL] port 2222 is NOT listening"
}
if (Get-NetTCPConnection -LocalPort 2224 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host "[OK] port 2224 (HTTP API) is listening"
} else {
    Write-Host "[WARN] port 2224 is NOT listening"
}

Write-Host ""
Write-Host "=== 5. LOG TAIL ==="
$log = "C:\StandRise\bin\Release\net7.0\logs\python_rpc.log"
if (Test-Path $log) { Get-Content $log -Tail 40 } else { Write-Host "no log file: $log" }

Write-Host ""
Write-Host "Rollback to C#: Start-StandRise.ps1 -RpcEngine csharp"
Stop-Transcript | Out-Null
