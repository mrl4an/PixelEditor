@echo off
rem Pixel Editor build script. Always builds fresh binaries, so an old version is never started by mistake.
rem   build.cmd             build the editor into "app\" and the AI tools (pixed, pixed-mcp) into "tools\"
rem   build.cmd --gui       build only the editor
rem   build.cmd --test      also run all tests first
rem   build.cmd --clean     delete old build output first
setlocal
cd /d "%~dp0"

set "GUIONLY="
set "TEST="
set "CLEAN="
:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--gui" set "GUIONLY=1"
if /i "%~1"=="--test" set "TEST=1"
if /i "%~1"=="--clean" set "CLEAN=1"
shift
goto parse
:parsed

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET 10 SDK is required: https://dotnet.microsoft.com/download
  exit /b 1
)

rem a running editor locks app\PixelEditor.exe and the build would fail half way
tasklist /fi "imagename eq PixelEditor.exe" 2>nul | find /i "PixelEditor.exe" >nul
if not errorlevel 1 (
  echo Pixel Editor is running. Close it and run the build again.
  exit /b 1
)

if defined CLEAN (
  echo Cleaning old output...
  if exist "app" rmdir /s /q "app"
  if exist "tools" rmdir /s /q "tools"
)

if defined TEST (
  echo Running tests...
  dotnet test "tests\PixelEditor.Tests" --nologo -v q
  if errorlevel 1 exit /b 1
  dotnet test "tests\PixelEditor.ViewModels.Tests" --nologo -v q
  if errorlevel 1 exit /b 1
)

echo Building the editor into app\ ...
dotnet publish "src\PixelEditor.App\PixelEditor.App.csproj" -c Release -o "app" --nologo -v q
if errorlevel 1 (
  echo Editor build failed.
  exit /b 1
)

if not defined GUIONLY (
  echo Building AI tools into tools\ ...
  dotnet publish "src\PixelEditor.Cli\PixelEditor.Cli.csproj" -c Release -o "tools" --nologo -v q
  if errorlevel 1 exit /b 1
  dotnet publish "src\PixelEditor.Mcp\PixelEditor.Mcp.csproj" -c Release -o "tools" --nologo -v q
  if errorlevel 1 exit /b 1
  copy /y "docs\AI-GUIDE.md" "tools\AI-GUIDE.md" >nul
)

echo Done. Start the editor with PixelEditor.cmd
endlocal
