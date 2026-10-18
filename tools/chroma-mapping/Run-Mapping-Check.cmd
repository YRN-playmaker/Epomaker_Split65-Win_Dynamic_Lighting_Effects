@echo off
cd /d "%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -MTA -ExecutionPolicy Bypass -File "%~dp0Collect-Mapping.ps1"
echo.
pause
