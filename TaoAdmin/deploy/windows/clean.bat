@echo off
rem TaoAdmin one-click clean uninstall (stops services, removes artifacts)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-windows.ps1" -Action clean
pause
