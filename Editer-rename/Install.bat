@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "EXE_PATH=%SCRIPT_DIR%EditorRenameTool.exe"

if not exist "%EXE_PATH%" (
    echo [INFO] EditorRenameTool.exe not found. Building now...
    call "%SCRIPT_DIR%build.bat"
    if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
)

echo [INFO] Registering shortcut to SendTo menu...
"%EXE_PATH%" --install

endlocal
