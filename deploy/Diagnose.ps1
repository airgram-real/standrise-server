# StandRise diagnostics -> diagnose-log.txt
$ErrorActionPreference = "Continue"
$Log = Join-Path $PSScriptRoot "diagnose-log.txt"
function L([string]$m) { Write-Host $m; Add-Content -LiteralPath $Log -Value $m -Encoding UTF8 }
Set-Content -LiteralPath $Log -Value "=== StandRise diagnose $(Get-Date) ===" -Encoding UTF8

L "Host: $env:COMPUTERNAME"
L ""
L "--- .NET SDKs ---"
try { (& dotnet --list-sdks) | ForEach-Object { L "  $_" } } catch { L "  dotnet missing" }

L ""
L "--- Services ---"
foreach ($s in "StandRiseMongoMain","StandRiseMongoGame","MongoDB") {
    $o = Get-Service $s -ErrorAction SilentlyContinue
    L ("  {0}: {1}" -f $s, $(if ($o) { $o.Status } else { "MISSING" }))
}

L ""
L "--- Processes ---"
Get-Process dotnet,PhotonSocketServer,mongod -ErrorAction SilentlyContinue |
    ForEach-Object { L ("  {0} pid={1}" -f $_.ProcessName, $_.Id) }

L ""
L "--- TCP ports ---"
foreach ($p in 2222,2224,2077,1337) {
    $r = Test-NetConnection 127.0.0.1 -Port $p -WarningAction SilentlyContinue
    L ("  {0}: {1}" -f $p, $(if ($r.TcpTestSucceeded) { "LISTENING" } else { "DOWN" }))
}
L "--- UDP endpoints 5055/5056 ---"
$u = Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in 5055,5056 }
if ($u) { $u | ForEach-Object { L ("  {0}:{1}" -f $_.LocalAddress, $_.LocalPort) } } else { L "  none" }

L ""
L "--- local.settings.json ---"
$ls = "C:\StandRise\bin\Release\net7.0\local.settings.json"
if (Test-Path $ls) { Get-Content $ls | ForEach-Object { L "  $_" } } else { L "  MISSING" }

L ""
L "--- Photon config ---"
Get-ChildItem "C:\PhotonServer\deploy" -Recurse -Filter "Photon.LoadBalancing.dll.config" -ErrorAction SilentlyContinue | ForEach-Object {
    L "  $($_.FullName)"
    Select-String -LiteralPath $_.FullName -Pattern "PublicIPAddress|ServerUrl" -ErrorAction SilentlyContinue |
        Select-Object -First 8 | ForEach-Object { L "     $($_.Line.Trim())" }
}
L "  plugin dll present: $(Test-Path 'C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\MatchmakingPlugin.dll')"

L ""
L "--- RPC log (tail 60) ---"
$r = Get-ChildItem "C:\StandRise\bin\Release\net7.0\logs\*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($r) { L "  file: $($r.FullName)"; Get-Content $r.FullName -Tail 60 | ForEach-Object { L "   $_" } } else { L "  none" }

L ""
L "--- Photon log (tail 40) ---"
$p = Get-ChildItem "C:\PhotonServer\deploy\log\*.log","C:\PhotonServer\deploy\bin_Win64\log\*.log","C:\PhotonServer\deploy\Loadbalancing\*\bin\log\*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 2
if ($p) { $p | ForEach-Object { L "  file: $($_.FullName)"; Get-Content $_.FullName -Tail 40 | ForEach-Object { L "   $_" } } } else { L "  none" }

L ""
L "--- Plugin debug log (tail 30) ---"
$pd = "C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\plugin_debug.log"
if (Test-Path $pd) { Get-Content $pd -Tail 30 | ForEach-Object { L "   $_" } } else { L "  none" }

L ""
L "--- Mongo logs (tail 15 each) ---"
foreach ($m in "C:\StandRise\logs\mongod-main.log","C:\StandRise\logs\mongod-game.log") {
    L "  $m"
    if (Test-Path $m) { Get-Content $m -Tail 15 | ForEach-Object { L "   $_" } } else { L "   MISSING" }
}

L ""
L "=== DIAGNOSE DONE ==="
