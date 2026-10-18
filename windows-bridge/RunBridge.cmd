@echo off
setlocal
cd /d "%~dp0"
"%~dp0prebuilt\Split65Bridge.exe"
if errorlevel 1 pause
