# Day 10+ P0.2: EF Core Migrations baseline 一键脚本 (Windows PowerShell)
#   用途: 在 spike_test_v3 数据库 (本地/CI) seed 4 个老 EF Core migration 记录,
#         让 EF Core Migrate 只跑新加的 migration, 避免 DROP/ALTER 失败.
#
# 前提条件:
#   1) PG 16+ 已启动, spike_test_v3 数据库已创建
#      - 默认连接: localhost:5432 / postgres / 784533
#   2) Python 3.10+ + psycopg2-binary 已安装
#      - pip install psycopg2-binary
#   3) 后端 (dotnet run) 未启动, 避免 EF Core Migrate 抢跑
#
# 退出码:
#   0 = baseline seed 成功
#   1 = baseline seed 参数错
#   2 = baseline seed DB 连接失败
#
# 后续步骤:
#   cd backend\src\SakuraFilter.Api
#   dotnet run -c Debug
#   # EF Core Migrate 会自动只跑未应用的 migration

[CmdletBinding()]
param(
    [string]$PgHost = "localhost",
    [int]$PgPort = 5432,
    [string]$PgDb = "spike_test_v3",
    [string]$PgUser = "postgres",
    [string]$PgPassword = "784533",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$BaselineScript = Join-Path $RepoRoot "spike-test\_ef_migrations_baseline.py"

if (-not (Test-Path $BaselineScript)) {
    Write-Host "[error] 找不到 baseline 脚本: $BaselineScript" -ForegroundColor Red
    exit 1
}

Write-Host "=== EF Core Migrations baseline seed (本地开发) ===" -ForegroundColor Cyan
Write-Host "Repo:   $RepoRoot"
Write-Host "Script: $BaselineScript"
Write-Host ""

$arguments = @(
    $BaselineScript
    "--pg-host=$PgHost"
    "--pg-port=$PgPort"
    "--pg-db=$PgDb"
    "--pg-user=$PgUser"
    "--pg-password=$PgPassword"
)
if ($DryRun) {
    $arguments += "--dry-run"
}

try {
    & python @arguments
    $exitCode = $LASTEXITCODE
}
catch {
    Write-Host "[error] 执行失败: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "OK baseline seed 成功. 现在可以 dotnet run 启动后端." -ForegroundColor Green
    Write-Host "  cd backend\src\SakuraFilter.Api"
    Write-Host "  dotnet run -c Debug"
} else {
    Write-Host "FAIL baseline seed 失败 (exit=$exitCode)" -ForegroundColor Red
    Write-Host "  退出码: 1=参数错, 2=DB 连接失败" -ForegroundColor Red
}

exit $exitCode
