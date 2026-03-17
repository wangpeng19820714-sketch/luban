@echo off
setlocal

set SCRIPT_DIR=%~dp0
for %%I in ("%SCRIPT_DIR%..") do set ROOT_DIR=%%~fI

set PROJECT_FILE=%ROOT_DIR%\src\Luban\Luban.csproj
set OUTPUT_DIR=%ROOT_DIR%\build\output
set CONFIGURATION=Release

if not exist "%PROJECT_FILE%" (
    echo [ERROR] Project file not found: "%PROJECT_FILE%"
    exit /b 1
)

echo [INFO] Root      : "%ROOT_DIR%"
echo [INFO] Project   : "%PROJECT_FILE%"
echo [INFO] Output    : "%OUTPUT_DIR%"
echo [INFO] Config    : "%CONFIGURATION%"

if exist "%OUTPUT_DIR%" (
    echo [INFO] Cleaning output directory...
    rmdir /s /q "%OUTPUT_DIR%"
)

mkdir "%OUTPUT_DIR%" >nul 2>nul

echo [INFO] Restoring dependencies...
dotnet restore "%PROJECT_FILE%"
if errorlevel 1 (
    echo [ERROR] dotnet restore failed
    exit /b 1
)

echo [INFO] Publishing Luban...
dotnet publish "%PROJECT_FILE%" -c %CONFIGURATION% -o "%OUTPUT_DIR%" --self-contained false
if errorlevel 1 (
    echo [ERROR] dotnet publish failed
    exit /b 1
)

echo.
echo [SUCCESS] Build completed.
if exist "%OUTPUT_DIR%\Luban.exe" (
    echo [INFO] Executable : "%OUTPUT_DIR%\Luban.exe"
)
if exist "%OUTPUT_DIR%\Luban.dll" (
    echo [INFO] DLL entry   : "%OUTPUT_DIR%\Luban.dll"
    echo [INFO] Run with    : dotnet "%OUTPUT_DIR%\Luban.dll" --help
)

endlocal
exit /b 0
