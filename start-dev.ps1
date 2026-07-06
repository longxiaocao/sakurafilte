param(
  [switch]$Force,
  [switch]$NoStart
)

# start-dev.ps1
#   一键启动开发环境: 自动生成 .env (含 MinIO dev 默认凭证) + 加载 + 启后端
#   WHY: 避免新成员手动配置 9 个 MinIO 环境变量, 真正"克隆即跑"
#   注: 中文注释必须放在 param() 之后, PowerShell 5.1 在 param 之前的注释含中文标点会解析失败.
#
#   用法:
#     .\start-dev.ps1                # 启动 (缺 .env 时自动生成)
#     .\start-dev.ps1 -Force         # 强制重新生成 .env
#     .\start-dev.ps1 -NoStart       # 仅生成/校验 .env, 不启动后端

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path "$PSScriptRoot").Path
$EnvFile = Join-Path $RepoRoot '.env'
$EnvExample = Join-Path $RepoRoot '.env.example'

function Write-Step($n, $total, $msg) {
  Write-Host "[$n/$total] $msg" -ForegroundColor Yellow
}

# -------------------- 1. 检测/生成 .env --------------------
Write-Step 1 4 "检查 .env ..."
if ((Test-Path $EnvFile) -and -not $Force) {
  Write-Host "  .env 已存在, 跳过生成 (使用 -Force 重新生成)" -ForegroundColor Green
} else {
  if (-not (Test-Path $EnvExample)) {
    throw ".env.example 不存在: $EnvExample"
  }
  Write-Host "  从 .env.example 注入 dev 默认值..." -ForegroundColor Yellow
  $content = Get-Content $EnvExample -Raw
  # MinIO dev 默认值 (本地 docker-compose 端口 9000, 默认凭证 minioadmin/minioadmin)
  $replacements = @{
    '<minio_endpoint>'    = 'localhost'
    '<minio_access_key>'  = 'minioadmin'
    '<minio_secret_key>'  = 'minioadmin'
  }
  foreach ($k in $replacements.Keys) {
    $content = $content.Replace($k, $replacements[$k])
  }
  Set-Content -Path $EnvFile -Value $content -Encoding UTF8
  Write-Host "  生成完毕: $EnvFile" -ForegroundColor Green
  Write-Host "  MinIO dev 默认: localhost:9000, minioadmin/minioadmin" -ForegroundColor Green
}

if ($NoStart) {
  Write-Host ""
  Write-Host ".env 已就绪, -NoStart 跳过启动" -ForegroundColor Green
  exit 0
}

# -------------------- 2. 清理旧进程 --------------------
Write-Step 2 4 "清理旧后端进程..."
Get-Process -Name dotnet -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-5) } | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance -ClassName Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue | ForEach-Object {
  try {
    $cmd = $_.CommandLine
    if ($cmd -match 'SakuraFilter.Api' -or $cmd -match 'dotnet run') {
      Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
  } catch {}
}
Start-Sleep -Seconds 2

# -------------------- 3. 加载 .env --------------------
Write-Step 3 4 "加载 .env 环境变量..."
Get-Content $EnvFile | ForEach-Object {
  $line = $_.Trim()
  if ($line -eq '' -or $line.StartsWith('#')) { return }
  $idx = $line.IndexOf('=')
  if ($idx -lt 1) { return }
  $key = $line.Substring(0, $idx).Trim()
  $val = $line.Substring($idx + 1).Trim()
  if (($val.StartsWith('"') -and $val.EndsWith('"')) -or ($val.StartsWith("'") -and $val.EndsWith("'"))) {
    $val = $val.Substring(1, $val.Length - 2)
  }
  [System.Environment]::SetEnvironmentVariable($key, $val, 'Process')
}
Write-Host "  9 个 MinIO 变量已注入:" -ForegroundColor Green
Write-Host "    Minio__Endpoint, Minio__AccessKey, Minio__SecretKey, Minio__BucketName, Minio__PublicEndpoint," -ForegroundColor Gray
Write-Host "    MINIO_ACCESS_KEY, MINIO_SECRET_KEY, MINIO_PUBLIC_ENDPOINT, STORAGE_PROVIDER" -ForegroundColor Gray

# -------------------- 4. 启动后端 --------------------
Write-Step 4 4 "启动后端..."
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$apiDir = Join-Path $RepoRoot 'backend\src\SakuraFilter.Api'
Set-Location $apiDir
$logFile = Join-Path $RepoRoot 'backend.log'
$errFile = Join-Path $RepoRoot 'backend.err.log'
$proc = Start-Process -FilePath 'dotnet.exe' -ArgumentList 'run','--no-launch-profile','--urls','http://0.0.0.0:5148' -RedirectStandardOutput $logFile -RedirectStandardError $errFile -WindowStyle Hidden -PassThru
Write-Host "  Started PID=$($proc.Id)" -ForegroundColor Green

# 等待端口监听
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 2
  $conn = Get-NetTCPConnection -State Listen -LocalPort 5148 -ErrorAction SilentlyContinue
  if ($conn) {
    Write-Host "  Port 5148 LISTEN after $((($i+1)*2))s" -ForegroundColor Green
    $ready = $true
    break
  }
}
if (-not $ready) {
  Write-Host "  TIMEOUT - port 5148 not listening" -ForegroundColor Red
  Write-Host "=== LAST 30 LOG LINES ==="
  Get-Content $logFile -Tail 30
  Write-Host "=== LAST 10 ERR LINES ==="
  Get-Content $errFile -Tail 10
  exit 1
}

Write-Host ""
Write-Host "OK 开发环境已就绪" -ForegroundColor Green
Write-Host "   API:    http://localhost:5148" -ForegroundColor Gray
Write-Host "   前端:   cd frontend; npm run dev" -ForegroundColor Gray
Write-Host "   停止:   .\kill_stuck.ps1" -ForegroundColor Gray
