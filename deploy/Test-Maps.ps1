# Тесты отображения карт в истории матчей.
# Компилируют настоящий RpcServer\Api\MapNames.cs и прогоняют по нему таблицу случаев.
$ErrorActionPreference = "Stop"
$report = "C:\StandRise\deploy\test_maps_report.txt"
Start-Transcript -Path $report -Force | Out-Null

try {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
    if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
    Write-Host "=== ТЕСТЫ КАРТ В ИСТОРИИ МАТЧЕЙ ===" -ForegroundColor Cyan
    Write-Host ""

    $proj = "C:\StandRise\tests\MapNames\MapNames.Tests.csproj"
    if (-not (Test-Path $proj)) { throw "не найден $proj" }

    $out = & $dotnet run --project $proj -c Release --nologo 2>&1
    $code = $LASTEXITCODE
    $out | ForEach-Object { Write-Host $_ }

    Write-Host ""
    if ($code -eq 0) { Write-Host "РЕЗУЛЬТАТ: ОК" -ForegroundColor Green }
    else { Write-Host "РЕЗУЛЬТАТ: ЕСТЬ ПРОВАЛЕННЫЕ ТЕСТЫ (код $code)" -ForegroundColor Red }
}
catch {
    Write-Host "ОШИБКА: $_" -ForegroundColor Red
}
finally {
    Stop-Transcript | Out-Null
    Write-Host ""
    Write-Host "Отчёт: C:\StandRise\deploy\test_maps_report.txt"
    Write-Host "Нажми любую клавишу..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
