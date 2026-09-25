@echo off
setlocal

pushd "%~dp0"
if "%~1"=="" (
    set "ISS=MandelbrotViewer-Setup-2.22.7.iss"
) else (
    set "ISS=%~1"
)
if not exist "%ISS%" (
    echo Error: Inno script not found: "%ISS%".
    echo Usage: setup.bat [script.iss]
    popd
    pause
    exit /b 1
)

echo === Step 1: publishing self-contained build ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"
if %errorlevel% neq 0 (
    echo Error: publish failed.
    popd
    pause
    exit /b 1
)

set "ISCC=ISCC.exe"
where ISCC.exe >nul 2>nul
if %errorlevel% neq 0 (
    if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" (
        set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
    ) else if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" (
        set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
    ) else (
        echo Error: ISCC not found. Install Inno Setup 6 with:
        echo   winget install -e --id JRSoftware.InnoSetup
        popd
        pause
        exit /b 1
    )
)

echo === Step 2: compiling "%ISS%" ===
"%ISCC%" /O"%~dp0." "%ISS%"
set "EXITCODE=%ERRORLEVEL%"
popd
if %EXITCODE% neq 0 pause
exit /b %EXITCODE%
