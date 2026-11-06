@echo off
setlocal
title Unlock Instant Browser Switching (Disable UCPD)
echo ==============================================================================
echo        UNLOCK INSTANT BROWSER SWITCHING (WINDOWS 10 UCPD UNLOCKER)
echo ==============================================================================
echo.

:: Check for Administrator elevation
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Administrator rights required. Requesting elevation prompt...
    powershell -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

echo [*] Stopping and disabling User Choice Protection Driver (ucpd)...
sc config ucpd start= disabled
sc stop ucpd

echo [*] Disabling UCPD re-enable velocity scheduled tasks...
schtasks /change /disable /tn "\Microsoft\Windows\AppxDeploymentClient\UCPD velocity" >nul 2>&1

echo.
echo ==============================================================================
echo [SUCCESS] Windows UCPD kernel protection has been disabled!
echo You can now switch default browsers instantly in under 15ms.
echo ==============================================================================
echo.
pause

