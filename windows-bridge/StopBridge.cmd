@echo off
setlocal
"%~dp0prebuilt\Split65Bridge.exe" --stop
if errorlevel 1 pause
