# Requires: PowerShell 5+ or PowerShell Core
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Resolve script directory and switch to it
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$APP_VERSION = "0.1.0"
$BUILD_DATE  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")

Write-Host "=== Building Codomon ==="
Write-Host ("    Version:    {0}" -f $APP_VERSION)
Write-Host ("    Build date: {0}" -f $BUILD_DATE)

dotnet build "Codomon.Desktop/Codomon.Desktop.csproj" -c Release `
    -p:AppVersion="$APP_VERSION" `
    -p:BuildDate="$BUILD_DATE"

Write-Host "=== Build complete ==="