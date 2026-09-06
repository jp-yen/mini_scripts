@echo off
setlocal enabledelayedexpansion

cd /d "%~dp0"

set "PUBLISH_DIR=publish"
set "TARGET_EXE=%PUBLISH_DIR%\FontSelector.exe"
set "TARGET_PDB=%PUBLISH_DIR%\FontSelector.pdb"
set "PROJECT_PATH=src\FontSelector\FontSelector.csproj"


echo ===================================================
echo  Font Selector - Build and Publish Script
echo ===================================================

:: 1. Terminate running process if any
echo [1/2] Checking and stopping running FontSelector.exe...
taskkill /f /im FontSelector.exe >nul 2>&1
timeout /t 1 /nobreak >nul 2>&1

:: 2. Build and publish with debug strip (self-contained single file)
echo.
echo [2/2] Publishing project (Release / win-x64 / SingleFile / SelfContained / Stripped)...
dotnet publish "%PROJECT_PATH%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o "%PUBLISH_DIR%"

:: Clean up optional XML documentation files
if exist "%PUBLISH_DIR%\*.xml" del /f /q "%PUBLISH_DIR%\*.xml" 2>nul

if %ERRORLEVEL% equ 0 (
    echo.
    echo ===================================================
    echo  [SUCCESS] Build finished successfully!
    echo  Output: %TARGET_EXE%
    echo ===================================================
) else (
    echo.
    echo ===================================================
    echo  [FAILED] Build failed. See error output above.
    echo ===================================================
    echo.
    pause
)
