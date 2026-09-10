@echo off
echo =======================================================
echo Compiling GnuPG UI (GnuGP-UI.exe) - locating csc.exe
echo =======================================================

setlocal EnableDelayedExpansion

rem Candidate locations for csc.exe
set CSC_CAND1=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set CSC_CAND2=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe

set "CSC="
if exist "%CSC_CAND1%" (
    set "CSC=%CSC_CAND1%"
)
if not defined CSC (
    if exist "%CSC_CAND2%" (
        set "CSC=%CSC_CAND2%"
    )
)

if not defined CSC (
    where csc.exe >nul 2>nul
    if %ERRORLEVEL% EQU 0 (
        for /f "delims=" %%i in ('where csc.exe') do (
            set "CSC=%%i"
            goto found_csc
        )
    )
)
:found_csc

if not defined CSC (
    echo Error: csc.exe not found in standard framework locations or PATH.
    echo On Windows 11, the .NET Framework csc.exe is usually available without admin.
    exit /b 1
)

rem Determine framework dir for WPF references if possible
for %%F in ("%CSC%") do set "CSC_DIR=%%~dpF"

rem Search candidate directories for PresentationCore/PresentationFramework/WindowsBase
set "PRESENT_DIR="
set "CAND1=%CSC_DIR%..\WPF"
set "CAND2=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\WPF"
set "CAND3=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF"
set "CAND4=!ProgramFiles(x86)!\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades"
set "CAND5=!ProgramFiles(x86)!\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Facades"

if exist "!CAND1!\PresentationCore.dll" set "PRESENT_DIR=!CAND1!"
if not defined PRESENT_DIR if exist "!CAND2!\PresentationCore.dll" set "PRESENT_DIR=!CAND2!"
if not defined PRESENT_DIR if exist "!CAND3!\PresentationCore.dll" set "PRESENT_DIR=!CAND3!"
if not defined PRESENT_DIR if exist "!CAND4!\PresentationCore.dll" set "PRESENT_DIR=!CAND4!"
if not defined PRESENT_DIR if exist "!CAND5!\PresentationCore.dll" set "PRESENT_DIR=!CAND5!"

echo Using C# compiler: %CSC%

if defined PRESENT_DIR goto use_present
goto no_present

:use_present
echo Found WPF assemblies in: %PRESENT_DIR%
set REFS=/r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:"%PRESENT_DIR%\PresentationCore.dll" /r:"%PRESENT_DIR%\PresentationFramework.dll" /r:"%PRESENT_DIR%\WindowsBase.dll" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
goto do_compile

:no_present
echo Warning: WPF assemblies not found in standard candidate locations.
echo Attempting compile without explicit WPF references (may fail).
set REFS=/r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:PresentationCore.dll /r:PresentationFramework.dll /r:WindowsBase.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
goto do_compile

:do_compile
echo Compiling with: "%CSC%" %REFS%
"%CSC%" /nologo /target:winexe /out:GnuGP-UI.exe %REFS% Program.cs GpgModels.cs GpgService.cs Services\GpgService.Keys.cs Services\GpgService.Crypto.cs ThemeHelper.cs MainWindow.cs Views\DashboardView.cs Views\KeyManagerView.cs Views\KeyGeneratorWindow.cs Views\KeyImportWindow.cs Views\CryptoView.cs Views\CryptoView.Encrypt.cs Views\CryptoView.Decrypt.cs Views\SignVerifyView.cs Views\SettingsWindow.cs

if %ERRORLEVEL% EQU 0 (
    echo =======================================================
    echo Compilation SUCCESS! GnuGP-UI.exe created.
    echo =======================================================
) else (
    echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
    echo Compilation FAILED! Return code %ERRORLEVEL%
    echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
    exit /b %ERRORLEVEL%
)

endlocal
