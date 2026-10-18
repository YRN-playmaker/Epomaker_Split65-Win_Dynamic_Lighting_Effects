@echo off
setlocal
powershell.exe -NoLogo -NoProfile -MTA -ExecutionPolicy Bypass -File "%~dp0Split65-Windows-Diagnose.ps1"
echo.
pause
