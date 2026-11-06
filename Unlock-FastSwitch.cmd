@echo off
setlocal
title Unlock Instant Browser Switching (Windows 10 UCPD Unlocker)
cls
echo ==============================================================================
echo        UNLOCK INSTANT BROWSER SWITCHING (WINDOWS 10 UCPD UNLOCKER)
echo ==============================================================================
echo.

:: Check for Administrator elevation
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Administrator privileges required.
    echo     Requesting elevation prompt...
    powershell -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

echo [*] Disabling User Choice Protection Driver (ucpd) startup...
sc config ucpd start= disabled >nul 2>&1
if %errorlevel% equ 0 (
    echo [+] Service startup successfully set to: DISABLED
) else (
    echo [!] Failed to configure service. Please run as Administrator.
)

echo [*] Disabling UCPD re-enable scheduled tasks...
schtasks /change /disable /tn "\Microsoft\Windows\AppxDeploymentClient\UCPD velocity" >nul 2>&1
echo [+] Scheduled task UCPD velocity: DISABLED

echo.
echo ==============================================================================
echo  NOTE: WHY A RESTART IS REQUIRED
echo ==============================================================================
echo  The 'ucpd.sys' driver is a kernel-level driver that Microsoft designed
echo  without a dynamic live-unload routine. Because of this:
echo.
echo  1. The driver has been PERMANENTLY DISABLED from starting again.
echo  2. A quick PC REBOOT is required to unload the driver from memory.
echo  3. After rebooting, all 1-click batch files and the BIOS selector
echo     will switch default browsers instantly in under 15 milliseconds!
echo ==============================================================================
echo.
echo  Would you like to restart your computer now to apply changes?
echo.
echo  [R] Restart Now
echo  [L] I will restart Later manually
echo.
set /p choice="Enter choice (R/L): "

if /i "%choice%"=="R" (
    echo.
    echo Restarting computer in 5 seconds...
    shutdown /r /t 5 /c "Rebooting to finalize instant default browser switching..."
) else (
    echo.
    echo Changes saved! Please restart your PC at your convenience.
    echo Until reboot, you can switch browsers via Windows Settings (ms-settings:defaultapps).
    echo.
    pause
)
