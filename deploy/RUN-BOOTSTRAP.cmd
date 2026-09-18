@echo off
chcp 65001 >nul
title StandRise Bootstrap
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
cd /d "%~dp0"
echo.
echo  StandRise: full install (.NET 7 + MongoDB + RPC + Photon)
echo  Log: %~dp0bootstrap-log.txt
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Bootstrap.ps1"
echo.
echo ==== FINISHED - see bootstrap-log.txt ====
pause
