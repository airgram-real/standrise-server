param(
    [string]$InstallRoot = "C:\StandRise",
    [string]$PhotonRoot = "C:\PhotonServer",
    [ValidateSet("csharp","python")]
    [string]$RpcEngine = $(if ($env:RPC_ENGINE) { $env:RPC_ENGINE } else { "python" })
)

$ErrorActionPreference = "Stop"

# Stop previous RPC (dotnet VsCode OR python standrise)
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*VsCode.dll*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*standrise.main*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

if ($RpcEngine -eq "python") {
    $py = "C:\Program Files\Python312\python.exe"
    if (-not (Test-Path $py)) { throw "Python not found: $py - install Python 3.12 first" }
    $wd = Join-Path $InstallRoot "python_server"
    $env:STANDRISE_SETTINGS = Join-Path $InstallRoot "bin\Release\net7.0\local.settings.json"
    Start-Process -FilePath $py -ArgumentList "-m standrise.main" -WorkingDirectory $wd -WindowStyle Minimized
    Write-Host "[OK] RPC started (Python) settings=$($env:STANDRISE_SETTINGS)"
} else {
    $dll = Join-Path $InstallRoot "bin\Release\net7.0\VsCode.dll"
    if (-not (Test-Path $dll)) { throw "Not found: $dll" }
    Start-Process -FilePath "$env:ProgramFiles\dotnet\dotnet.exe" -ArgumentList "`"$dll`"" -WorkingDirectory (Split-Path $dll) -WindowStyle Minimized
    Write-Host "[OK] RPC started (C#)"
}

$photonExe = Join-Path $PhotonRoot "deploy\bin_Win64\PhotonSocketServer.exe"
if (Test-Path $photonExe) {
    Get-Process PhotonSocketServer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process $photonExe -ArgumentList "/run LoadBalancing" -WorkingDirectory (Split-Path $photonExe) -WindowStyle Minimized
    Write-Host "[OK] Photon started"
} else {
    Write-Host "[WARN] Photon not found: $photonExe"
}
