@echo off
chcp 65001 >nul
net session >nul 2>&1
if %errorlevel%==0 goto elevated
echo Zapros prav administratora...
powershell -NoProfile -Command "Start-Process '%~f0' -Verb RunAs"
exit /b

:elevated
echo Ustanovka sajta projectre.work. Zhdi, eto neskolko minut.
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\StandRise\Web\Run-All.ps1" > "C:\StandRise\Web\runall.console.txt" 2>&1
echo.
type "C:\StandRise\Web\runall.console.txt"
echo.
echo ---- gotovo ----
pause
