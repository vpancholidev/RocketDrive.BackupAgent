@echo off
setlocal
cd /d "%~dp0"
"RocketDrive.BackupAgent.exe" %*
exit /b %ERRORLEVEL%