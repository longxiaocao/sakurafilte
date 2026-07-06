@echo off
REM SakuraFilter one-click startup (Windows double-click entry)
REM   Usage 1: Double-click dev-up.bat
REM              -> default: backend 5148, frontend 5175
REM   Usage 2: dev-up.bat 5180
REM              -> frontend=5180, backend=5148
REM   Usage 3: dev-up.bat 5180 5150
REM              -> frontend=5180, backend=5150
REM   Usage 4: set REBUILD=1 && dev-up.bat
REM   Usage 5: set SKIP_BACKEND=1 && dev-up.bat
REM   Usage 6: set SKIP_FRONTEND=1 && dev-up.bat

setlocal EnableExtensions

cd /d "%~dp0\.." || (echo [ERROR] cannot cd to project root & pause & exit /b 1)

REM ===== Default ports =====
if not defined FRONTEND_PORT set "FRONTEND_PORT=5175"
if not defined BACKEND_PORT  set "BACKEND_PORT=5148"

REM ===== Positional args (override env vars) =====
if not "%~1"=="" set "FRONTEND_PORT=%~1"
if not "%~2"=="" set "BACKEND_PORT=%~2"

REM ===== Build PowerShell args =====
set "PS_ARGS=-FrontendPort %FRONTEND_PORT% -BackendPort %BACKEND_PORT%"
if defined REBUILD       set "PS_ARGS=%PS_ARGS% -Rebuild"
if defined SKIP_BACKEND  set "PS_ARGS=%PS_ARGS% -SkipBackend"
if defined SKIP_FRONTEND set "PS_ARGS=%PS_ARGS% -SkipFrontend"

echo.
echo ========================================
echo  SakuraFilter one-click startup
echo  Frontend port: %FRONTEND_PORT%
echo  Backend  port: %BACKEND_PORT%
echo  PS args:       %PS_ARGS%
echo ========================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-up.ps1" %PS_ARGS%

if errorlevel 1 (
    echo.
    echo [ERROR] startup failed, see logs above
    pause
)

endlocal & exit /b %errorlevel%
