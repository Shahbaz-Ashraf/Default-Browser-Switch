@echo off
title Switch Default Browser to Mozilla Firefox
cls
"I:\Default Browser Switch\FastBrowserSwitch.exe" switch "Mozilla Firefox"
if %errorlevel% neq 0 (
    echo.
    echo Press any key to continue...
    pause >nul
) else (
    timeout /t 2 >nul
)
