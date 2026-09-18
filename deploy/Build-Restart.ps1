# StandRise: build VsCode.dll and restart the RPC process.
# ASCII only on purpose: Windows PowerShell 5.1 reads .ps1 in the ANSI codepage.
#
# The live bin\Release\net7.0\VsCode.dll is locked while the server runs, so the RPC
# has to be stopped BEFORE the build. If the build then fails, nothing was copied and
# bin still holds the previous VsCode.dll - the script starts that one back up, so the
# server never stays down.
#
# Report: C:\StandRise\deploy\build_report.txt
param(
    [switch]$NoRestart
)

$ErrorActionPreference = "Continue"
$report = "C:\StandRise\deploy\build_report.txt"
Start-Transcript -Path $report -Force | Out-Null

function Find-Dotnet {
    $candidates = @(
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "C:\Program Files\dotnet\dotnet.exe",
        "C:\Users\Administrator\dotnet-sdk\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

Write-Host "=== 1. TOOLCHAIN ==="
$dotnet = Find-Dotnet
if (-not $dotnet) {
    Write-Host "[FAIL] dotnet not found"
    Stop-Transcript | Out-Null
    exit 1
}
Write-Host "dotnet: $dotnet"

# A previous version of this script built into obj\buildcheck. The project globs obj\**,
# so a leftover tree there duplicates the generated AssemblyInfo and breaks every build
# with CS0579. Remove it before doing anything else.
$stale = "C:\StandRise\obj\buildcheck"
if (Test-Path $stale) {
    Write-Host "removing stale $stale"
    Remove-Item $stale -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== 2. STOP RPC ==="
$stopped = 0
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*VsCode.dll*" } |
    ForEach-Object {
        Write-Host ("  stopping dotnet PID " + $_.ProcessId)
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        $stopped++
    }
if ($stopped -eq 0) { Write-Host "  no running RPC found" }
Start-Sleep -Seconds 3

Write-Host ""
Write-Host "=== 3. BUILD ==="
Push-Location "C:\StandRise"
$buildOutput = & $dotnet build "C:\StandRise\VsCode.csproj" -c Release -v minimal --nologo 2>&1
$buildExit = $LASTEXITCODE
Pop-Location

# Only diagnostics go into the report - the full log is huge and lives in build_stdout.txt.
$errors = $buildOutput | Select-String -Pattern ": error " | Select-Object -First 40
if ($errors) {
    Write-Host "--- errors ---"
    $errors | ForEach-Object { Write-Host $_.Line }
    Write-Host "--- end errors ---"
}

if ($buildExit -ne 0) {
    Write-Host "[WARN] build failed (exit $buildExit) - starting the PREVIOUS VsCode.dll back up."
} else {
    Write-Host "[OK] build succeeded"
}

if ($NoRestart) {
    Write-Host "[SKIP] restart not requested - RPC left stopped"
    Stop-Transcript | Out-Null
    exit $buildExit
}

Write-Host ""
Write-Host "=== 4. START RPC ==="
$dll = "C:\StandRise\bin\Release\net7.0\VsCode.dll"
if (-not (Test-Path $dll)) {
    Write-Host "[FAIL] missing $dll"
    Stop-Transcript | Out-Null
    exit 1
}
Write-Host ("dll built at " + (Get-Item $dll).LastWriteTime)
Start-Process -FilePath $dotnet -ArgumentList "`"$dll`"" -WorkingDirectory (Split-Path $dll) -WindowStyle Minimized
Start-Sleep -Seconds 8

Write-Host ""
Write-Host "=== 5. POST-START CHECKS ==="
$running = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*VsCode.dll*" }
if ($running) { foreach ($p in $running) { Write-Host ("[OK] RPC PID " + $p.ProcessId) } }
else { Write-Host "[FAIL] RPC process did not start" }

if (Get-NetTCPConnection -LocalPort 2222 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host "[OK] port 2222 is listening"
} else {
    Write-Host "[FAIL] port 2222 is NOT listening"
}
if (Get-NetTCPConnection -LocalPort 2224 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host "[OK] port 2224 is listening"
} else {
    Write-Host "[WARN] port 2224 is NOT listening"
}

Write-Host ""
Write-Host "=== 6. LOG TAIL ==="
$log = Get-ChildItem "C:\StandRise\bin\Release\net7.0\logs\tcp_*.log" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($log) { Get-Content $log.FullName -Tail 20 } else { Write-Host "no tcp log yet" }

Stop-Transcript | Out-Null
exit $buildExit
