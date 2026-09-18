@echo off
chcp 65001 >nul
title StandRise Install 5
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
cd /d "%~dp0"
echo Installing StandRise... log: %~dp0bootstrap-log.txt
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Bootstrap5.ps1"
exit
