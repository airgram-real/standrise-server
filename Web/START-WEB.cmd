@echo off
setlocal enableextensions
chcp 65001 >nul
title ProjectRework Web
set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

echo Starting site on http://127.0.0.1:8080
echo Close this window to stop it.
echo.
"%DOTNET%" run --project "C:\StandRise\Web\ProjectRework.Web.csproj" -c Release --no-launch-profile
echo.
echo Site stopped.
pause
