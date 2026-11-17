@echo off
rem Pixel Editor launcher: double-click to start the editor (GUI).
rem It rebuilds automatically when the sources are newer than the built program, so an old version is never started.
rem   PixelEditor.cmd               start the editor
rem   PixelEditor.cmd file.pixproj  open a project
rem   PixelEditor.cmd --rebuild     force a rebuild
rem   PixelEditor.cmd --build-only  build without starting
rem For the full build (editor + AI tools, tests) use build.cmd.
setlocal
cd /d "%~dp0"

set "REBUILD="
set "BUILDONLY="
set "ARGS="
:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--rebuild" (set "REBUILD=1") else if /i "%~1"=="--build-only" (set "BUILDONLY=1") else set ARGS=%ARGS% "%~1"
shift
goto parse
:parsed

if defined REBUILD goto build
if not exist "app\PixelEditor.exe" goto build

rem newer source files (cs, xaml, json, csproj, props, icons) than the built exe mean the build is old
powershell -NoProfile -ExecutionPolicy Bypass -Command "$e=(Get-Item 'app\PixelEditor.exe').LastWriteTime; $n=Get-ChildItem -Path 'src','Directory.Build.props' -Recurse -File | Where-Object { $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\obj\*' -and $_.Extension -in '.cs','.xaml','.json','.csproj','.props','.ico','.png' -and $_.LastWriteTime -gt $e } | Select-Object -First 1; if ($n) { Write-Host ('Sources changed (' + $n.Name + '), rebuilding...'); exit 1 } else { exit 0 }"
if errorlevel 1 goto build
goto run

:build
call "%~dp0build.cmd" --gui
if errorlevel 1 (
  echo.
  echo Build failed, the editor was not started.
  pause
  exit /b 1
)
if defined BUILDONLY exit /b 0

:run
start "" "app\PixelEditor.exe" %ARGS%
endlocal
