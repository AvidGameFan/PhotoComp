@echo off
REM usage: run-build-windows-arm64.bat [version number]
REM If no version is supplied, reads the default from version.txt
REM Example usage:
REM   run-build-windows-arm64.bat
REM   run-build-windows-arm64.bat 1.2.0

if "%~1"=="" (
    set /p VERSION=<version.txt
) else (
    set VERSION=%~1
)

powershell -ExecutionPolicy Bypass -File .\build-windows-arm64.ps1 -Version "%VERSION%"
