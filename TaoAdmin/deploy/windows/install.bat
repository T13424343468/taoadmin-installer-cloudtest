@echo off
rem TaoAdmin one-click installer launcher (route menu: docker or native)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-windows.ps1" -Action install
pause
