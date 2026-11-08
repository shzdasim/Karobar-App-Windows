#Requires -Version 5.1
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Karobar-App-Windows (.NET 9 Native) - Pull and Run" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

# 1. Verify .NET 9 SDK is installed on Windows VM (auto-install via winget if missing)
$HasDotNet9Sdk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $Sdks = & dotnet --list-sdks 2>$null
    if ($Sdks -match "^9\.") {
        $HasDotNet9Sdk = $true
    }
}

if (-not $HasDotNet9Sdk) {
    Write-Host "[1/4] .NET 9 SDK not found. Installing Microsoft.DotNet.SDK.9 via winget..." -ForegroundColor Yellow
    winget install Microsoft.DotNet.SDK.9 --accept-package-agreements --accept-source-agreements
    $MachinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $UserPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$MachinePath;$UserPath"
}

Push-Location $RepoRoot
try {
    # 2. Pull latest changes from GitHub if inside a git repo
    if (Test-Path (Join-Path $RepoRoot ".git")) {
        Write-Host "[1/4] Pulling latest commits from GitHub (Karobar-App-Windows)..." -ForegroundColor Green
        git pull
    }

    Write-Host "[2/4] Restoring and Building Karobar.Native.sln..." -ForegroundColor Green
    dotnet build Karobar.Native.sln -c Debug
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    Write-Host "[3/4] Running Phase 1 xUnit Verification Suite..." -ForegroundColor Green
    dotnet test tests/Karobar.Tests/Karobar.Tests.csproj -c Debug --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

    Write-Host "[4/4] Launching Native Windows WPF Application (Karobar.Desktop)..." -ForegroundColor Cyan
    dotnet run --project src/Karobar.Desktop/Karobar.Desktop.csproj -c Debug --no-build
}
finally {
    Pop-Location
}
