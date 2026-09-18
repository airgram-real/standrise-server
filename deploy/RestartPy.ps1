#Requires -RunAsAdministrator
# ASCII ONLY.
# Restart the Python RPC (and Photon) and verify the ports.
$ErrorActionPreference = "Continue"
$Log = Join-Path $PSScriptRoot "restartpy-log.txt"
function L([string]$m) {
    $l = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $m
    Write-Host $l
    Add-Content -LiteralPath $Log -Value $l -Encoding UTF8
}
Set-Content -LiteralPath $Log -Value "=== StandRise restart python RPC $(Get-Date) ===" -Encoding UTF8
$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User")

$logFile = "C:\StandRise\bin\Release\net7.0\logs\python_rpc.log"

L "--- clearing python_rpc.log so the next login is the only thing in it ---"
try {
    if (Test-Path $logFile) {
        $arch = $logFile + ".before-" + (Get-Date -Format "yyyyMMdd-HHmmss")
        Move-Item -LiteralPath $logFile -Destination $arch -Force
        L ("  old log -> {0}" -f (Split-Path $arch -Leaf))
    }
} catch { L ("  log rotate warn: {0}" -f $_.Exception.Message) }

L "--- clearing stale __pycache__ ---"
Get-ChildItem -Path "C:\StandRise\python_server" -Filter "__pycache__" -Recurse -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

L "--- syntax check ---"
$py = "C:\Program Files\Python312\python.exe"
if (-not (Test-Path $py)) { L "python312 not found"; exit 1 }
$files = @(
    "C:\StandRise\python_server\standrise\session.py",
    "C:\StandRise\python_server\standrise\auth_store.py",
    "C:\StandRise\python_server\standrise\google_identity.py",
    "C:\StandRise\python_server\standrise\services\__init__.py",
    "C:\StandRise\python_server\standrise\services\handshake.py",
    "C:\StandRise\python_server\standrise\services\player.py",
    "C:\StandRise\python_server\standrise\services\google_auth.py"
)
$bad = $false
foreach ($f in $files) {
    & $py -m py_compile $f 2>&1 | ForEach-Object { L ("  " + $_) }
    if ($LASTEXITCODE -ne 0) { L ("  SYNTAX ERROR in {0}" -f $f); $bad = $true }
}
if ($bad) { L "=== ABORTED: syntax errors ==="; exit 1 }
L "  all files compile"

L "--- restart RPC + Photon ---"
& "C:\StandRise\deploy\Start-StandRise.ps1" 2>&1 | ForEach-Object { L ("  " + $_) }
Start-Sleep -Seconds 12

foreach ($p in 2222, 2224) {
    $r = Test-NetConnection 127.0.0.1 -Port $p -WarningAction SilentlyContinue
    if ($r.TcpTestSucceeded) { L ("  TCP {0}: LISTENING" -f $p) } else { L ("  TCP {0}: DOWN" -f $p) }
}
$proc = @(Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*standrise.main*" })
L ("  python standrise.main processes: {0}" -f $proc.Count)

L "--- accounts in main db ---"
$mongosh = (Get-ChildItem "${env:ProgramFiles}\MongoDB\Server\*\bin\mongosh.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if ($mongosh) {
    $js = @'
var d = db.getSiblingDB("StandRise");
var players = {};
d.player.find({}, {uid:1, name:1}).forEach(function(p){ players[String(p._id)] = (p.uid||"?") + "/" + (p.name||"?"); });
print("account_google (" + d.account_google.countDocuments({}) + "):");
d.account_google.find({}).forEach(function(a){
  var pid = String(a.playerId||"");
  print("  googleId=" + (a.googleId||"<none>") +
        " authCode=" + (a.authCode ? "yes" : "no") +
        " playerId=" + pid +
        " player=" + (players[pid] || "MISSING"));
});
print("account_test (" + d.account_test.countDocuments({}) + "):");
d.account_test.find({}).forEach(function(a){
  var pid = String(a.playerId||"");
  print("  authCode=" + (a.authCode||"?") + " playerId=" + pid + " player=" + (players[pid] || "MISSING"));
});
print("player (" + d.player.countDocuments({}) + "):");
d.player.find({}, {uid:1, name:1, createDate:1}).forEach(function(p){
  print("  _id=" + p._id + " uid=" + (p.uid||"?") + " name=" + (p.name||"?"));
});
'@
    $f = Join-Path $env:TEMP "accounts.js"
    [IO.File]::WriteAllText($f, $js, (New-Object Text.UTF8Encoding($false)))
    & $mongosh "mongodb://eliseyy22:eliseyy22%21@127.0.0.1:2077/?authSource=admin" --quiet --file $f 2>&1 | ForEach-Object { L ("  " + $_) }
    Remove-Item $f -Force -ErrorAction SilentlyContinue
} else {
    L "  mongosh not found"
}

L "--- head of fresh python_rpc.log ---"
if (Test-Path $logFile) {
    Get-Content -LiteralPath $logFile -TotalCount 40 |
        Where-Object { $_ -notlike "*pymongo*" } |
        ForEach-Object { L ("  " + $_) }
} else {
    L "  python_rpc.log not created - RPC probably did not start"
}
L "=== DONE ==="
