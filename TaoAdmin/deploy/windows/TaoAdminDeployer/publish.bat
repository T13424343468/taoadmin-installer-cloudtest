@echo off
REM ============================================================
REM TaoAdminDeployer publish script
REM PACKAGE VERSION: DEPLOYER-V1 (2026-11-21)
REM Publishes a self-contained single-file exe (no .NET needed on target).
REM ============================================================
cd /d "%~dp0"
echo Publishing TaoAdminDeployer (self-contained, win-x64) ...
dotnet publish src\Deployer.App\Deployer.App.csproj -c Release -o "%~dp0.." --nologo
if errorlevel 1 (
  echo PUBLISH FAILED
  exit /b 1
)
if not exist "%~dp0..\TaoAdminDeployer.exe" (
  echo PUBLISH OUTPUT MISSING: TaoAdminDeployer.exe
  exit /b 1
)
echo PUBLISH OK: deploy\windows\TaoAdminDeployer.exe
exit /b 0
