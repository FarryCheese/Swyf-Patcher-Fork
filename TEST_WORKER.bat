@echo off
setlocal
cd /d "%~dp0"
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_NOLOGO=1
set "GAME=%~1"
if "%GAME%"=="" (
  if exist "E:\SteamLibrary\steamapps\common\Scam With Your Friends" (
    set "GAME=E:\SteamLibrary\steamapps\common\Scam With Your Friends"
  ) else if exist "D:\steam\steamapps\common\Scam With Your Friends" (
    set "GAME=D:\steam\steamapps\common\Scam With Your Friends"
  )
)

echo ==============================================
echo  Qwen3 worker test (runs the TTS engine alone)
echo ==============================================
if not "%GAME%"=="" echo Game folder: %GAME%
echo Close the game before continuing. This can take a few minutes.
echo.
pause

echo.
echo [1/2] Testing GPU (Vulkan0)...
if not "%GAME%"=="" (
  dotnet run -c Release -- "%GAME%" --test-worker=Vulkan0 > worker_test.txt 2>&1
) else (
  dotnet run -c Release -- --test-worker=Vulkan0 > worker_test.txt 2>&1
)
type worker_test.txt

echo.
echo [2/2] Testing CPU...
if not "%GAME%"=="" (
  dotnet run -c Release -- "%GAME%" --test-worker=CPU >> worker_test.txt 2>&1
) else (
  dotnet run -c Release -- --test-worker=CPU >> worker_test.txt 2>&1
)
echo Done with CPU test.

echo.
echo Finished. Send me this file from this folder:
echo   worker_test.txt
pause
