#Requires -RunAsAdministrator
# StandRise bootstrap v3: prerequisites + mongo services + Install-StandRise.ps1 + verification.
$ErrorActionPreference = "Continue"
$ProgressPreference    = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root = $PSScriptRoot
$Log  = Join-Path $Root "bootstrap-log.txt"
function L([string]$m) {
    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $m
    Write-Host $line
    Add-Content -LiteralPath $Log -Value $line -Encoding UTF8
}
Set-Content -LiteralPath $Log -Value "=== StandRise bootstrap v3 $(Get-Date) ===" -Encoding UTF8
L "Host: $env:COMPUTERNAME  User: $env:USERNAME"

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User")
}
function Have-Net7 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    return [bool]((& dotnet --list-sdks 2>$null) | Where-Object { $_ -match '^7\.' })
}
function Find-Mongod {
    Get-ChildItem "${env:ProgramFiles}\MongoDB\Server\*\bin\mongod.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
function Get-File([string]$url, [string]$dest) {
    L "  downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
    L ("  got {0} MB" -f [math]::Round((Get-Item $dest).Length/1MB,1))
}

# ---------- 1. .NET 7 SDK ----------
L "--- .NET 7 SDK ---"
Refresh-Path
if (Have-Net7) {
    L "OK: $((& dotnet --list-sdks) -join ' | ')"
} else {
    try {
        $di = Join-Path $env:TEMP "dotnet-install.ps1"
        Get-File "https://dot.net/v1/dotnet-install.ps1" $di
        & $di -Channel 7.0 -InstallDir "C:\Program Files\dotnet" -NoPath 2>&1 | ForEach-Object { L "  $_" }
        $mp = [Environment]::GetEnvironmentVariable("Path","Machine")
        if ($mp -notlike "*C:\Program Files\dotnet*") { [Environment]::SetEnvironmentVariable("Path", "$mp;C:\Program Files\dotnet", "Machine") }
        Refresh-Path
    } catch { L "ERROR dotnet: $($_.Exception.Message)" }
    if (Have-Net7) { L "OK installed" } else { L "ERROR: .NET 7 SDK missing" }
}

# ---------- 2. MongoDB binaries ----------
L "--- MongoDB ---"
$mongod = Find-Mongod
if ($mongod) { L "OK: $mongod" } else {
    $target = "${env:ProgramFiles}\MongoDB\Server\7.0"
    foreach ($u in @(
        "https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-7.0.14.zip",
        "https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-6.0.16.zip")) {
        try {
            $zip = Join-Path $env:TEMP "mongodb.zip"
            if (Test-Path $zip) { Remove-Item $zip -Force }
            Get-File $u $zip
            $ex = Join-Path $env:TEMP "mongodb-extract"
            if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
            Expand-Archive -LiteralPath $zip -DestinationPath $ex -Force
            $sb = Get-ChildItem $ex -Recurse -Directory -Filter "bin" | Select-Object -First 1
            if (-not $sb) { continue }
            New-Item -ItemType Directory -Force -Path (Join-Path $target "bin") | Out-Null
            Copy-Item (Join-Path $sb.FullName "*") (Join-Path $target "bin") -Recurse -Force
            Remove-Item $zip,$ex -Recurse -Force -ErrorAction SilentlyContinue
            $mongod = Find-Mongod
            if ($mongod) { break }
        } catch { L "  failed: $($_.Exception.Message)" }
    }
    if ($mongod) { L "OK: $mongod" } else { L "ERROR: mongod missing"; }
}

# ---------- 3. mongosh ----------
L "--- mongosh ---"
$mongosh = $null
if ($mongod) {
    $bin = Split-Path $mongod
    $mongosh = Join-Path $bin "mongosh.exe"
    if (-not (Test-Path $mongosh)) {
        try {
            $zip = Join-Path $env:TEMP "mongosh.zip"
            Get-File "https://downloads.mongodb.com/compass/mongosh-2.3.1-win32-x64.zip" $zip
            $ex = Join-Path $env:TEMP "mongosh-extract"
            if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
            Expand-Archive -LiteralPath $zip -DestinationPath $ex -Force
            $sb = Get-ChildItem $ex -Recurse -Directory -Filter "bin" | Select-Object -First 1
            if ($sb) { Copy-Item (Join-Path $sb.FullName "*") $bin -Recurse -Force }
            Remove-Item $zip,$ex -Recurse -Force -ErrorAction SilentlyContinue
        } catch { L "  failed: $($_.Exception.Message)" }
    }
    L "mongosh present: $(Test-Path $mongosh)"
}

# ---------- 4. Mongo dirs, configs and SERVICES ----------
L "--- Mongo services ---"
$cfgDir = "C:\StandRise\deploy\mongo"
foreach ($d in @("C:\StandRise","C:\StandRise\logs","C:\StandRise\data\mongo-main","C:\StandRise\data\mongo-game",$cfgDir)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}
$specs = @(
    @{ Name="StandRiseMongoMain"; Cfg="$cfgDir\mongod-main.cfg"; Data="C:\StandRise\data\mongo-main"; Port=2077; LogFile="C:\StandRise\logs\mongod-main.log" },
    @{ Name="StandRiseMongoGame"; Cfg="$cfgDir\mongod-game.cfg"; Data="C:\StandRise\data\mongo-game"; Port=1337; LogFile="C:\StandRise\logs\mongod-game.log" }
)
foreach ($s in $specs) {
    $yaml = @"
storage:
  dbPath: $($s.Data)
systemLog:
  destination: file
  path: $($s.LogFile)
  logAppend: true
net:
  port: $($s.Port)
  bindIp: 127.0.0.1
security:
  authorization: enabled
"@
    [IO.File]::WriteAllText($s.Cfg, $yaml, (New-Object Text.UTF8Encoding($false)))
    L "config -> $($s.Cfg)"
}

if ($mongod) {
    foreach ($s in $specs) {
        $svc = Get-Service $s.Name -ErrorAction SilentlyContinue
        if (-not $svc) {
            L "creating service $($s.Name) via mongod --install ..."
            $o = & $mongod --config $s.Cfg --install --serviceName $s.Name --serviceDisplayName $s.Name 2>&1
            $o | ForEach-Object { L "   mongod: $_" }
            $svc = Get-Service $s.Name -ErrorAction SilentlyContinue
        }
        if (-not $svc) {
            L "  --install failed, trying sc.exe ..."
            $binPath = '"{0}" --config "{1}" --service' -f $mongod, $s.Cfg
            $r = & sc.exe create $s.Name binPath= $binPath start= auto DisplayName= $s.Name 2>&1
            $r | ForEach-Object { L "   sc: $_" }
            $svc = Get-Service $s.Name -ErrorAction SilentlyContinue
        }
        if ($svc) {
            if ($svc.Status -ne "Running") {
                try { Start-Service $s.Name -ErrorAction Stop; L "OK: $($s.Name) started" }
                catch {
                    L "ERROR starting $($s.Name): $($_.Exception.Message)"
                    if (Test-Path $s.LogFile) { Get-Content $s.LogFile -Tail 15 | ForEach-Object { L "   mongolog: $_" } }
                }
            } else { L "OK: $($s.Name) already running" }
        } else { L "ERROR: service $($s.Name) could not be created" }
    }
    Start-Sleep -Seconds 4
    foreach ($s in $specs) {
        $r = Test-NetConnection 127.0.0.1 -Port $s.Port -WarningAction SilentlyContinue
        L ("port {0}: {1}" -f $s.Port, $(if ($r.TcpTestSucceeded) {"LISTENING"} else {"DOWN"}))
    }
}

# ---------- 5. main installer ----------
L "--- Install-StandRise.ps1 ---"
Refresh-Path
$installer = Join-Path $Root "Install-StandRise.ps1"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer 2>&1 | ForEach-Object { L "  $_" }
L "Installer exit code: $LASTEXITCODE"

# ---------- 6. RPC startup capture if needed ----------
$rpcUp = (Test-NetConnection 127.0.0.1 -Port 2222 -WarningAction SilentlyContinue).TcpTestSucceeded
if (-not $rpcUp) {
    L "--- RPC did not listen, capturing startup output ---"
    $dll = "C:\StandRise\bin\Release\net7.0\VsCode.dll"
    if (Test-Path $dll) {
        $o = Join-Path $Root "rpc-stdout.txt"; $e = Join-Path $Root "rpc-stderr.txt"
        Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -like "*VsCode.dll*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $p = Start-Process (Get-Command dotnet).Source -ArgumentList "`"$dll`"" -WorkingDirectory (Split-Path $dll) `
             -RedirectStandardOutput $o -RedirectStandardError $e -PassThru -NoNewWindow
        Start-Sleep -Seconds 25
        if (-not $p.HasExited) { L "  process still alive after 25s" } else { L "  process EXITED, code $($p.ExitCode)" }
        if (Test-Path $o) { L "  stdout:"; Get-Content $o -Tail 60 | ForEach-Object { L "   $_" } }
        if (Test-Path $e) { L "  stderr:"; Get-Content $e -Tail 40 | ForEach-Object { L "   $_" } }
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Start-StandRise.ps1") 2>&1 | ForEach-Object { L "  restart: $_" }
        Start-Sleep -Seconds 10
    } else { L "  VsCode.dll MISSING - build failed" }
}

# ---------- 7. verification ----------
L "--- VERIFY ---"
foreach ($p in 2077,1337,2222,2224) {
    $r = Test-NetConnection 127.0.0.1 -Port $p -WarningAction SilentlyContinue
    L ("TCP {0}: {1}" -f $p, $(if ($r.TcpTestSucceeded) {"LISTENING"} else {"DOWN"}))
}
$udp = (Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in 5055,5056 } | Select-Object -ExpandProperty LocalPort -Unique) -join ","
L "UDP 5055/5056: $(if ($udp) {$udp} else {'none'})"
L "Processes: $((Get-Process dotnet,PhotonSocketServer,mongod -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName)#$($_.Id)" }) -join ', ')"
foreach ($svc in "StandRiseMongoMain","StandRiseMongoGame") {
    $s = Get-Service $svc -ErrorAction SilentlyContinue
    L "Service ${svc}: $(if ($s) {$s.Status} else {'MISSING'})"
}
$ls = "C:\StandRise\bin\Release\net7.0\local.settings.json"
if (Test-Path $ls) { L "local.settings.json:"; Get-Content $ls | ForEach-Object { L "  $_" } } else { L "local.settings.json MISSING" }
Get-ChildItem "C:\PhotonServer\deploy" -Recurse -Filter "Photon.LoadBalancing.dll.config" -ErrorAction SilentlyContinue | ForEach-Object {
    L "Photon config: $($_.FullName)"
    Select-String -LiteralPath $_.FullName -Pattern "PublicIPAddress|ServerUrl" | Select-Object -First 6 | ForEach-Object { L "   $($_.Line.Trim())" }
}
L "Plugin dll: $(Test-Path 'C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\MatchmakingPlugin.dll')"
L "VsCode.dll: $(Test-Path 'C:\StandRise\bin\Release\net7.0\VsCode.dll')"
$r = Get-ChildItem "C:\StandRise\bin\Release\net7.0\logs\*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($r) { L "RPC log ($($r.Name)) tail:"; Get-Content $r.FullName -Tail 40 | ForEach-Object { L "   $_" } } else { L "no RPC logs" }
foreach ($m in "C:\StandRise\logs\mongod-main.log","C:\StandRise\logs\mongod-game.log") {
    if (Test-Path $m) { L "$m tail:"; Get-Content $m -Tail 6 | ForEach-Object { L "   $_" } }
}
L "=== BOOTSTRAP DONE ==="
