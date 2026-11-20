@echo off
rem TaoAdmin service status viewer
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-windows.ps1" -Action status
pause
