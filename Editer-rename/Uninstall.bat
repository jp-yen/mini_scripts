@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "EXE_PATH=%SCRIPT_DIR%EditorRenameTool.exe"

if exist "%EXE_PATH%" (
    echo [INFO] Removing shortcut from SendTo menu...
    "%EXE_PATH%" --uninstall
) else (
    set "SHORTCUT=%APPDATA%\Microsoft\Windows\SendTo\Editor Rename Tool.lnk"
    if exist "%SHORTCUT%" (
        del /f /q "%SHORTCUT%"
        echo [INFO] Shortcut removed successfully.
    ) else (
        echo [INFO] Shortcut not found.
    )
)

endlocal
