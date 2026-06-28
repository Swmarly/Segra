@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "FRONTEND=%ROOT%Frontend"
set "WEBROOT=%ROOT%Resources\wwwroot"
set "PUBLISH_DIR=%ROOT%publish"
set "VERSION_ARG="

if not "%~1"=="" set "VERSION_ARG=-p:Version=%~1"

cd /d "%ROOT%"
if errorlevel 1 goto :fail

echo.
echo === Segra build ===
echo Root: %ROOT%
if defined VERSION_ARG echo Version: %~1

call :refresh_path

call :ensure_tool dotnet "Microsoft.DotNet.SDK.10" "https://dotnet.microsoft.com/download/dotnet/10.0"
if errorlevel 1 goto :fail

call :ensure_tool node "OpenJS.NodeJS.LTS" "https://nodejs.org/"
if errorlevel 1 goto :fail

call :ensure_tool npm "OpenJS.NodeJS.LTS" "https://nodejs.org/"
if errorlevel 1 goto :fail

call :close_running_app
if errorlevel 1 goto :fail

echo.
echo === Installing frontend dependencies ===
pushd "%FRONTEND%"
if errorlevel 1 goto :fail

if exist package-lock.json (
  call npm ci
) else (
  call npm install
)
if errorlevel 1 goto :fail

echo.
echo === Building frontend ===
call npm run build
if errorlevel 1 goto :fail
popd

echo.
echo === Copying frontend build ===
if not exist "%WEBROOT%" mkdir "%WEBROOT%"
if errorlevel 1 goto :fail

del /f /q "%WEBROOT%\*" >nul 2>nul
for /d %%D in ("%WEBROOT%\*") do rd /s /q "%%D"

xcopy "%FRONTEND%\dist\*" "%WEBROOT%\" /e /i /y >nul
if errorlevel 1 goto :fail

echo.
echo === Restoring backend ===
call dotnet restore "%ROOT%Segra.csproj" -r win-x64
if errorlevel 1 goto :fail

echo.
echo === Publishing application ===
if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"
if exist "%PUBLISH_DIR%" (
  echo.
  echo ERROR: Could not remove "%PUBLISH_DIR%".
  echo Close any running copy of Segra from that folder and try again.
  goto :fail
)
call dotnet publish "%ROOT%Segra.csproj" -c Release --self-contained -r win-x64 -o "%PUBLISH_DIR%" --no-restore %VERSION_ARG%
if errorlevel 1 goto :fail

echo.
echo === Build complete ===
echo Output: "%PUBLISH_DIR%"
echo Executable: "%PUBLISH_DIR%\Segra.exe"
echo.
exit /b 0

:refresh_path
if exist "%ProgramFiles%\nodejs" set "PATH=%ProgramFiles%\nodejs;%PATH%"
if exist "%ProgramFiles(x86)%\nodejs" set "PATH=%ProgramFiles(x86)%\nodejs;%PATH%"
if exist "%LocalAppData%\Programs\nodejs" set "PATH=%LocalAppData%\Programs\nodejs;%PATH%"
if exist "%AppData%\npm" set "PATH=%AppData%\npm;%PATH%"
if exist "%ProgramFiles%\dotnet" set "PATH=%ProgramFiles%\dotnet;%PATH%"
if exist "%LocalAppData%\Microsoft\dotnet" set "PATH=%LocalAppData%\Microsoft\dotnet;%PATH%"
exit /b 0

:close_running_app
tasklist /fi "imagename eq Segra.exe" 2>nul | find /i "Segra.exe" >nul
if errorlevel 1 exit /b 0

echo.
echo Segra.exe is currently running and may lock files in the publish folder.
choice /m "Close Segra.exe now"
if errorlevel 2 exit /b 1

taskkill /im Segra.exe /f >nul
if errorlevel 1 (
  echo Could not close Segra.exe.
  exit /b 1
)
exit /b 0

:ensure_tool
where %~1 >nul 2>nul
if not errorlevel 1 exit /b 0

echo.
echo ERROR: %~1 was not found on PATH.
echo Segra needs %~1 to build.

where winget >nul 2>nul
if errorlevel 1 (
  echo.
  echo winget was not found, so the build script cannot install it automatically.
  echo Install it manually from:
  echo   %~3
  echo Then open a new Command Prompt and run build.bat again.
  exit /b 1
)

choice /m "Install %~1 now with winget"
if errorlevel 2 (
  echo.
  echo Install it manually from:
  echo   %~3
  echo Then open a new Command Prompt and run build.bat again.
  exit /b 1
)

winget install --id %~2 --exact --source winget --accept-package-agreements --accept-source-agreements
if errorlevel 1 exit /b 1

call :refresh_path
where %~1 >nul 2>nul
if errorlevel 1 (
  echo.
  echo %~1 was installed, but this Command Prompt still cannot find it.
  echo Open a new Command Prompt and run build.bat again.
  exit /b 1
)
exit /b 0

:fail
echo.
echo Build failed.
exit /b 1
