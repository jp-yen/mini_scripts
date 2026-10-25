@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo  Windows-telnet Release Build (.NET 10 / win-x64)
echo ========================================================
echo.

dotnet publish -c Release -r win-x64 --no-self-contained -o "%~dp0dist"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ========================================================
    echo  [SUCCESS] Build succeeded!
    echo  Output: %~dp0dist\telnet.exe
    echo ========================================================
) else (
    echo.
    echo ========================================================
    echo  [ERROR] Build failed with exit code %ERRORLEVEL%.
    echo ========================================================
)

echo.
pause
endlocal
