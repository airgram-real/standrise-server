# Запуск на СТАРОМ сервере перед упаковкой архива
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "mongo-dump"),
    [string]$MainUri = "mongodb://eliseyy22:eliseyy22%21@127.0.0.1:2077/?authSource=admin&authMechanism=SCRAM-SHA-256",
    [string]$GameUri = "mongodb://eliseyy22:eliseyy22%21@127.0.0.1:1337/?authSource=admin&authMechanism=SCRAM-SHA-256"
)
$ErrorActionPreference = "Stop"
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

$mongodump = Get-Command mongodump -ErrorAction SilentlyContinue
if (-not $mongodump) {
    $mongodump = Get-ChildItem "${env:ProgramFiles}\MongoDB\Tools\*\bin\mongodump.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
}
if ($mongodump) {
    $exe = if ($mongodump.Source) { $mongodump.Source } else { $mongodump.FullName }
    Write-Host "Export Main..."
    & $exe --uri="$MainUri" --out="$OutputDir\Main"
    Write-Host "Export Inventory..."
    & $exe --uri="$GameUri" --out="$OutputDir\Inventory"
    Write-Host "[OK] BSON dump -> $OutputDir"
    return
}

Write-Host "[WARN] mongodump not found."
Write-Host "[WARN] Install MongoDB Database Tools, or stop mongod and copy data manually."
Write-Host "[WARN] Archive will continue WITHOUT mongo data."
exit 0
