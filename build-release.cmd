@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  exit /b 1
)

set "PROJECT_DIR=%~dp0"
set "OUTPUT_DIR=%PROJECT_DIR%release\win-x64"
dotnet publish "%PROJECT_DIR%MikroTikManager.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%OUTPUT_DIR%"
if errorlevel 1 exit /b 1

echo.
echo Standalone release created:
echo %OUTPUT_DIR%\MikroTikManager.exe
endlocal
