#!/usr/bin/env pwsh
#Requires -Version 5.1
<#
.SYNOPSIS
    Packages PhotoComp for Windows x64.
.DESCRIPTION
    Publishes a self-contained release build targeting win-x64,
    then zips it into dist\PhotoComp-windows-x64.zip.
.EXAMPLE
    .\build-windows.ps1
    .\build-windows.ps1 -Version "1.2.0"
#>
param(
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectName  = "PhotoComp"
$ProjectFile  = "$PSScriptRoot\PhotoComp\PhotoComp.csproj"
$Runtime      = "win-x64"
$DistDir      = "$PSScriptRoot\dist"
$PublishDir   = "$DistDir\$Runtime"
$ZipName      = "$ProjectName-windows-x64-v$Version.zip"
$ZipPath      = "$DistDir\$ZipName"

Write-Host ""
Write-Host "=== PhotoComp Windows Packaging ===" -ForegroundColor Cyan
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
Write-Host "Publishing (Release / self-contained) ..." -ForegroundColor Cyan

dotnet publish $ProjectFile `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
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
