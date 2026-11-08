@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\win-pull-and-run.ps1"
if %ERRORLEVEL% NEQ 0 pause
