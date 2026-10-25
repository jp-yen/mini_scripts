@echo off
setlocal
cd /d "%~dp0"

echo ===================================================
echo   CPU Thermo Watcher - Build Script (csc.exe)
echo ===================================================
echo.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
    echo [ERROR] csc.exe not found at: %CSC%
    goto :FAIL
)

echo [1/3] Compiler found: %CSC%

if not exist "OpenHardwareMonitorLib.dll" (
    echo [2/3] OpenHardwareMonitorLib.dll not found. Downloading via PowerShell...
    powershell -ExecutionPolicy Bypass -File .\setup_lib.ps1
)

if not exist "OpenHardwareMonitorLib.dll" (
    echo [ERROR] OpenHardwareMonitorLib.dll could not be obtained.
    goto :FAIL
)
echo [2/3] Dependency check: OpenHardwareMonitorLib.dll OK

set "BAK=CpuThermoWatcher.old_%RANDOM%.exe"
if exist "CpuThermoWatcher.exe" (
    ren "CpuThermoWatcher.exe" "%BAK%" 2>nul
)

echo [3/3] Compiling source files...
"%CSC%" @csc.rsp

if %ERRORLEVEL% neq 0 (
    if exist "%BAK%" ren "%BAK%" "CpuThermoWatcher.exe" 2>nul
    goto :FAIL
)

del /f /q CpuThermoWatcher.old*.exe 2>nul

echo.
echo ===================================================
echo   [SUCCESS] Build completed successfully!
echo   Output: %~dp0CpuThermoWatcher.exe
echo ===================================================
echo.
echo Please run CpuThermoWatcher.exe as Administrator.
if "%NO_PAUSE%"=="" pause
exit /b 0

:FAIL
echo.
echo ===================================================
echo   [FAILED] Compilation failed.
echo ===================================================
echo.
if "%NO_PAUSE%"=="" pause
exit /b 1
