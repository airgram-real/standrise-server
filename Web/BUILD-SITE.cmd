@echo off
setlocal enableextensions
chcp 65001 >nul
set "SRC=C:\Users\Administrator\Downloads\ProjectRework\ProjectRework"
set "DST=C:\StandRise\Web\wwwroot"
set "LOG=C:\StandRise\Web\build_site.txt"

echo ==== BUILD SITE ==== > "%LOG%"
echo %DATE% %TIME% >> "%LOG%"

set "NPM=C:\Program Files\nodejs\npm.cmd"
if not exist "%NPM%" set "NPM=npm"

echo. >> "%LOG%"
echo --- npm run build --- >> "%LOG%"
pushd "%SRC%"
call "%NPM%" run build >> "%LOG%" 2>&1
set "RC=%ERRORLEVEL%"
popd
echo exitcode=%RC% >> "%LOG%"

if not "%RC%"=="0" goto fail
if not exist "%SRC%\dist\index.html" goto fail

echo. >> "%LOG%"
echo --- copy dist to wwwroot --- >> "%LOG%"
if exist "%DST%" rmdir /s /q "%DST%"
mkdir "%DST%"
xcopy "%SRC%\dist" "%DST%" /E /I /Y >> "%LOG%" 2>&1
echo copied=%ERRORLEVEL% >> "%LOG%"

echo. >> "%LOG%"
echo ==== OK ==== >> "%LOG%"
dir /b "%DST%" >> "%LOG%"
goto done

:fail
echo. >> "%LOG%"
echo ==== BUILD FAILED ==== >> "%LOG%"

:done
type "%LOG%"
echo.
echo -------------------------------------------
echo Log: %LOG%
echo -------------------------------------------
pause
