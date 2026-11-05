@echo off
rem Build RadiusTest as a single standalone executable with .NET 10
setlocal
echo Building standalone RadiusTest.exe (.NET 10)...
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
if errorlevel 1 (
    echo Build FAILED.
    exit /b 1
)
copy /y "%~dp0bin\Release\net10.0-windows\win-x64\publish\RadiusTest.exe" "%~dp0RadiusTest.exe" >nul 2>&1
if errorlevel 1 (
    del "%~dp0RadiusTest.exe.old" >nul 2>&1
    ren "%~dp0RadiusTest.exe" "RadiusTest.exe.old" >nul 2>&1
    copy /y "%~dp0bin\Release\net10.0-windows\win-x64\publish\RadiusTest.exe" "%~dp0RadiusTest.exe" >nul 2>&1
)
echo Build SUCCESS: %~dp0RadiusTest.exe
endlocal
