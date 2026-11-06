<#
.SYNOPSIS
    BIOS-Style Interactive Default Browser Selector
    Features real-time keyboard navigation, ANSI BIOS theme, and sub-15ms switching.
#>

$host.UI.RawUI.WindowTitle = "BIOS Default Browser Setup Utility"

# Run the compiled native high-speed engine
$exePath = Join-Path $PSScriptRoot "FastBrowserSwitch.exe"
if (Test-Path $exePath) {
    & $exePath
} else {
    Write-Host "[!] Compiling FastBrowserSwitch engine..." -ForegroundColor Yellow
    & "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize /target:exe /out:"$exePath" (Join-Path $PSScriptRoot "FastBrowserSwitch.cs")
    & $exePath
}

