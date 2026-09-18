@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\StandRise\Web\Install-WebDeps.ps1"
if errorlevel 1 pause
