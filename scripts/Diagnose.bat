@echo off
setlocal
"%LOCALAPPDATA%\Programs\SignalAtlas\SignalAtlas.Diagnostics.exe" --full
exit /b %errorlevel%
