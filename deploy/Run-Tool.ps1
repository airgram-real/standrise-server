# Thin wrappers → tools/py/run.py
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgsRest
)
$ErrorActionPreference = "Stop"
$py = if (Test-Path "C:\Program Files\Python312\python.exe") { "C:\Program Files\Python312\python.exe" } else { "python" }
$cmd = Split-Path -Leaf $MyInvocation.MyCommand.Path
$cmd = $cmd -replace '\.ps1$','' -replace '^Run-',''
& $py "C:\StandRise\tools\py\run.py" $cmd @ArgsRest
exit $LASTEXITCODE
