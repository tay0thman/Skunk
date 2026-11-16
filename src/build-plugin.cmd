@echo off
rem Build the compiled, always-on Skunk plug-in.
rem   dist\Skunk.rhp + dist\Skunk.rui      drag Skunk.rhp onto Rhino 9 to install it
rem   dist\skunk-<version>-rh9_0-win.yak    or drag the .yak onto Rhino (Package Manager install)
rem Same plug-in id and commands as ScriptEditor\Skunk.rhproj: install one or the other, never both.
dotnet build "%~dp0Skunk\Skunk.csproj" -c Release
if errorlevel 1 (
  echo.
  echo Build failed - dist was not updated.
  exit /b 1
)
echo.
echo Updated %~dp0..\dist
