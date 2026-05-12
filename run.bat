@echo off
setlocal

:: Check for Administrator privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ============================================================
    echo  WARNING: Not running as Administrator
    echo  The ETW demo requires elevated privileges for logman,
    echo  wevtutil, and the Event Log channel demo.
    echo.
    echo  Right-click this script and select "Run as administrator"
    echo ============================================================
    echo.
    pause
    exit /b 1
)

echo Running EtwEventSource demo as Administrator...
echo.

pushd "%~dp0EtwEventSource"
dotnet run
popd

echo.
pause
