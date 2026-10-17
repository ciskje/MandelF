@echo off
setlocal

pushd "%~dp0"
where dotnet >nul 2>nul
if %errorlevel% equ 0 (
    set "DOTNET=dotnet"
) else if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
) else (
    echo Error: .NET SDK not found - not on PATH and missing "%USERPROFILE%\.dotnet\dotnet.exe".
    popd
    pause
    exit /b 1
)

"%DOTNET%" run --project "source\MandelbrotViewer.csproj" -- %*
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%
