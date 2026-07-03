# 改进 3: GFW push 兜底脚本
# WHY: GitHub HTTPS (github.com:443) 间歇性被 GFW 阻断, 30 次重试全失败
#      SSH (git@github.com:22) 通道通常可用, 优先走 SSH
#      不修改 git config (用户规则: NEVER update the git config)
#
# 使用:
#   .\push.ps1              # 默认 push origin master
#   .\push.ps1 main         # push origin main
#   .\push.ps1 master 60    # push origin master, 最多 60 次重试
#
# 策略:
#   1. 检查 SSH key 是否存在 (~/.ssh/id_rsa 或 id_ed25519)
#   2. 若有 SSH key: 先尝试 git@github.com:USER/REPO.git (SSH)
#   3. SSH 失败或无 key: 回退 HTTPS, 循环重试 (默认 30 次, 8s 间隔)
#   4. 成功后恢复原 remote (若被临时修改)

param(
    [string]$Branch = "master",
    [int]$MaxRetries = 30,
    [int]$IntervalSec = 8
)

$ErrorActionPreference = "Continue"
$originalRemote = (git remote get-url origin 2>$null)
if (-not $originalRemote) {
    Write-Host "[FAIL] 未找到 origin remote" -ForegroundColor Red
    exit 1
}

Write-Host "===== GFW push 兜底脚本 =====" -ForegroundColor Cyan
Write-Host "分支: $Branch"
Write-Host "原 remote: $originalRemote"
Write-Host "最大重试: $MaxRetries 次 (间隔 ${IntervalSec}s)"
Write-Host ""

# 步骤 1: 检查 SSH key
$sshKey = $null
$sshKeys = @("$env:USERPROFILE\.ssh\id_ed25519", "$env:USERPROFILE\.ssh\id_rsa")
foreach ($k in $sshKeys) {
    if (Test-Path $k) {
        $sshKey = $k
        break
    }
}

# 步骤 2: 若有 SSH key, 先尝试 SSH push
if ($sshKey) {
    Write-Host "[SSH] 检测到 SSH key: $sshKey" -ForegroundColor Green
    # 从 HTTPS remote 提取 user/repo
    # https://github.com/USER/REPO.git -> git@github.com:USER/REPO.git
    if ($originalRemote -match "github.com[:/]([^/]+)/([^/]+?)(\.git)?$") {
        $user = $matches[1]
        $repo = $matches[2]
        $sshRemote = "git@github.com:${user}/${repo}.git"
        Write-Host "[SSH] 尝试 SSH push: $sshRemote"

        # 临时切换 remote (不改 config, 用 push url)
        Write-Host "git push $sshRemote $Branch"
        $sshResult = git push $sshRemote $Branch 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "[SSH] push 成功!" -ForegroundColor Green
            Write-Host $sshResult
            exit 0
        } else {
            Write-Host "[SSH] push 失败, 回退 HTTPS" -ForegroundColor Yellow
            Write-Host $sshResult
        }
    } else {
        Write-Host "[SSH] remote 不是 GitHub, 跳过 SSH" -ForegroundColor Yellow
    }
} else {
    Write-Host "[SSH] 未检测到 SSH key, 直接走 HTTPS 重试" -ForegroundColor Yellow
    Write-Host "  (生成 SSH key: ssh-keygen -t ed25519 -C 'email@example.com')"
    Write-Host "  (添加到 GitHub: https://github.com/settings/keys)"
}

# 步骤 3: HTTPS 重试循环
Write-Host ""
Write-Host "[HTTPS] 开始重试 (最多 $MaxRetries 次)..." -ForegroundColor Cyan
$ok = $false
for ($i = 1; $i -le $MaxRetries; $i++) {
    Write-Host "  try $i / $MaxRetries ..." -NoNewline
    $result = git push origin $Branch 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host " 成功!" -ForegroundColor Green
        $ok = $true
        break
    } else {
        $errShort = ($result | Select-Object -First 1).Trim()
        if ($errShort.Length -gt 80) { $errShort = $errShort.Substring(0, 80) + "..." }
        Write-Host " 失败 ($errShort)" -ForegroundColor Red
        if ($i -lt $MaxRetries) {
            Start-Sleep -Seconds $IntervalSec
        }
    }
}

if ($ok) {
    Write-Host ""
    Write-Host "===== push 成功 =====" -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "===== push 失败 ($MaxRetries 次重试全失败) =====" -ForegroundColor Red
    Write-Host "建议:"
    Write-Host "  1. 配置 SSH key (ssh-keygen -t ed25519)"
    Write-Host "  2. 用代理: git config --global http.proxy http://127.0.0.1:7890"
    Write-Host "  3. 等待 GFW 恢复 (通常几小时后可用)"
    Write-Host "  4. 用 GitHub Desktop 或浏览器上传"
    exit 1
}
