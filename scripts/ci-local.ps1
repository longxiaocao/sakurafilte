# ============================================================================
# 本地 CI 一键验证 (pre-push gate)
#   SakuraFilter — push 前本地快速验证, 减少 GitHub CI 失败轮次
#
# 用法:
#   .\scripts\ci-local.ps1              # 全量验证 (后端 build+单测 → 前端 type-check+单测+build)
#   .\scripts\ci-local.ps1 -InstallHook # 安装为 git pre-push hook (推送前自动跑)
#   .\scripts\ci-local.ps1 -Fast        # 跳过前端 build (快速模式, 约 3 分钟)
#
# 退出码: 0 = 全部通过 | 1 = 有失败 (pre-push hook 依赖)
# 依赖: dotnet 8, Node 20+, PowerShell 5.1+
# ============================================================================
param(
    [switch]$InstallHook,
    [switch]$Fast
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$passCount = 0
$failCount = 0
$failNames = @()

function Run-Step {
    param([string]$Name, [scriptblock]$Body)
    Write-Host "`n===== $Name =====" -ForegroundColor Cyan
    $stepSw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Body
        if ($LASTEXITCODE -ne 0) { throw "exit code = $LASTEXITCODE" }
        Write-Host ("  PASS: {0} ({1}s)" -f $Name, [math]::Round($stepSw.Elapsed.TotalSeconds, 1)) -ForegroundColor Green
        return @{ ok = $true; name = $Name }
    } catch {
        Write-Host ("  FAIL: {0} — {1}" -f $Name, $_.Exception.Message) -ForegroundColor Red
        return @{ ok = $false; name = $Name }
    }
}

# ---------- 1. 后端 ----------
$r1 = Run-Step "后端 dotnet build (SakuraFilter.sln)" {
    Push-Location "$root/backend"
    dotnet build SakuraFilter.sln --nologo
    Pop-Location
}

$r2 = Run-Step "后端单测 (SakuraFilter.Api.Tests)" {
    Push-Location "$root/backend"
    dotnet test tests/SakuraFilter.Api.Tests/SakuraFilter.Api.Tests.csproj -c Release --nologo
    Pop-Location
}

# ---------- 2. 前端 ----------
$r3 = Run-Step "前端 vue-tsc 类型检查" {
    Push-Location "$root/frontend"
    npm run type-check
    Pop-Location
}

$r4 = Run-Step "前端单测 (unit, 无需后端)" {
    Push-Location "$root/frontend"
    npx vitest run tests/unit/
    Pop-Location
}

# 契约测试需后端运行态 (调用 /api/admin/dict/_schema 等真实端点)
$backendUp = $false
try {
    $resp = Invoke-WebRequest -Uri "http://localhost:5148/api/etl/status" -Headers @{ "X-Admin-Token" = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C" } -TimeoutSec 5 -SkipHttpErrorCheck -UseBasicParsing
    $backendUp = ($resp.StatusCode -eq 200)
} catch { $backendUp = $false }
if ($backendUp) {
    $r5 = Run-Step "前端契约测试 (contract, 需后端)" {
        Push-Location "$root/frontend"
        npx vitest run tests/contract/
        Pop-Location
    }
} else {
    Write-Host "`n  跳过契约测试: 需后端运行态 (localhost:5148 未就绪) — 可忽略, 或启动后端后重跑" -ForegroundColor Yellow
    $r5 = @{ ok = $true; name = "契约测试 (跳过, 需后端)" }
}

if (-not $Fast) {
    $r6 = Run-Step "前端生产构建 (vite build)" {
        Push-Location "$root/frontend"
        npm run build
        Pop-Location
    }
}

# ---------- 汇总 ----------
$results = @($r1, $r2, $r3, $r4, $r5) + $(if (-not $Fast) { @($r6) } else { @() })
$passCount = @($results | Where-Object { $_.ok }).Count
$failResults = @($results | Where-Object { -not $_.ok })
$failCount = $failResults.Count
$sw.Stop()
$minutes = [math]::Round($sw.Elapsed.TotalMinutes, 1)
Write-Host "`n================================================================"
if ($failCount -eq 0) {
    Write-Host ("  全部通过 ({0} 项, 耗时 {1} 分钟) — 可以推送" -f $passCount, $minutes) -ForegroundColor Green
} else {
    Write-Host ("  失败 {0} 项 / 通过 {1} 项 (耗时 {2} 分钟)" -f $failCount, $passCount, $minutes) -ForegroundColor Red
    foreach ($f in $failResults) { Write-Host "    ✗ $($f.name)" -ForegroundColor Red }
    Write-Host "  修复后重跑本脚本, 全部通过再推送" -ForegroundColor Yellow
}
Write-Host "================================================================"

# ---------- 安装 pre-push hook (可选) ----------
if ($InstallHook) {
    $hookDir = Join-Path $root ".git\hooks"
    $hook = Join-Path $hookDir "pre-push"
    $hookContent = @"
#!/bin/sh
# ci-local pre-push hook (自动生成, 由 scripts/ci-local.ps1 -InstallHook 安装)
echo "[ci-local] 推送前本地验证 (后端 build+单测 / 前端 type-check+单测+build)..."
cd "$(git rev-parse --show-toplevel)"
pwsh -NoProfile -File scripts/ci-local.ps1
if [ $? -ne 0 ]; then
    echo "[ci-local] 本地验证失败, 已阻止推送 — 修复后重试 (跳过: git push --no-verify)"
    exit 1
fi
exit 0
"@
    if (-not (Test-Path $hookDir)) { New-Item -ItemType Directory -Force -Path $hookDir | Out-Null }
    Set-Content -Path $hook -Value $hookContent -Encoding UTF8
    Write-Host "`n已安装 pre-push hook: $hook (验证失败将阻止推送; 紧急跳过: git push --no-verify)" -ForegroundColor Green
}

exit $(if ($failCount -eq 0) { 0 } else { 1 })
