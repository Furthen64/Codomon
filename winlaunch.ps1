# Requires: PowerShell 5+ or PowerShell Core
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Resolve script directory and switch to it
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "=== Launching Codomon ==="

dotnet run --project "Codomon.Desktop/Codomon.Desktop.csproj"