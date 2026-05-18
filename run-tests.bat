@echo off
dotnet test PhotoComp.Tests\PhotoComp.Tests.csproj --nologo -p:AppendTargetFrameworkToOutputPath=false %*

REM Example usage:
REM run-tests.bat
REM run-tests.bat --filter "Delete"
REM run-tests.bat -v detailed