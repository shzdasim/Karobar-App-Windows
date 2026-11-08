#Requires -Version 5.1
$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Karobar-App-Windows (.NET 9 Native) - Pull and Run" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$LocalDotNetDir = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
$ProgFilesDotNetDir = Join-Path $env:ProgramFiles "dotnet"

if (Test-Path $LocalDotNetDir) {
    $env:Path = "$LocalDotNetDir;$env:Path"
    $env:DOTNET_ROOT = $LocalDotNetDir
} elseif (Test-Path $ProgFilesDotNetDir) {
    $env:Path = "$ProgFilesDotNetDir;$env:Path"
}

Push-Location $RepoRoot
try {
    # 1. Pull latest changes from GitHub if inside a git repo
    if (Test-Path (Join-Path $RepoRoot ".git")) {
        Write-Host "[1/4] Pulling latest commits from GitHub (Karobar-App-Windows)..." -ForegroundColor Green
        git pull
    }

    # 2. Verify .NET 9 SDK is installed (auto-install via official Microsoft dotnet-install.ps1 if missing)
    $HasDotNet9Sdk = $false
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $Sdks = & dotnet --list-sdks 2>$null
        if ($Sdks -match "^9\.") {
            $HasDotNet9Sdk = $true
        }
    }

    if (-not $HasDotNet9Sdk) {
        Write-Host "[2/4] .NET 9 SDK not found. Downloading and installing via official Microsoft installer..." -ForegroundColor Yellow
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $InstallerScript = Join-Path $env:TEMP "dotnet-install.ps1"
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $InstallerScript -UseBasicParsing
        & $InstallerScript -Channel 9.0 -InstallDir $LocalDotNetDir
        $env:Path = "$LocalDotNetDir;$env:Path"
        $env:DOTNET_ROOT = $LocalDotNetDir

        $CurrentUserPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
        if ($CurrentUserPath -notlike "*$LocalDotNetDir*") {
            [System.Environment]::SetEnvironmentVariable("Path", "$LocalDotNetDir;$CurrentUserPath", "User")
            [System.Environment]::SetEnvironmentVariable("DOTNET_ROOT", $LocalDotNetDir, "User")
        }
        Write-Host ".NET 9 SDK installed successfully to $LocalDotNetDir" -ForegroundColor Green
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
