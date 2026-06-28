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

call :require dotnet
if errorlevel 1 goto :fail

call :require node
if errorlevel 1 goto :fail

call :require npm
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

:require
where %1 >nul 2>nul
if errorlevel 1 (
  echo.
  echo ERROR: %1 was not found on PATH.
  echo Please install %1 and try again.
  exit /b 1
)
exit /b 0

:fail
echo.
echo Build failed.
exit /b 1
