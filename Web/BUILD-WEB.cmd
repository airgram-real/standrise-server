@echo off
setlocal enableextensions
chcp 65001 >nul
set "LOG=C:\StandRise\Web\build_web.txt"
echo ==== BUILD WEB ==== > "%LOG%"
echo %DATE% %TIME% >> "%LOG%"

set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

echo. >> "%LOG%"
"%DOTNET%" build "C:\StandRise\Web\ProjectRework.Web.csproj" -c Release --nologo >> "%LOG%" 2>&1
echo exitcode=%ERRORLEVEL% >> "%LOG%"

type "%LOG%"
echo.
echo -------------------------------------------
echo Log: %LOG%
echo -------------------------------------------
pause
