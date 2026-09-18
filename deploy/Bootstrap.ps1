#Requires -RunAsAdministrator
# StandRise bootstrap v2: prerequisites + Install-StandRise.ps1 + verification.
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
Set-Content -LiteralPath $Log -Value "=== StandRise bootstrap v2 $(Get-Date) ===" -Encoding UTF8

$osv = Get-CimInstance Win32_OperatingSystem
L "Host: $env:COMPUTERNAME  User: $env:USERNAME"
L "OS: $($osv.Caption) $($osv.Version)"
L "Root: $Root"

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
        if ($mp -notlike "*C:\Program Files\dotnet*") {
            [Environment]::SetEnvironmentVariable("Path", "$mp;C:\Program Files\dotnet", "Machine")
        }
        Refresh-Path
    } catch { L "ERROR dotnet: $($_.Exception.Message)" }
    if (Have-Net7) { L "OK: .NET 7 SDK installed" } else { L "ERROR: .NET 7 SDK missing" }
}

# ---------- 2. MongoDB (portable ZIP -> Program Files\MongoDB\Server\7.0) ----------
L "--- MongoDB ---"
$mongod = Find-Mongod
if ($mongod) {
    L "OK: already present: $mongod"
} else {
    $target = "${env:ProgramFiles}\MongoDB\Server\7.0"
    $urls = @(
        "https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-7.0.14.zip",
        "https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-7.0.5.zip",
        "https://fastdl.mongodb.org/windows/mongodb-windows-x86_64-6.0.16.zip"
    )
    foreach ($u in $urls) {
        try {
            $zip = Join-Path $env:TEMP "mongodb.zip"
            if (Test-Path $zip) { Remove-Item $zip -Force }
            Get-File $u $zip
            $ex = Join-Path $env:TEMP "mongodb-extract"
            if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
            Expand-Archive -LiteralPath $zip -DestinationPath $ex -Force
            $srcBin = Get-ChildItem $ex -Recurse -Directory -Filter "bin" | Select-Object -First 1
            if (-not $srcBin) { L "  no bin/ in archive"; continue }
            New-Item -ItemType Directory -Force -Path (Join-Path $target "bin") | Out-Null
            Copy-Item (Join-Path $srcBin.FullName "*") (Join-Path $target "bin") -Recurse -Force
            Remove-Item $zip -Force -ErrorAction SilentlyContinue
            Remove-Item $ex -Recurse -Force -ErrorAction SilentlyContinue
            $mongod = Find-Mongod
            if ($mongod) { break }
        } catch { L "  failed: $($_.Exception.Message)" }
    }
    if ($mongod) { L "OK: mongod -> $mongod" } else { L "ERROR: mongod NOT installed" }
}

# ---------- 3. mongosh (required to create the DB user) ----------
L "--- mongosh ---"
if ($mongod) {
    $bin    = Split-Path $mongod
    $shPath = Join-Path $bin "mongosh.exe"
    if (Test-Path $shPath) {
        L "OK: already present"
    } else {
        foreach ($u in @(
            "https://downloads.mongodb.com/compass/mongosh-2.3.1-win32-x64.zip",
            "https://downloads.mongodb.com/compass/mongosh-2.2.15-win32-x64.zip"
        )) {
            try {
                $zip = Join-Path $env:TEMP "mongosh.zip"
                if (Test-Path $zip) { Remove-Item $zip -Force }
                Get-File $u $zip
                $ex = Join-Path $env:TEMP "mongosh-extract"
                if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
                Expand-Archive -LiteralPath $zip -DestinationPath $ex -Force
                $sb = Get-ChildItem $ex -Recurse -Directory -Filter "bin" | Select-Object -First 1
                if ($sb) { Copy-Item (Join-Path $sb.FullName "*") $bin -Recurse -Force }
                Remove-Item $zip -Force -ErrorAction SilentlyContinue
                Remove-Item $ex -Recurse -Force -ErrorAction SilentlyContinue
                if (Test-Path $shPath) { break }
            } catch { L "  failed: $($_.Exception.Message)" }
        }
        if (Test-Path $shPath) { L "OK: mongosh -> $shPath" } else { L "WARN: mongosh missing (DB user will not be created)" }
    }
}

# ---------- 4. main installer ----------
L "--- Install-StandRise.ps1 ---"
Refresh-Path
$installer = Join-Path $Root "Install-StandRise.ps1"
if (-not (Test-Path $installer)) { L "FATAL: installer missing"; exit 1 }
$enc = [byte[]](Get-Content -LiteralPath $installer -Encoding Byte -TotalCount 3)
L ("installer first bytes: {0:X2} {1:X2} {2:X2} (EF BB BF = UTF-8 BOM, good)" -f $enc[0],$enc[1],$enc[2])
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer 2>&1 | ForEach-Object { L "  $_" }
L "Installer exit code: $LASTEXITCODE"

# ---------- 5. verification ----------
L "--- VERIFY ---"
Start-Sleep -Seconds 8
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
$r = Get-ChildItem "C:\StandRise\bin\Release\net7.0\logs\*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($r) { L "RPC log ($($r.Name)) tail:"; Get-Content $r.FullName -Tail 40 | ForEach-Object { L "   $_" } } else { L "no RPC logs" }
foreach ($m in "C:\StandRise\logs\mongod-main.log","C:\StandRise\logs\mongod-game.log") {
    if (Test-Path $m) { L "$m tail:"; Get-Content $m -Tail 8 | ForEach-Object { L "   $_" } }
}
L "=== BOOTSTRAP DONE ==="
