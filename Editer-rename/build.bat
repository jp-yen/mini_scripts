@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [ERROR] csc.exe not found at: %CSC%
    pause
    exit /b 1
)

echo [INFO] Compiling EditorRenameTool.cs...
"%CSC%" /nologo /target:winexe /optimize /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /out:"%SCRIPT_DIR%EditorRenameTool.exe" "%SCRIPT_DIR%EditorRenameTool.cs"

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] Build succeeded: EditorRenameTool.exe
) else (
    echo [ERROR] Build failed with exit code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)
endlocal
