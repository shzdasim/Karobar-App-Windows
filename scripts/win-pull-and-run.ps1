#Requires -Version 5.1
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Karobar-App-Windows (.NET 9 Native) — Pull & Run" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

# 1. Verify .NET 9 SDK is installed on Windows VM (auto-install via winget if missing)
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[1/4] .NET 9 SDK not found. Installing Microsoft.DotNet.SDK.9 via winget..." -ForegroundColor Yellow
    winget install Microsoft.DotNet.SDK.9 --accept-package-agreements --accept-source-agreements
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
}

Push-Location $RepoRoot
try {
    # 2. Pull latest changes from GitHub if inside a git repo
    if (Test-Path (Join-Path $RepoRoot ".git")) {
        Write-Host "[1/4] Pulling latest commits from GitHub (Karobar-App-Windows)..." -ForegroundColor Green
        git pull
    }

    Write-Host "[2/4] Restoring & Building Karobar.Native.sln..." -ForegroundColor Green
    dotnet build Karobar.Native.sln -c Debug

    Write-Host "[3/4] Running Phase 1 xUnit Verification Suite..." -ForegroundColor Green
    dotnet test tests/Karobar.Tests/Karobar.Tests.csproj -c Debug --no-build

    Write-Host "[4/4] Launching Native Windows WPF Application (Karobar.Desktop)..." -ForegroundColor Cyan
    dotnet run --project src/Karobar.Desktop/Karobar.Desktop.csproj -c Debug --no-build
}
finally {
    Pop-Location
}
