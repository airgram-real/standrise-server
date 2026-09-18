@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\StandRise\deploy\Test-Maps.ps1"
