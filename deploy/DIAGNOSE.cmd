@echo off
chcp 65001 >nul
title StandRise Diagnose
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Diagnose.ps1"
echo.
echo ==== see diagnose-log.txt ====
pause
