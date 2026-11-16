@echo off
rem Rebuild the Skunk engine and refresh ScriptEditor\shared\Skunk.Core.dll.
rem Run this after changing any C# source under src\Skunk.Core, then Build / Publish
rem ScriptEditor\Skunk.rhproj from the Rhino 9 Script Editor as usual.
dotnet build "%~dp0Skunk.Core\Skunk.Core.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Build failed - ScriptEditor\shared was not updated.
  exit /b 1
)
echo.
echo Updated %~dp0..\ScriptEditor\shared\Skunk.Core.dll
