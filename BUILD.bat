@echo off
:: ============================================================
::  BUILD.bat — Build and optionally run MultiMonitorRestorer
::  Run this from the project root (where ARCHITECTURE.md is).
::  Requires: .NET 8 SDK installed
:: ============================================================

setlocal
set PROJ=MultiMonitorRestorer\MultiMonitorRestorer.csproj
set DOTNET=%LOCALAPPDATA%\dotnet\dotnet.exe
if not exist "%DOTNET%" set DOTNET=dotnet
echo.
echo ─────────────────────────────────────────────────
echo  Multi-Monitor Window Layout Restorer — Build
echo ─────────────────────────────────────────────────
echo.

dotnet build "%PROJ%" -c Release
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build failed. See errors above.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Build succeeded!
echo.
echo Options:
echo   R = Run the app now (appears in system tray)
echo   P = Publish as single-file self-contained .exe
echo   Q = Quit
echo.
set /p CHOICE=Choose [R/P/Q]: 

if /i "%CHOICE%"=="R" goto :run
if /i "%CHOICE%"=="P" goto :publish
goto :end

:run
echo.
echo Starting app — look for the icon in the system tray...
start "" "MultiMonitorRestorer\bin\Release\net8.0-windows\MultiMonitorRestorer.exe"
goto :end

:publish
echo.
echo Publishing self-contained single-file executable...
dotnet publish "%PROJ%" -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:PublishTrimmed=false ^
  -o "publish"
echo.
echo Output: %CD%\publish\MultiMonitorRestorer.exe
goto :end

:end
echo.
endlocal
pause
