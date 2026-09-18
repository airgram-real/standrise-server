@echo off
rem Double-click: recover match history from the old game database.
rem Nothing is deleted - the current data is copied to a timestamped backup first.
rem Report: C:\StandRise\deploy\restore_report.txt
title StandRise: restore match history database
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\StandRise\deploy\Restore-MatchDb.ps1" > "C:\StandRise\deploy\restore_stdout.txt" 2>&1
exit
