@echo off
setlocal EnableExtensions
cd /d "%~dp0"

:menu
cls
echo ============================================
echo          Opsis Desktop Customization 2.7.7.2402
echo ============================================
echo.
echo   1. Build the app and open it
echo   2. Build the installer
echo   3. Build the installer and open the app
echo   0. Exit
echo.
choice /c 1230 /n /m "Choose an option: "
if errorlevel 4 goto end
if errorlevel 3 goto installerAndOpen
if errorlevel 2 goto installerOnly
if errorlevel 1 goto buildAndOpen
goto end

:buildAndOpen
call :checkDotnet
if errorlevel 1 goto failed
call :buildApp
if errorlevel 1 goto failed
start "" "%~dp0publish\Opsis.Studio.exe"
echo.
echo Opsis Desktop Customization was built and opened.
pause
goto menu

:installerOnly
call :checkDotnet
if errorlevel 1 goto failed
call :findInno
if errorlevel 1 goto failed
call :buildInstaller
if errorlevel 1 goto failed
echo.
echo Installer created at:
echo "%~dp0Output\OpsisSetup-2.7.7.2402.exe"
pause
goto menu

:installerAndOpen
call :checkDotnet
if errorlevel 1 goto failed
call :findInno
if errorlevel 1 goto failed
call :buildInstaller
if errorlevel 1 goto failed
if not exist "%~dp0publish\Opsis.Studio.exe" goto failed
start "" "%~dp0publish\Opsis.Studio.exe"
echo.
echo The installer was created and Opsis Desktop Customization was opened.
echo Installer: "%~dp0Output\OpsisSetup-2.7.7.2402.exe"
pause
goto menu

:checkDotnet
where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo .NET SDK was not found. Install the .NET 11 SDK and retry.
    exit /b 1
)
dotnet --list-sdks | findstr /r "^11\." >nul
if errorlevel 1 (
    echo.
    echo .NET 11 SDK was not found. Install it and retry.
    exit /b 1
)
exit /b 0

:findInno
if defined ISCC if exist "%ISCC%" goto compilerFound
set "ISCC="
for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%I"
if defined ISCC goto compilerFound
if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Inno Setup 7\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Inno Setup 7\ISCC.exe"

if not defined ISCC (
    for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1" /v InstallLocation 2^>nul ^| findstr /i "InstallLocation"') do if exist "%%B\ISCC.exe" set "ISCC=%%B\ISCC.exe"
)
if not defined ISCC (
    for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1" /v InstallLocation 2^>nul ^| findstr /i "InstallLocation"') do if exist "%%B\ISCC.exe" set "ISCC=%%B\ISCC.exe"
)
if not defined ISCC (
    for /f "tokens=2,*" %%A in ('reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1" /v InstallLocation 2^>nul ^| findstr /i "InstallLocation"') do if exist "%%B\ISCC.exe" set "ISCC=%%B\ISCC.exe"
)
if not defined ISCC (
    for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%I"
)
if defined ISCC goto compilerFound
echo Enter the full path to ISCC.exe or drop it here.
set /p "ISCC=ISCC.exe path: "
if not defined ISCC exit /b 1
set "ISCC=%ISCC:"=%"
:compilerFound
if not exist "%ISCC%" (
    echo.
    echo ISCC.exe was not found at:
    echo "%ISCC%"
    exit /b 1
)
exit /b 0

:buildApp
echo.
echo Building Opsis Desktop Customization...
dotnet publish "%~dp0Opsis.Studio.csproj" --configuration Release --runtime win-x64 --self-contained false -o "%~dp0publish"
if errorlevel 1 exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\explorer\Build-Explorer.ps1" -OutputDirectory "%~dp0publish" -AllowClassicMenu
if errorlevel 1 exit /b 1
if not exist "%~dp0publish\Opsis.Studio.exe" exit /b 1
exit /b 0

:buildInstaller
echo.
echo Building Opsis Desktop Customization and compiling the installer...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\build.ps1" -Compiler "%ISCC%"
if errorlevel 1 exit /b 1
if not exist "%~dp0Output\OpsisSetup-2.7.7.2402.exe" exit /b 1
exit /b 0

:failed
echo.
echo Build failed. Check the error above, then retry.
pause
goto menu

:end
endlocal
