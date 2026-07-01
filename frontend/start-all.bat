@echo off
REM Day 9: 前后端一键启动 (Windows)
REM   1. 后端: dotnet run (http://localhost:5000)
REM   2. 前端: Vite dev server (http://localhost:5173)
REM   在新窗口分别启动, 关闭任一窗口不影响另一个

setlocal
cd /d "%~dp0\.."

echo ========================================
echo  SakuraFilter 一键启动 (Backend + Frontend)
echo ========================================

REM ===== 1. 启动后端 =====
echo.
echo [1/2] 启动后端 (新窗口)...
start "SakuraFilter Backend" cmd /k "cd /d %~dp0\..\backend\src\SakuraFilter.Api && echo [Backend] 启动中... && dotnet run --no-launch-profile"

REM ===== 2. 启动前端 =====
echo [2/2] 启动前端 (新窗口)...
start "SakuraFilter Frontend" cmd /k "cd /d %~dp0\..\frontend && echo [Frontend] 启动中... && call start-dev.bat"

echo.
echo ========================================
echo  两个窗口已弹出
echo  Backend  http://localhost:5000
echo  Frontend http://localhost:5173
echo  Swagger  http://localhost:5000/swagger
echo  按任意键关闭启动器 (不关闭已启动的服务)
echo ========================================
pause > nul

endlocal
