# V2 改进 2: 测试矩阵自动化脚本
# 用途: 一键执行 V2 全量测试套件, 减少手工串联
# 测试矩阵 (5 阶段, 按依赖顺序):
#   1. 后端单测 (dotnet test)
#   2. 前端单测 (vitest run)
#   3. 契约测试 (vitest run, 需后端运行)
#   4. E2E 测试 (playwright test, 需前后端运行)
#   5. 视觉回归 (playwright test --update-snapshots 首次, 后续对比)
#
# 用法:
#   .\scripts\run-v2-tests.ps1              # 默认全跑 (1-5)
#   .\scripts\run-v2-tests.ps1 -SkipE2E     # 跳过 E2E (无前后端运行环境)
#   .\scripts\run-v2-tests.ps1 -SkipVisual  # 跳过视觉回归
#   .\scripts\run-v2-tests.ps1 -BackendOnly # 仅后端单测
#   .\scripts\run-v2-tests.ps1 -FrontendOnly # 仅前端单测
#
# 退出码:
#   0 = 全部通过
#   非 0 = 至少一个阶段失败 (含具体阶段码)

[CmdletBinding()]
param(
    [switch]$SkipE2E,
    [switch]$SkipVisual,
    [switch]$SkipContract,
    [switch]$BackendOnly,
    [switch]$FrontendOnly,
    [switch]$UpdateVisualBaseline
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."

# 颜色输出 (Musk 风格: 黑白 + 单一强调色)
function Write-Stage($msg) { Write-Host "`n[STAGE] $msg" -ForegroundColor White }
function Write-Ok($msg)    { Write-Host "[OK]    $msg" -ForegroundColor Green }
function Write-Fail($msg)  { Write-Host "[FAIL]  $msg" -ForegroundColor Red }
function Write-Info($msg)  { Write-Host "[INFO]  $msg" -ForegroundColor DarkGray }

# 阶段结果追踪
$script:results = @{}
$script:exitCode = 0

function Invoke-Stage {
    param(
        [string]$Name,
        [scriptblock]$Action
    )
    Write-Stage "$Name"
    try {
        & $Action
        if ($LASTEXITCODE -eq 0) {
            $script:results[$Name] = "PASS"
            Write-Ok "$Name 通过"
        } else {
            $script:results[$Name] = "FAIL (exit=$LASTEXITCODE)"
            Write-Fail "$Name 失败 (exit=$LASTEXITCODE)"
            $script:exitCode = 1
        }
    } catch {
        $script:results[$Name] = "ERROR: $_"
        Write-Fail "$Name 异常: $_"
        $script:exitCode = 2
    }
}

# ===== 仅后端/仅前端模式快速返回 =====
if ($BackendOnly -and $FrontendOnly) {
    Write-Fail "-BackendOnly 与 -FrontendOnly 互斥"
    exit 3
}

# ===== 阶段 1: 后端单测 =====
if (-not $FrontendOnly) {
    Invoke-Stage "1.后端单测" {
        Push-Location "$root\backend"
        try {
            # 先编译, 失败立即终止避免无谓测试
            & dotnet build SakuraFilter.sln -c Debug --nologo 2>&1 | Select-String -Pattern "error|错误" | Select-Object -Last 5
            if ($LASTEXITCODE -ne 0) {
                Write-Fail "后端编译失败, 跳过测试"
                return
            }
            # 执行全部测试 (含 V2ValidatorTests + CursorHmacTests + XssSanitizerTests)
            & dotnet test tests\SakuraFilter.Api.Tests\SakuraFilter.Api.Tests.csproj --no-build --nologo 2>&1 | Select-Object -Last 10
        } finally {
            Pop-Location
        }
    }
}

# ===== 阶段 2: 前端单测 =====
if (-not $BackendOnly) {
    Invoke-Stage "2.前端单测" {
        Push-Location "$root\frontend"
        try {
            # 仅运行 unit 测试 (不依赖后端运行)
            & npx vitest run tests/unit/ 2>&1 | Select-Object -Last 10
        } finally {
            Pop-Location
        }
    }
}

# ===== 阶段 3: 契约测试 (需后端运行) =====
if (-not $BackendOnly -and -not $SkipContract) {
    Invoke-Stage "3.契约测试" {
        # 探测后端是否运行
        $backendUp = $false
        try {
            $resp = Invoke-WebRequest -Uri "http://localhost:5148/health" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            $backendUp = $resp.StatusCode -eq 200
        } catch { $backendUp = $false }

        if (-not $backendUp) {
            Write-Info "后端未运行 (http://localhost:5148/health 不可达), 跳过契约测试"
            $script:results["3.契约测试"] = "SKIP (backend down)"
            return
        }

        Push-Location "$root\frontend"
        try {
            & npx vitest run tests/contract/ 2>&1 | Select-Object -Last 10
        } finally {
            Pop-Location
        }
    }
}

# ===== 阶段 4: E2E 测试 (需前后端运行) =====
if (-not $BackendOnly -and -not $SkipE2E) {
    Invoke-Stage "4.E2E测试" {
        $frontendUp = $false
        try {
            $resp = Invoke-WebRequest -Uri "http://localhost:5173" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            $frontendUp = $resp.StatusCode -lt 500
        } catch { $frontendUp = $false }

        if (-not $frontendUp) {
            Write-Info "前端未运行 (http://localhost:5173 不可达), 跳过 E2E 测试"
            $script:results["4.E2E测试"] = "SKIP (frontend down)"
            return
        }

        Push-Location "$root\frontend"
        try {
            # 跑全部 E2E (含 v2-seo-redirect.spec.ts)
            & npx playwright test tests/e2e/ 2>&1 | Select-Object -Last 15
        } finally {
            Pop-Location
        }
    }
}

# ===== 阶段 5: 视觉回归 (需前后端运行) =====
if (-not $BackendOnly -and -not $SkipVisual) {
    Invoke-Stage "5.视觉回归" {
        $frontendUp = $false
        try {
            $resp = Invoke-WebRequest -Uri "http://localhost:5173" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            $frontendUp = $resp.StatusCode -lt 500
        } catch { $frontendUp = $false }

        if (-not $frontendUp) {
            Write-Info "前端未运行, 跳过视觉回归"
            $script:results["5.视觉回归"] = "SKIP (frontend down)"
            return
        }

        Push-Location "$root\frontend"
        try {
            if ($UpdateVisualBaseline) {
                Write-Info "重置视觉基线模式 (--UpdateVisualBaseline)"
                & npx playwright test tests/visual/ --update-snapshots 2>&1 | Select-Object -Last 10
            } else {
                & npx playwright test tests/visual/ 2>&1 | Select-Object -Last 10
            }
        } finally {
            Pop-Location
        }
    }
}

# ===== 汇总报告 =====
Write-Stage "测试矩阵汇总"
$script:results.GetEnumerator() | Sort-Object Name | ForEach-Object {
    $status = $_.Value
    if ($status -like "PASS*") {
        Write-Ok "$($_.Name): $status"
    } elseif ($status -like "SKIP*") {
        Write-Info "$($_.Name): $status"
    } else {
        Write-Fail "$($_.Name): $status"
    }
}

Write-Host ""
if ($script:exitCode -eq 0) {
    Write-Ok "V2 测试矩阵全部通过"
} else {
    Write-Fail "V2 测试矩阵存在失败项 (exit=$script:exitCode)"
}
exit $script:exitCode
