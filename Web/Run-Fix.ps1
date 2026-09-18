# Добивает то, что не доехало в прошлый заход:
#   1) распаковывает архив с фоном и логотипом
#   2) собирает фронтенд (в прошлый раз npm не нашёл node)
#   3) переводит запуск со служб на планировщик (обычное консольное
#      приложение службой быть не умеет — оно и не стартовало)

$log = "C:\StandRise\Web\runfix.log"
$done = "C:\StandRise\Web\runfix.done"
Remove-Item $done -ErrorAction SilentlyContinue
Set-Content -Path $log -Value "==== RUN FIX $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ====" -Encoding utf8
function Log($t) {
    $line = "$(Get-Date -Format 'HH:mm:ss')  $t"
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding utf8
}

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Log "Администратор: $isAdmin"

# ---------------- 0. Игровой сервер ----------------
Log ""
Log "=== ШАГ 0: пересборка игрового сервера ==="
# В прошлый заход сборка падала (я вырезал реестр карт вместе с блоком кода),
# и сервер остался на старой dll — поэтому история матчей и не менялась.
try {
    $out0 = & powershell -NoProfile -ExecutionPolicy Bypass -File "C:\StandRise\deploy\Build-Restart.ps1" 2>&1
    $out0 | Select-Object -Last 40 | ForEach-Object { Log "  $_" }
} catch { Log "ОШИБКА сборки сервера: $_" }

$report = "C:\StandRise\deploy\build_report.txt"
if (Test-Path $report) {
    $txt = Get-Content $report -Raw
    if ($txt -match "build succeeded") { Log "игровой сервер: СОБРАЛСЯ" }
    elseif ($txt -match "build failed")  { Log "игровой сервер: СБОРКА УПАЛА (см. build_report.txt)" }
}

# ---------------- 1. Архив с фоном ----------------
Log ""
Log "=== ШАГ 1: фон и логотип ==="
$sevenZip = @("C:\Program Files\7-Zip\7z.exe", "C:\Program Files (x86)\7-Zip\7z.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
$bgSrc = "C:\Users\Administrator\Downloads\cursedsouls_background.7z"
$bgDst = "C:\Users\Administrator\Downloads\cursedsouls_background"
if (-not $sevenZip) { Log "ОШИБКА: 7-Zip не найден" }
elseif (-not (Test-Path $bgSrc)) { Log "ОШИБКА: нет $bgSrc" }
else {
    if (Test-Path $bgDst) { Remove-Item $bgDst -Recurse -Force }
    New-Item -ItemType Directory -Path $bgDst -Force | Out-Null
    & $sevenZip x $bgSrc "-o$bgDst" -y | Out-Null
    $files = Get-ChildItem $bgDst -Recurse -File
    Log "распаковано файлов: $($files.Count)"
    foreach ($f in $files) {
        Log ("  {0}  ({1:N0} КБ)" -f $f.FullName.Substring($bgDst.Length + 1), ($f.Length / 1KB))
    }
}

# ---------------- 2. Сборка сайта ----------------
Log ""
Log "=== ШАГ 2: сборка сайта ==="
$nodeDir = (Get-ChildItem "C:\StandRise\Web\node" -Filter "node.exe" -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1).DirectoryName
if (-not $nodeDir) { Log "ОШИБКА: node.exe не найден" }
else {
    Log "node: $nodeDir"
    # Вот из-за чего упало в прошлый раз: npm.cmd внутри вызывает просто `node`,
    # а его каталога не было в PATH процесса.
    $env:Path = "$nodeDir;$env:Path"
    $src = "C:\Users\Administrator\Downloads\ProjectRework\ProjectRework"
    Push-Location $src
    $out = & cmd /c "`"$nodeDir\npm.cmd`" run build 2>&1"
    $rc = $LASTEXITCODE
    Pop-Location
    $out | ForEach-Object { Log "  $_" }
    Log "сборка сайта exit=$rc"

    if (Test-Path "$src\dist\index.html") {
        $dst = "C:\StandRise\Web\wwwroot"
        if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        Copy-Item "$src\dist\*" $dst -Recurse -Force
        Log "в wwwroot файлов: $((Get-ChildItem $dst -Recurse -File).Count)"
        $idx = Get-Content "$dst\index.html" -Raw
        Log ("telegram-web-app.js в index.html: {0}" -f ($idx -match 'telegram-web-app'))
    } else { Log "ОШИБКА: dist не собрался" }
}

# ---------------- 3. Автозапуск ----------------
Log ""
Log "=== ШАГ 3: автозапуск ==="
if (-not $isAdmin) {
    Log "Без прав администратора: порты и автозапуск пропускаю, но сайт подниму обычными процессами."
    $dotnetNA = "C:\Program Files\dotnet\dotnet.exe"
    $siteDllNA = "C:\StandRise\Web\bin\Release\net7.0\ProjectRework.Web.dll"
    Get-Process dotnet -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $dotnetNA -and $_.MainWindowTitle -eq "" } | Out-Null
    if (Test-Path $siteDllNA) {
        Start-Process -FilePath $dotnetNA -ArgumentList "`"$siteDllNA`"" `
            -WorkingDirectory "C:\StandRise\Web\bin\Release\net7.0" -WindowStyle Hidden
        Log "сайт запущен процессом (без службы)"
    } else { Log "нет $siteDllNA" }
    $caddyNA = "C:\StandRise\Web\caddy\caddy.exe"
    if (Test-Path $caddyNA) {
        Start-Process -FilePath $caddyNA -ArgumentList "run","--config","`"C:\StandRise\Web\Caddyfile`"" `
            -WorkingDirectory "C:\StandRise\Web\caddy" -WindowStyle Hidden
        Log "caddy запущен процессом (без службы)"
    }
}
else {
    # Службы из прошлого захода не стартовали: sc.exe умеет запускать только
    # настоящие сервисы, а сайт и caddy — обычные консольные программы.
    foreach ($svc in "ProjectReworkWeb", "ProjectReworkCaddy") {
        if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
            sc.exe stop $svc | Out-Null
            Start-Sleep -Seconds 1
            sc.exe delete $svc | Out-Null
            Log "удалена нерабочая служба $svc"
        }
    }
    Get-Process caddy -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    function Ensure-Task($name, $exe, $cmdArgs, $workdir) {
        try {
            Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
            $act = New-ScheduledTaskAction -Execute $exe -Argument $cmdArgs -WorkingDirectory $workdir
            $trg = New-ScheduledTaskTrigger -AtStartup
            $set = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                   -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
                   -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
            $prc = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
            Register-ScheduledTask -TaskName $name -Action $act -Trigger $trg -Settings $set -Principal $prc -Force | Out-Null
            Start-ScheduledTask -TaskName $name
            Start-Sleep -Seconds 4
            $info = Get-ScheduledTaskInfo -TaskName $name
            Log "$name : LastResult=$($info.LastTaskResult)"
        } catch { Log "ОШИБКА задачи ${name}: $_" }
    }

    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
    $siteDll = "C:\StandRise\Web\bin\Release\net7.0\ProjectRework.Web.dll"
    if (Test-Path $siteDll) {
        Ensure-Task "ProjectReworkWeb" $dotnet "`"$siteDll`"" "C:\StandRise\Web\bin\Release\net7.0"
    } else { Log "ПРОПУЩЕНО: нет $siteDll" }

    $caddyExe = "C:\StandRise\Web\caddy\caddy.exe"
    if (Test-Path $caddyExe) {
        Ensure-Task "ProjectReworkCaddy" $caddyExe "run --config `"C:\StandRise\Web\Caddyfile`"" "C:\StandRise\Web\caddy"
    } else { Log "ПРОПУЩЕНО: нет caddy.exe" }
}

