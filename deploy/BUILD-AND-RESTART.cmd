@echo off
rem Double-click: build VsCode.dll, then restart the RPC process.
rem Photon is not touched. Report: C:\StandRise\deploy\build_report.txt
title StandRise: build + restart RPC
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\StandRise\deploy\Build-Restart.ps1" > "C:\StandRise\deploy\build_stdout.txt" 2>&1
exit
