#Requires -RunAsAdministrator
# StandRise bootstrap v5
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
Set-Content -LiteralPath $Log -Value "=== StandRise bootstrap v5 $(Get-Date) ===" -Encoding UTF8
L "Host: $env:COMPUTERNAME  User: $env:USERNAME"

function Refresh-Path { $env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User") }
function Find-Mongod {
    Get-ChildItem "${env:ProgramFiles}\MongoDB\Server\*\bin\mongod.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
Refresh-Path

# ---------- 1. prerequisites (already installed by v3) ----------
L "--- prerequisites ---"
L "dotnet SDKs: $((& dotnet --list-sdks 2>$null) -join ' | ')"
$mongod = Find-Mongod
L "mongod: $mongod"
if (-not $mongod) { L "FATAL: mongod missing"; exit 1 }
$mongoBin = Split-Path $mongod
$mongosh  = Join-Path $mongoBin "mongosh.exe"
L "mongosh present: $(Test-Path $mongosh)"

# --- Visual C++ runtime: the MongoDB ZIP build does not bundle it, mongod.exe dies instantly without it
L "--- VC++ redistributable ---"
try {
    $vc = Join-Path $env:TEMP "vc_redist.x64.exe"
    if (-not (Test-Path $vc)) {
        Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/vc_redist.x64.exe" -OutFile $vc -UseBasicParsing
        L "  downloaded ($([math]::Round((Get-Item $vc).Length/1MB,1)) MB)"
    } else { L "  already downloaded" }
    $pvc = Start-Process $vc -ArgumentList "/install","/quiet","/norestart" -Wait -PassThru
    L "  vc_redist exit code: $($pvc.ExitCode)   (0 = installed, 1638/5100 = newer already present, 3010 = reboot pending)"
} catch { L "  vc_redist failed: $($_.Exception.Message)" }

# --- can mongod.exe even start?
$vf = Join-Path $env:TEMP "mongod-ver.txt"
cmd /c "`"$mongod`" --version > `"$vf`" 2>&1"
L "mongod --version exit code: $LASTEXITCODE"
if (Test-Path $vf) { Get-Content $vf | Select-Object -First 6 | ForEach-Object { L "  $_" } } else { L "  no output at all" }

# ---------- 2. dirs + configs ----------
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
}
L "mongo configs written"

# ---------- 3. start MongoDB: service first, process as fallback ----------
L "--- MongoDB startup ---"
foreach ($s in $specs) {
    $up = (Test-NetConnection 127.0.0.1 -Port $s.Port -WarningAction SilentlyContinue).TcpTestSucceeded
    if ($up) { L "$($s.Name): port $($s.Port) already LISTENING"; continue }

    # -- attempt A: real Windows service
    $svc = Get-Service $s.Name -ErrorAction SilentlyContinue
    if (-not $svc) {
        $bp = '"{0}" --config "{1}" --service' -f $mongod, $s.Cfg
        L "$($s.Name): New-Service with $bp"
        try { New-Service -Name $s.Name -BinaryPathName $bp -DisplayName $s.Name -StartupType Automatic -ErrorAction Stop | Out-Null; L "  service created" }
        catch { L "  New-Service failed: $($_.Exception.Message)" }
        $svc = Get-Service $s.Name -ErrorAction SilentlyContinue
    }
    if ($svc) {
        try { Start-Service $s.Name -ErrorAction Stop; L "  service started" }
        catch { L "  service start failed: $($_.Exception.Message)" }
        Start-Sleep -Seconds 5
        $up = (Test-NetConnection 127.0.0.1 -Port $s.Port -WarningAction SilentlyContinue).TcpTestSucceeded
        L "  port $($s.Port): $(if($up){'LISTENING'}else{'DOWN'})"
    }

    # -- attempt B: plain background process
    if (-not $up) {
        if ($svc) {
            L "  removing non-working service"
            Stop-Service $s.Name -Force -ErrorAction SilentlyContinue
            & sc.exe delete $s.Name 2>&1 | ForEach-Object { L "   sc delete: $_" }
        }
        L "  starting mongod as a background process"
        Start-Process -FilePath $mongod -ArgumentList @("--config", $s.Cfg) -WindowStyle Hidden
        Start-Sleep -Seconds 6
        $up = (Test-NetConnection 127.0.0.1 -Port $s.Port -WarningAction SilentlyContinue).TcpTestSucceeded
        L "  port $($s.Port): $(if($up){'LISTENING'}else{'DOWN'})"
        if ($up) {
            try {
                Unregister-ScheduledTask -TaskName $s.Name -Confirm:$false -ErrorAction SilentlyContinue
                $a = New-ScheduledTaskAction -Execute $mongod -Argument ('--config "{0}"' -f $s.Cfg) -WorkingDirectory $mongoBin
                $t = New-ScheduledTaskTrigger -AtStartup
                Register-ScheduledTask -TaskName $s.Name -Action $a -Trigger $t -RunLevel Highest -User "SYSTEM" -Force | Out-Null
                L "  autostart task '$($s.Name)' registered"
            } catch { L "  task registration failed: $($_.Exception.Message)" }
        }
    }
    if (-not $up -and (Test-Path $s.LogFile)) {
        L "  mongod log tail:"
        Get-Content $s.LogFile -Tail 20 | ForEach-Object { L "   $_" }
    }
}

# ---------- 4. DB user ----------
L "--- Mongo user ---"
if (Test-Path $mongosh) {
    foreach ($s in $specs) {
        $js = "db = db.getSiblingDB('admin'); try { db.createUser({ user: 'eliseyy22', pwd: 'eliseyy22!', roles: [{role:'root', db:'admin'}] }); print('CREATED'); } catch(e) { print('ERR ' + e.codeName + ' ' + e.message); }"
        $tmp = Join-Path $env:TEMP ("mku_{0}.js" -f $s.Port)
        [IO.File]::WriteAllText($tmp, $js, (New-Object Text.UTF8Encoding($false)))
        $o = & $mongosh "mongodb://127.0.0.1:$($s.Port)/admin" --quiet --file $tmp 2>&1
        $o | ForEach-Object { L "  :$($s.Port) $_" }
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        $chk = Join-Path $env:TEMP ("mck_{0}.js" -f $s.Port)
        [IO.File]::WriteAllText($chk, "print('AUTH_OK ' + db.getSiblingDB('admin').runCommand({connectionStatus:1}).authInfo.authenticatedUsers.length);", (New-Object Text.UTF8Encoding($false)))
        $o2 = & $mongosh "mongodb://eliseyy22:eliseyy22%21@127.0.0.1:$($s.Port)/admin?authSource=admin" --quiet --file $chk 2>&1
        $o2 | ForEach-Object { L "  :$($s.Port) auth-test $_" }
        Remove-Item $chk -Force -ErrorAction SilentlyContinue
    }
} else { L "mongosh missing - cannot create user" }

# ---------- 5. installer ----------
L "--- Install-StandRise.ps1 ---"
Refresh-Path
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Install-StandRise.ps1") 2>&1 | ForEach-Object { L "  $_" }
L "Installer exit code: $LASTEXITCODE"

# ---------- 6. RPC diagnostics ----------
try {
    $rpcUp = (Test-NetConnection 127.0.0.1 -Port 2222 -WarningAction SilentlyContinue).TcpTestSucceeded
    if (-not $rpcUp) {
        L "--- RPC not listening: capturing startup output ---"
        $dll = "C:\StandRise\bin\Release\net7.0\VsCode.dll"
        if (Test-Path $dll) {
            $o = Join-Path $Root "rpc-stdout.txt"; $e = Join-Path $Root "rpc-stderr.txt"
            Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
                Where-Object { $_.CommandLine -like "*VsCode.dll*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            $p = Start-Process (Get-Command dotnet).Source -ArgumentList "`"$dll`"" -WorkingDirectory (Split-Path $dll) -RedirectStandardOutput $o -RedirectStandardError $e -PassThru -NoNewWindow
            Start-Sleep -Seconds 25
            L "  exited: $($p.HasExited)"
            if (Test-Path $o) { L "  stdout:"; Get-Content $o -Tail 60 | ForEach-Object { L "   $_" } }
            if (Test-Path $e) { L "  stderr:"; Get-Content $e -Tail 40 | ForEach-Object { L "   $_" } }
            if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root "Start-StandRise.ps1") 2>&1 | ForEach-Object { L "  restart: $_" }
            Start-Sleep -Seconds 12
        } else { L "  VsCode.dll MISSING - build did not produce it" }
    }
} catch { L "RPC diag exception: $($_.Exception.Message)" }

# ---------- 7. verify ----------
L "--- VERIFY ---"
foreach ($p in 2077,1337,2222,2224) {
    $r = Test-NetConnection 127.0.0.1 -Port $p -WarningAction SilentlyContinue
    L ("TCP {0}: {1}" -f $p, $(if ($r.TcpTestSucceeded) {"LISTENING"} else {"DOWN"}))
}
$udp = (Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in 5055,5056 } | Select-Object -ExpandProperty LocalPort -Unique) -join ","
L "UDP 5055/5056: $(if ($udp) {$udp} else {'none'})"
L "Processes: $((Get-Process dotnet,PhotonSocketServer,mongod -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName)#$($_.Id)" }) -join ', ')"
L "VsCode.dll: $(Test-Path 'C:\StandRise\bin\Release\net7.0\VsCode.dll')"
L "Plugin dll: $(Test-Path 'C:\PhotonServer\deploy\Plugins\MatchmakingPlugin\bin\MatchmakingPlugin.dll')"
$ls = "C:\StandRise\bin\Release\net7.0\local.settings.json"
if (Test-Path $ls) { L "local.settings.json:"; Get-Content $ls | ForEach-Object { L "  $_" } } else { L "local.settings.json MISSING" }
L "=== BOOTSTRAP DONE ==="
