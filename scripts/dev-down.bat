@echo off
REM SakuraFilter one-click shutdown (Windows double-click entry)
REM   Kills backend (SakuraFilter.Api) and frontend (vite/npm under sakurafilter)

setlocal EnableExtensions

cd /d "%~dp0\.." || (echo [ERROR] cannot cd to project root & pause & exit /b 1)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-down.ps1"

if errorlevel 1 pause
endlocal & exit /b %errorlevel%
