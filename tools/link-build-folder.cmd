@echo off
rem Keep the Rhino Script Editor's build folder out of Google Drive.
rem
rem Google Drive marks every folder in this project read-only (that is how it shows its desktop.ini
rem icons), and the Script Editor cannot delete read-only folders. Its package build deletes the
rem previous ScriptEditor\build\rh9 first, so every build after the first one fails with
rem   Error preparing build directory | Access to the path '...\build\rh9\shared' is denied.
rem This makes ScriptEditor\build a junction to %LOCALAPPDATA%\Skunk-build\ScriptEditor-build,
rem outside the synced folder (the dotnet builds already put their output there). The project and
rem every path stay the same. Run it once per machine; it is safe to run again.
setlocal
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "BUILD=%ROOT%\ScriptEditor\build"
set "TARGET=%LOCALAPPDATA%\Skunk-build\ScriptEditor-build"

if not exist "%TARGET%" mkdir "%TARGET%" || exit /b 1

dir /AL /B "%ROOT%\ScriptEditor" 2>nul | findstr /I /X "build" >nul
if not errorlevel 1 (
  echo ScriptEditor\build is already a junction to the build folder outside Google Drive.
  goto :show
)

if exist "%BUILD%" (
  echo Removing the old ScriptEditor\build folder ^(Script Editor build output only^).
  attrib -r -s -h "%BUILD%\*" /s /d >nul 2>&1
  attrib -r "%BUILD%" >nul 2>&1
  rmdir /s /q "%BUILD%"
  if exist "%BUILD%" (
    echo Could not remove ScriptEditor\build. Close Rhino ^(it may have the build loaded^) and run this again.
    exit /b 1
  )
)
mklink /J "%BUILD%" "%TARGET%" >nul || exit /b 1
echo Linked ScriptEditor\build to the build folder outside Google Drive.

:show
for /f "tokens=*" %%L in ('dir /AL "%ROOT%\ScriptEditor" ^| findstr /I /C:"<JUNCTION>"') do echo   %%L
exit /b 0
