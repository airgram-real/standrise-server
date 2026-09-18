# Открывает 80/443, ставит сайт и Caddy службами и запускает их.
# Требует запуска от администратора.

$log = "C:\StandRise\Web\setup_domain.txt"
$lines = New-Object System.Collections.Generic.List[string]
function Say($t, $c = "Gray") { Write-Host $t -ForegroundColor $c; $lines.Add($t) }
function Flush() { try { $lines | Out-File -FilePath $log -Encoding utf8 } catch {} }

Say "==== НАСТРОЙКА ДОМЕНА projectre.work ====" "Cyan"
Say (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Say ""

$admin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Say "Запусти этот файл от имени администратора — иначе нельзя ни открыть порты, ни создать службы." "Red"
    Flush
    Write-Host "Нажми любую клавишу..."; try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch {}
    exit 1
}

$caddy = "C:\StandRise\Web\caddy\caddy.exe"
if (-not (Test-Path $caddy)) {
    Say "Нет $caddy — сначала запусти INSTALL-DEPS.cmd" "Red"
    Flush; Write-Host "Нажми любую клавишу..."; try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch {}
    exit 1
}

# ---- Порты ----
Say "--- Брандмауэр ---" "Cyan"
foreach ($p in 80, 443) {
    $name = "ProjectRework HTTP $p"
    $existing = Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue
    if ($existing) { Say "Правило уже есть: $name" }
    else {
        New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow `
            -Protocol TCP -LocalPort $p -Profile Any | Out-Null
        Say "Открыт порт $p" "Green"
    }
}
Say ""

# ---- Проверка, что домен указывает сюда ----
Say "--- DNS ---" "Cyan"
try {
    $ips = (Resolve-DnsName projectre.work -Type A -ErrorAction Stop | Where-Object { $_.IPAddress }).IPAddress
    Say "projectre.work -> $($ips -join ', ')"
    $mine = (Invoke-RestMethod -Uri "https://api.ipify.org?format=json" -TimeoutSec 30).ip
    Say "Внешний адрес сервера: $mine"
    if ($ips -contains $mine) { Say "Совпадает — сертификат выпустится" "Green" }
    else { Say "НЕ совпадает. Пока A-запись не укажет на $mine, Let's Encrypt сертификат не выдаст." "Red" }
} catch { Say "Не смог проверить DNS: $_" "Yellow" }
Say ""

# ---- Службы ----
Say "--- Службы ---" "Cyan"

function Ensure-Service($name, $binPath, $display) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc) {
        Say "Служба $name уже есть — пересоздаю с новыми параметрами"
        sc.exe stop $name | Out-Null
        Start-Sleep -Seconds 2
        sc.exe delete $name | Out-Null
        Start-Sleep -Seconds 2
    }
    sc.exe create $name binPath= $binPath start= auto DisplayName= $display | Out-Null
    sc.exe failure $name reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
    Start-Service -Name $name -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    $s = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($s -and $s.Status -eq "Running") { Say "$name запущена" "Green" }
    else { Say "$name НЕ запустилась (статус: $($s.Status))" "Red" }
}

$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$siteDll = "C:\StandRise\Web\bin\Release\net7.0\ProjectRework.Web.dll"
if (-not (Test-Path $siteDll)) {
    Say "Нет $siteDll — сначала собери сайт (BUILD-WEB.cmd)" "Red"
} else {
    Ensure-Service "ProjectReworkWeb" "`"$dotnet`" `"$siteDll`"" "Project Rework Web"
}

Ensure-Service "ProjectReworkCaddy" "`"$caddy`" run --config `"C:\StandRise\Web\Caddyfile`"" "Project Rework Caddy"

Say ""
Say "==== ГОТОВО ====" "Cyan"
Say "Проверь: https://projectre.work"
Say "Первый сертификат выпускается 10-60 секунд после старта Caddy."
Flush
Write-Host ""
Write-Host "Лог: $log"
Write-Host "Нажми любую клавишу..."
try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { Start-Sleep -Seconds 20 }