# ---------------- 4. Проверка ----------------
Log ""
Log "=== ШАГ 4: проверка ==="
Start-Sleep -Seconds 6
foreach ($p in 8080, 80, 443) {
    $listening = (Get-NetTCPConnection -State Listen -LocalPort $p -ErrorAction SilentlyContinue) -ne $null
    Log "порт ${p}: $(if ($listening) { 'слушает' } else { 'молчит' })"
}
try {
    $h = Invoke-RestMethod -Uri "http://127.0.0.1:8080/api/health" -TimeoutSec 20
    Log "health: ok=$($h.ok) botToken=$($h.botToken) site=$($h.site) links=$($h.links)"
} catch { Log "health не ответил: $($_.Exception.Message)" }
try {
    $c = Invoke-RestMethod -Uri "http://127.0.0.1:8080/api/catalog" -TimeoutSec 20
    Log "catalog: голды=$($c.gold.Count) пропуск=$($c.pass.Count) первая цена=$($c.gold[0].stars)"
} catch { Log "catalog не ответил: $($_.Exception.Message)" }

Log ""
Log "=== DNS ==="
try {
    $ips = (Resolve-DnsName projectre.work -Type A -ErrorAction Stop | Where-Object { $_.IPAddress }).IPAddress
    Log "projectre.work -> $($ips -join ', ')"
    if ($ips -match "^(104\.21\.|172\.6[4-9]\.|172\.7[0-1]\.|188\.114\.|162\.159\.)") {
        Log "Это адреса Cloudflare: домен идёт через прокси (оранжевое облако)."
        Log "Пока он включён, Let's Encrypt не сможет проверить домен и сертификата не будет."
        Log "В панели Cloudflare переключи запись projectre.work в DNS only (серое облако)."
    }
} catch { Log "DNS не проверился: $_" }

Log ""
Log "==== ВСЁ ===="
Set-Content -Path $done -Value "done" -Encoding utf8
