# Сборка полного архива для переноса на WS2022 (ipssystem)
param(
    [string]$RepoRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputZip = "",
    [switch]$IncludeMongoDump,
    [switch]$IncludePhotonSdk,
    [switch]$IncludeClientFiles
)

$ErrorActionPreference = "Stop"
$date = Get-Date -Format "yyyyMMdd-HHmm"
if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip = Join-Path ([Environment]::GetFolderPath("Desktop")) "StandRise-FULL-$date.zip"
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $fb = "C:\Users\Administrator\dotnet-sdk\dotnet.exe"
    if (Test-Path $fb) { $dotnet = $fb } else { throw "dotnet not found" }
}

Write-Host "[1/6] Build Release..."
Push-Location $RepoRoot
& $dotnet build (Join-Path $RepoRoot "VsCode.csproj") -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "[1b/6] Build MatchmakingPlugin..."
$hive = Join-Path $RepoRoot "PhotonPlugin\PhotonHivePlugin.dll"
if (Test-Path $hive) {
    Push-Location (Join-Path $RepoRoot "PhotonPlugin")
    & $dotnet build MatchmakingPlugin.csproj -c Release --nologo
    Pop-Location
    $built = Join-Path $RepoRoot "PhotonPlugin\bin\Release\net472\MatchmakingPlugin.dll"
    if (Test-Path $built) {
        New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot "deploy\built") | Out-Null
        Copy-Item $built (Join-Path $RepoRoot "deploy\built\MatchmakingPlugin.dll") -Force
        Write-Host "  Plugin -> deploy\built\MatchmakingPlugin.dll"
    }
}
Pop-Location

if ($IncludeMongoDump) {
    Write-Host "[2/6] MongoDB dump..."
    & (Join-Path $PSScriptRoot "Export-MongoDump.ps1")
} else {
    Write-Host "[2/6] Mongo dump skipped (use -IncludeMongoDump)"
}

$staging = Join-Path $env:TEMP "StandRise-FULL-$date"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

Write-Host "[3/6] Copy project..."
$excludeDirs = @('obj', '.git', '_build_verify', '_decompiled_mm', '.aider.tags.cache.v4', '.vscode', 'scratch', 'win-x64', 'runtimes', 'mongo-data-raw')
$excludeFiles = @('traffic_hex.log', '*.pdb', '*.bak', 'dump*.txt', 'il2cpp*.txt', 'data_*.txt', 'meta_*.txt', 'hexdump.txt', 'server_out.log', 'server_stdout.txt', 'bot_log.txt', 'mod_actions_log.jsonl', 'allies_matches_raw.jsonl', 'build_err*.txt')

function ShouldSkipFile([string]$name) {
    foreach ($p in $excludeFiles) { if ($name -like $p) { return $true } }
    return $false
}

function Copy-Tree([string]$src, [string]$dst) {
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    Get-ChildItem -LiteralPath $src -Force | ForEach-Object {
        if ($_.PSIsContainer) {
            if ($excludeDirs -contains $_.Name) { return }
            if ($_.Name -eq 'bin') {
                Copy-BinRelease $_.FullName (Join-Path $dst 'bin')
                return
            }
            if ($_.Name -eq 'logs') { return }
            Copy-Tree $_.FullName (Join-Path $dst $_.Name)
        } elseif (-not (ShouldSkipFile $_.Name)) {
            if (-not $IncludeClientFiles -and ($_.Name -like '*.obb' -or $_.Name -like '*.apk')) { return }
            Copy-Item $_.FullName (Join-Path $dst $_.Name) -Force
        }
    }
}

function Copy-BinRelease([string]$binRoot, [string]$dstBin) {
    $release = Join-Path $binRoot 'Release\net7.0'
    if (-not (Test-Path $release)) { return }
    $out = Join-Path $dstBin 'Release\net7.0'
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Get-ChildItem $release -Force | ForEach-Object {
        if ($_.PSIsContainer) {
            if ($_.Name -eq 'logs' -or $excludeDirs -contains $_.Name) { return }
            Copy-Item $_.FullName (Join-Path $out $_.Name) -Recurse -Force
        } elseif (-not (ShouldSkipFile $_.Name)) {
            Copy-Item $_.FullName (Join-Path $out $_.Name) -Force
        }
    }
}

Copy-Tree $RepoRoot $staging

if ($IncludePhotonSdk) {
    Write-Host "[4/6] Bundle Photon SDK..."
    $photonSources = @(
        "C:\Users\Administrator\Desktop\standoff-server-84.21.173.237\Photon-OnPremise-Server-SDK_v4-0-29-11263",
        "C:\PhotonServer"
    )
    $found = $null
    foreach ($s in $photonSources) {
        if (Test-Path (Join-Path $s "deploy\bin_Win64\PhotonSocketServer.exe")) { $found = $s; break }
    }
    if ($found) {
        $dest = Join-Path $staging "third-party\Photon-SDK"
        robocopy $found $dest /E /XD log /XF *.log plugin_debug.log /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
        Write-Host "  Photon SDK from $found"
    } else {
        Write-Host "[WARN] Photon SDK not found - archive without SDK" -ForegroundColor Yellow
    }
} else {
    Write-Host "[4/6] Photon SDK skipped (use -IncludePhotonSdk)"
}

Write-Host "[5/6] ZIP -> $OutputZip"
if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $OutputZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $staging -Recurse -Force

$sizeMb = [math]::Round((Get-Item $OutputZip).Length / 1048576, 1)
Write-Host "[6/6] Done: $OutputZip ($sizeMb megabytes)"
Write-Host ""
Write-Host "Next on WS2022:"
Write-Host "  1. Expand-Archive to C:\StandRise"
Write-Host "  2. Run deploy\INSTALL.cmd as Administrator"
Write-Host "  3. Follow CLAUDE.md"
