@echo off
cd /d "%~dp0"
echo Building LinuxSimplify...
dotnet publish -c Release -r win-x64 --self-contained true
if %ERRORLEVEL% NEQ 0 (echo BUILD FAILED & pause & exit /b 1)
echo.
echo SUCCESS: bin\Release\net8.0-windows\win-x64\publish\LinuxSimplify.exe
pause
