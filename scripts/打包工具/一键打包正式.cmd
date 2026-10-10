@echo off
chcp 65001 >nul
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%..\.."

pushd "%REPO_ROOT%" >nul 2>&1
if errorlevel 1 (
    echo [失败] 无法进入仓库根目录：%REPO_ROOT%
    exit /b 1
)

if not exist "MineRailMonitor.sln" (
    echo [失败] 当前目录不是 MineRailMonitor 仓库根目录。
    popd
    exit /b 1
)

where powershell.exe >nul 2>&1
if errorlevel 1 (
    echo [失败] 未找到 Windows PowerShell。
    popd
    exit /b 1
)

where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo [失败] 未找到 dotnet，请先安装 .NET SDK。
    popd
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%一键打包正式.ps1"
set "EXIT_CODE=%ERRORLEVEL%"

if "%~1"=="/nopause" goto :done
echo.
pause

:done
popd
exit /b %EXIT_CODE%
