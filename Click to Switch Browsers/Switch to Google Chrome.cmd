@echo off
title Switch Default Browser to Google Chrome
cls
"I:\Default Browser Switch\FastBrowserSwitch.exe" switch "Google Chrome"
if %errorlevel% neq 0 (
    echo.
    echo Press any key to continue...
    pause >nul
) else (
    timeout /t 2 >nul
)
