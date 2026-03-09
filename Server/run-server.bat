@echo off
chcp 65001 >nul
cd /d "%～dp0"

echo [+] 检查 .NET SDK...
dotnet --list-sdks >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] 未检测到 .NET SDK！
    echo     请先安装 .NET 8 SDK:
    echo     https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo [+] 还原 NuGet 依赖（包括 SQLite）...
dotnet restore
if %errorlevel% neq 0 (
    echo [!] 依赖还原失败！
    pause
    exit /b 1
)

echo [+] 编译并启动服务器...
dotnet run

echo [!] 服务器已退出。
pause