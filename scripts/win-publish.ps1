#Requires -Version 5.1
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$NativeRoot = Split-Path -Parent $ScriptDir
$OutDir = Join-Path $NativeRoot "publish\win-x64"

Push-Location $NativeRoot
try {
    Write-Host "Publishing Self-Contained Native Windows x64 Executable..." -ForegroundColor Cyan
    dotnet publish src/Karobar.Desktop/Karobar.Desktop.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishReadyToRun=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $OutDir

    Write-Host "Published standalone Karobar.exe to: $OutDir\Karobar.exe" -ForegroundColor Green
}
finally {
    Pop-Location
}
