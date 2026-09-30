@echo off
setlocal
where powershell.exe >nul 2>nul
if errorlevel 1 (echo PowerShell is required for setup.& exit /b 1)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1" %*
set "code=%errorlevel%"
if not "%code%"=="0" echo Setup failed. See the message above and run Repair.bat after fixing the issue.
exit /b %code%
