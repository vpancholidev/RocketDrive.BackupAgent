@echo off
setlocal EnableDelayedExpansion

REM ====== CONFIG ======
REM Change this if your RunRocketDrive.cmd is in a different folder
set "APP_DIR=%~dp0"
set "LAUNCHER=%APP_DIR%RunRocketDrive.cmd"

REM Name of the scheduled task
set "TASK_NAME=RocketDrive Every5Min"
REM ======================

echo ====================================================
echo Creating Task Scheduler entry: "%TASK_NAME%"
echo Launcher: "%LAUNCHER%"
echo ====================================================

REM Validate launcher exists
if not exist "%LAUNCHER%" (
  echo ERROR: Launcher not found: "%LAUNCHER%"
  echo Put this script in the same folder as RunRocketDrive.cmd or edit the LAUNCHER variable.
  pause
  exit /b 1
)

REM Compute current user DOMAIN\USERNAME
set "USER_DOMAIN=%USERDOMAIN%"
set "USER_NAME=%USERNAME%"
set "ACCOUNT=%USER_DOMAIN%\%USER_NAME%"

echo Current user: %ACCOUNT%

REM Remove existing task if present
schtasks /Query /TN "%TASK_NAME%" >nul 2>&1
if %errorlevel% equ 0 (
  echo Existing task found. Deleting it first...
  schtasks /Delete /TN "%TASK_NAME%" /F >nul 2>&1
  if %errorlevel% neq 0 (
    echo Failed to delete existing task. You may need to run this script as Administrator.
    pause
    exit /b 1
  )
)

REM Create the task:
REM /SC MINUTE /MO 5 -> every 5 minutes
REM /RU <account> /RL HIGHEST -> run as this user, highest privileges
REM /F -> force overwrite (we already deleted, but safe)
REM /IT -> Interactive token: runs in interactive session (no password required), requires the user to be logged on
schtasks /Create /TN "%TASK_NAME%" /TR "\"%LAUNCHER%\"" /SC MINUTE /MO 5 /RU "%ACCOUNT%" /RL HIGHEST /F /IT

if %errorlevel% neq 0 (
  echo.
  echo ERROR: schtasks failed to create the task.
  echo - If you ran this as a different user than the account above, try running the script while logged in as that user.
  echo - If you want the task to run when nobody is logged on, you must create it with stored credentials (will ask for password).
  pause
  exit /b 1
)

echo.
echo Task created successfully.
echo To view details:
echo   schtasks /Query /TN "%TASK_NAME%" /V /FO LIST
echo To open Task Scheduler GUI and inspect: Start -> Task Scheduler -> Task Scheduler Library -> "%TASK_NAME%"
echo.
echo NOTE:
echo - This task uses an Interactive token (/IT) and will run only when the user '%USER_NAME%' is logged in.
echo - If you want it to run even when the user is not logged in, recreate the task without /IT and provide the account password.
echo.
pause
endlocal
