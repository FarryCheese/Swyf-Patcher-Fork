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
echo  Collecting info for the Qwen3 fix
echo ==============================================
if not "%GAME%"=="" echo Game folder: %GAME%
echo Close the game before continuing.
echo.
pause

echo.
echo [1/2] Dumping code info from the original DLLs...
if not "%GAME%"=="" (
  dotnet run -c Release -- "%GAME%" --dump
) else (
  dotnet run -c Release -- --dump
)

echo.
echo [2/2] Searching for Qwen related files...
if not "%GAME%"=="" (
  dir /s /b "%GAME%" 2>nul | findstr /i "qwen worker llama ggml vulkan" > qwen_files.txt
) else (
  echo Game folder not specified for file search.
)
echo Saved: %~dp0qwen_files.txt

echo.
echo Done. Send me these two files from this folder:
echo   swyf_dump.txt
echo   qwen_files.txt
pause
