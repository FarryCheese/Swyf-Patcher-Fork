@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
set "DN=dotnet"

where dotnet >nul 2>nul
if errorlevel 1 set "NEEDSDK=1"
if not defined NEEDSDK (
  dotnet --list-sdks | findstr /b "8\." >nul
  if errorlevel 1 set "NEEDSDK=1"
)

if defined NEEDSDK (
  echo .NET 8 SDK not found. Installing via winget...
  winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
  if errorlevel 1 (
    echo.
    echo Automatic install failed.
    echo Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
  )
  set "DN=%ProgramFiles%\dotnet\dotnet.exe"
)

"%DN%" run -c Release -- %*
set RC=%errorlevel%

echo.
pause
exit /b %RC%
