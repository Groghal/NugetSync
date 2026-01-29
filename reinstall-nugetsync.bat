@echo off

set "ROOT=%~dp0"

echo Building NugetSync tool...
dotnet build "%ROOT%src\NugetSync.Cli" -c Release || exit /b 1

set "TOOL_PATH=%ROOT%.tools"

if not exist "%TOOL_PATH%" (
  mkdir "%TOOL_PATH%"
)

echo Uninstalling existing local tool (if present)...
dotnet tool uninstall NugetSync.Cli --tool-path "%TOOL_PATH%"

echo Installing local tool to %TOOL_PATH% ...
dotnet tool install NugetSync.Cli --tool-path "%TOOL_PATH%" --add-source "%ROOT%src\NugetSync.Cli\bin" || exit /b 1

echo Ensuring PATH includes %TOOL_PATH% ...
echo %PATH% | find /I "%TOOL_PATH%" >nul
if errorlevel 1 (
  set "PATH=%PATH%;%TOOL_PATH%"
)

echo Done. Run: dotnet nugetsync or dotnet-nugetsync
echo.
echo Keeping this window open for this session...
cmd /k
