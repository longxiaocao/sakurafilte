# SakuraFilter 一键停止脚本 (Windows / PowerShell 5+ / 7)
# 作用: 停止后端 dotnet 进程 + 前端 node 进程 (仅停止 SakuraFilter 项目的)
# 用法 (项目根目录):
#   powershell -ExecutionPolicy Bypass -File .\scripts\dev-down.ps1
[CmdletBinding()]
param()

$ErrorActionPreference = 'SilentlyContinue'

# 跨 PS5/7 兼容的进程命令行获取
function Get-ProcessCommandLine($pid) {
    $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$pid" -ErrorAction SilentlyContinue).CommandLine
    if (-not $cmd) {
        $cmd = (Get-WmiObject Win32_Process -Filter "ProcessId=$pid" -ErrorAction SilentlyContinue).CommandLine
    }
    return $cmd
}

Write-Host "[1/3] Stopping backend (SakuraFilter.Api) ..." -ForegroundColor Cyan
$backendKilled = 0
# 1) 按进程名直接 Stop (最可靠, 不依赖 tasklist CSV 格式)
Get-Process -Name 'SakuraFilter.Api' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  Stop PID $($_.Id)" -ForegroundColor Gray
    Stop-Process -Id $_.Id -Force
    $backendKilled++
}
# 2) 兜底: dotnet.exe 进程命令行包含 SakuraFilter.Api
$tasklistOut = tasklist /FO CSV /NH 2>&1
foreach ($line in $tasklistOut) {
    if ($line -match '^"dotnet\.exe","(\d+)"') {
        $pid = [int]$Matches[1]
        $cmd = Get-ProcessCommandLine $pid
        if ($cmd -and $cmd -match 'SakuraFilter\.Api') {
            Write-Host "  Stop dotnet PID $pid" -ForegroundColor Gray
            Stop-Process -Id $pid -Force
            $backendKilled++
        }
    }
}
Write-Host "  Killed $backendKilled backend process(es)" -ForegroundColor Gray

Write-Host "[2/3] Stopping frontend (node, vite/npm) ..." -ForegroundColor Cyan
$frontendKilled = 0
# 用 CIM 直接枚举 node.exe, 检查命令行是否包含 vite/npm (SakuraFilter 项目)
$cimNode = Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue
foreach ($p in $cimNode) {
    $cmd = $p.CommandLine
    # 排除其他项目: 检查路径中包含 frontend 或 工作目录为我们的 frontend
    if ($cmd -and ($cmd -match 'vite' -or $cmd -match 'npm.*run.*dev' -or $cmd -match 'npm-cli\.js')) {
        # 进一步: 工作目录在 sakurafilter
        $cwd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.ProcessId)" -ErrorAction SilentlyContinue).CommandLine
        # 简化: 检查命令行是否包含 sakurafilter 路径
        if ($cmd -match 'sakurafilter') {
            Write-Host "  Stop PID $($p.ProcessId)" -ForegroundColor Gray
            Stop-Process -Id $p.ProcessId -Force
            $frontendKilled++
        }
    }
}
Write-Host "  Killed $frontendKilled frontend process(es)" -ForegroundColor Gray

Write-Host "[3/3] Port status:" -ForegroundColor Cyan
foreach ($port in @(5000, 5148, 5150, 5173, 5174, 5175, 5180)) {
    $conn = netstat -ano | Select-String ":$port\s" | Select-Object -First 1
    if ($conn -and $conn -match '\s(\d+)$') {
        Write-Host "  :$port  still in use (PID $($Matches[1]))" -ForegroundColor Yellow
    } else {
        Write-Host "  :$port  free" -ForegroundColor Gray
    }
}

# Cleanup temp runners
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Remove-Item (Join-Path $root 'scripts\_run-backend.ps1') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'scripts\_run-frontend.ps1') -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done." -ForegroundColor Green
