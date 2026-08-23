@echo off
REM Day 9: 前端 dev server 启动脚本 (Windows)
REM 用法: 双击运行 或 命令行 .\start-dev.bat

setlocal

cd /d "%~dp0"

echo ========================================
echo  SakuraFilter Frontend (Vue 3 + Vite)
echo ========================================

REM 检查 node_modules
if not exist "node_modules" (
  echo [1/3] 安装依赖 (npm install)...
  call npm install
  if errorlevel 1 (
    echo [错误] npm install 失败
    pause
    exit /b 1
  )
) else (
  echo [1/3] 依赖已存在
)

echo [2/3] 类型检查 (可选, 跳过可减少启动时间)...
REM call npm run type-check
echo       (跳过, 如需手动执行: npm run type-check)

echo [3/3] 启动 Vite dev server (http://localhost:5173)...
echo       (如需终止: Ctrl+C)
echo.
call npm run dev

endlocal
