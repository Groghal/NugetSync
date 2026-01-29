@echo off
setlocal

set "ROOT=%~dp0"

echo Building NugetSync tool...
dotnet build "%ROOT%src\NugetSync.Cli" -c Release || exit /b 1

echo Uninstalling existing tool (if present)...
dotnet tool uninstall NugetSync.Cli --tool-manifest "%ROOT%.config\dotnet-tools.json"

echo Installing local tool...
dotnet tool install NugetSync.Cli --add-source "%ROOT%src\NugetSync.Cli\bin" --tool-manifest "%ROOT%.config\dotnet-tools.json" || exit /b 1

echo Done. Run: dotnet nugetsync
@echo off
setlocal

set "ROOT="

echo Building NugetSync tool...
dotnet build "%ROOT%\src\NugetSync.Cli" -c Release || exit /b 1

echo Uninstalling existing tool (if present)...
dotnet tool uninstall NugetSync.Cli --tool-manifest "%ROOT%\.config\dotnet-tools.json"

echo Installing local tool...
dotnet tool install NugetSync.Cli --add-source "%ROOT%\src\NugetSync.Cli\bin" --tool-manifest "%ROOT%\.config\dotnet-tools.json" || exit /b 1

echo Done. Run: dotnet nugetsync
