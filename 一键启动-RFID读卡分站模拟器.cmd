@echo off
chcp 65001 >nul
setlocal

set "ROOT=%~dp0"
set "PROJ=%ROOT%src\MineRailMonitor.Simulator\MineRailMonitor.Simulator.csproj"
set "EXE=%ROOT%src\MineRailMonitor.Simulator\bin\Debug\net48\MineRailMonitor.Simulator.exe"

if not exist "%PROJ%" (
    echo ERROR: Simulator project not found:
    echo %PROJ%
    pause
    exit /b 1
)

echo Building current simulator source...
dotnet build "%PROJ%" --configuration Debug --no-restore --verbosity minimal -p:UseSharedCompilation=false

if errorlevel 1 (
    echo.
    echo ERROR: Failed to build simulator. Please close any old simulator window and try again.
    pause
    exit /b 1
)

if not exist "%EXE%" (
    echo.
    echo ERROR: Debug simulator EXE not found:
    echo %EXE%
    pause
    exit /b 1
)

echo Starting: %EXE%
start "RFID读卡分站模拟器" "%EXE%"

endlocal
