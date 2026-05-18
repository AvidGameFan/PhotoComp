REM usage: run-build-windows.bat [version number]
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 %*

@echo off
REM Example usage:
REM run-build-windows.bat
REM run-build-windows.bat 1.0.0