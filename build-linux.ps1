#!/usr/bin/env pwsh
#Requires -Version 5.1
<#
.SYNOPSIS
    Packages PhotoComp for Linux x64 from a Windows machine.
.DESCRIPTION
    Cross-compiles a self-contained release build targeting linux-x64
    using dotnet publish, then zips it into dist\PhotoComp-linux-x64.zip.
    No Linux machine or WSL required.
.EXAMPLE
    .\build-linux.ps1
    .\build-linux.ps1 -Version "1.2.0"
#>
param(
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectName  = "PhotoComp"
$ProjectFile  = "$PSScriptRoot\PhotoComp\PhotoComp.csproj"
$Runtime      = "linux-x64"
$DistDir      = "$PSScriptRoot\dist"
$PublishDir   = "$DistDir\$Runtime"
$ZipName      = "$ProjectName-linux-x64-v$Version.zip"
$ZipPath      = "$DistDir\$ZipName"

Write-Host ""
Write-Host "=== PhotoComp Linux Packaging (cross-compile from Windows) ===" -ForegroundColor Cyan
Write-Host "  Version    : $Version"
Write-Host "  Runtime    : $Runtime"
Write-Host "  Output zip : dist\$ZipName"
Write-Host ""

# ── 1. Clean previous artifacts ─────────────────────────────────────────────
if (Test-Path $PublishDir) {
    Write-Host "Cleaning $PublishDir ..." -ForegroundColor DarkGray
    Remove-Item $PublishDir -Recurse -Force
}
if (Test-Path $ZipPath) {
    Write-Host "Removing old $ZipName ..." -ForegroundColor DarkGray
    Remove-Item $ZipPath -Force
}
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# ── 2. Restore & publish ─────────────────────────────────────────────────────
Write-Host "Publishing (Release / self-contained / linux-x64) ..." -ForegroundColor Cyan

dotnet publish $ProjectFile `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    --output $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: dotnet publish failed (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

# ── 3. Zip the publish directory ─────────────────────────────────────────────
Write-Host ""
Write-Host "Creating zip ..." -ForegroundColor Cyan
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

$SizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "Done!  dist\$ZipName  ($SizeMB MB)" -ForegroundColor Green
Write-Host ""
