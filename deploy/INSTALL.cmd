@echo off
chcp 65001 >nul
title StandRise Installer
echo.
echo  StandRise — полная установка (RPC + MongoDB + Photon)
echo  Документация: CLAUDE.md
echo.
net session >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Запустите от имени администратора
  pause
  exit /b 1
)
cd /d "%~dp0"
if not exist config.env (
  copy /Y config.env.example config.env
  echo.
  echo [INFO] Создан config.env — задайте PUBLIC_IP и MONGO_PASS
  notepad config.env
  echo После сохранения нажмите любую клавишу...
  pause >nul
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-StandRise.ps1"
if errorlevel 1 (
  echo.
  echo [ERROR] Установка не удалась. См. CLAUDE.md раздел "Устранение неполадок"
)
pause
