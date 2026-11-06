@echo off
title Switch Default Browser to Avast Secure Browser
cls
"I:\Default Browser Switch\FastBrowserSwitch.exe" switch "Avast Secure Browser"
if %errorlevel% neq 0 (
    echo.
    echo Press any key to continue...
    pause >nul
) else (
    timeout /t 2 >nul
)
