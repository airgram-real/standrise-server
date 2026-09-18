# StandRise tools — Python
# Match history for APK 0.17 (MatchId MUST be 32-char hex).
#
#   .\MatchHistory.ps1 show
#   .\MatchHistory.ps1 show TEST_01
#   .\MatchHistory.ps1 seed TEST_01
#   .\MatchHistory.ps1 check-ids

param(
    [Parameter(Position = 0)]
    [ValidateSet("show", "seed", "check-ids")]
    [string]$Command = "show",

    [Parameter(Position = 1)]
    [string]$Who = "TEST_01"
)

$ErrorActionPreference = "Stop"
$py = "C:\Program Files\Python312\python.exe"
if (-not (Test-Path $py)) { $py = "python" }
$run = "C:\StandRise\tools\py\run.py"
Write-Host ">>> $Command $Who" -ForegroundColor Cyan
& $py $run match-history $Command $Who
exit $LASTEXITCODE
