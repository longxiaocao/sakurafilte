#!/usr/bin/env pwsh
# 校验 backend/migrations/*.sql 的数字前缀全局唯一 (2026-10-03)
#
# 用法: powershell -File scripts/check-migration-uniqueness.ps1   (Windows PowerShell 5.1 兼容)
#       pwsh -File scripts/check-migration-uniqueness.ps1          (PowerShell 7+)
# 退出码: 0 = 通过; 1 = 存在重复编号或文件名不合约定
#
# WHY: 迁移脚本按「文件名升序」逐个执行, 编号重复会让顺序语义不确定;
#   新增脚本时也容易取到已被占用的号 (历史上 020_* 与 026_* 各有 2 个同号文件)。
#   CI (ci.yml 迁移步骤) 内含等价的 bash 检查, 本脚本供本地/Windows 开发者使用。
[CmdletBinding()]
param(
    [string]$MigrationsDir = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MigrationsDir)) {
    # 兼容 PS 5.1: Join-Path 仅接受 2 个路径参数, 故用 Path.Combine
    $MigrationsDir = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($PSScriptRoot, '..', 'backend', 'migrations'))
}

$files = @(Get-ChildItem -Path $MigrationsDir -Filter '*.sql' -File | Sort-Object Name)
if ($files.Count -eq 0) {
    Write-Host "[FAIL] 未找到任何迁移脚本: $MigrationsDir"
    exit 1
}

$byNumber = @{}
foreach ($f in $files) {
    if ($f.Name -match '^(\d{3})_') {
        $num = $Matches[1]
        if (-not $byNumber.ContainsKey($num)) { $byNumber[$num] = @() }
        $byNumber[$num] += $f.Name
    }
    else {
        Write-Host "[FAIL] 文件名不符合 NNN_描述.sql 约定: $($f.Name)"
        exit 1
    }
}

$dupes = $byNumber.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 } | Sort-Object Name
if ($dupes) {
    foreach ($d in $dupes) {
        Write-Host "[FAIL] 编号 $($d.Name) 被 $($d.Value.Count) 个文件重复使用:"
        $d.Value | ForEach-Object { Write-Host "         - $_" }
    }
    Write-Host '[FAIL] 迁移编号必须全局唯一, 取号规则见 docs/ef-migrations-baseline.md §3.5'
    exit 1
}

$maxNum = ($byNumber.Keys | Sort-Object -Property { [int]$_ } -Descending)[0]
Write-Host "[PASS] 迁移编号全局唯一: $($files.Count) 个脚本, 最大编号 $maxNum"
exit 0
