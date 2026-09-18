@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0RestartPy.ps1"
echo.
echo ===== RESTARTPY FINISHED =====
pause
