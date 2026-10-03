#!/usr/bin/env pwsh
# =============================================================
# 部署前 schema 闸门 (2026-10-03, 来源 .ai/suggestions.md 2026-10-03 P1 建议)
#
# WHY 需要本脚本: CI 每次都在**全新空库**上跑 dotnet ef database update + 全部
#   backend/migrations/*.sql, 因此「代码里的新迁移」在 CI 一定成功 —— CI 全绿
#   **无法**证明生产库具备这些表/列。已经发生两次同类事故:
#     - 事故1: 实体声明了 machine_mr1_bindings 表, 但迁移脚本未生成 → 生产端点 42P01
#     - 事故2: 合并 master 后生产 __sakura_migrations 只到 038, 缺 026-029
#              → cross_references.is_whitelisted / product_images.show_dimension 列缺失 (42703)
#   本脚本在部署**前**把「目录里的迁移」与「目标库实际结构」做机械比对, 缺失即非零退出。
#
# 检查项:
#   1) 迁移登记完整性: backend/migrations/*.sql 的 basename 是否都已登记在
#      public.__sakura_migrations (与 backend/migrations/run-migrations.sh、
#      scripts/migrate.sh 使用同一张表) —— 缺任一个即 FAIL
#   2) DDL 落地: 迁移脚本中的 CREATE TABLE / ADD COLUMN 是否在目标库
#      information_schema 中真实存在 (同一脚本内被 DROP COLUMN 的列除外) —— 缺即 FAIL
#
# 用法:
#   powershell -File scripts/preflight-schema-check.ps1                 # 生产默认 (sakura-postgres/sakurafilter)
#   powershell -File scripts/preflight-schema-check.ps1 -Database sakurafilter_int_tests
#   powershell -File scripts/preflight-schema-check.ps1 -SkipDdlCheck   # 仅查迁移登记完整性
#
# 退出码: 0 = 通过 (可部署); 1 = 存在缺失 (禁止部署)
# 前置: 目标 postgres 容器运行中; 默认从 .env.prod 读 POSTGRES_DB / POSTGRES_USER (与 migrate.sh 同源)
# =============================================================
[CmdletBinding()]
param(
    [string]$Container = 'sakura-postgres',
    [string]$Database = '',
    [string]$User = '',
    [string]$MigrationsDir = '',
    [switch]$SkipDdlCheck
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, '..'))
if ([string]::IsNullOrWhiteSpace($MigrationsDir)) {
    $MigrationsDir = [System.IO.Path]::Combine($repoRoot, 'backend', 'migrations')
}

# --- 默认库名/用户: 从 .env.prod 读取 (单一来源, 与 scripts/migrate.sh 一致) ---
$envFile = [System.IO.Path]::Combine($repoRoot, '.env.prod')
if (([string]::IsNullOrWhiteSpace($Database)) -or ([string]::IsNullOrWhiteSpace($User))) {
    if (Test-Path $envFile) {
        $envText = Get-Content -Path $envFile -Encoding UTF8
        if ([string]::IsNullOrWhiteSpace($Database)) {
            $m = $envText | Select-String -Pattern '^POSTGRES_DB=(.+)$' | Select-Object -First 1
            if ($m) { $Database = $m.Matches[0].Groups[1].Value.Trim().Trim('"') }
        }
        if ([string]::IsNullOrWhiteSpace($User)) {
            $m = $envText | Select-String -Pattern '^POSTGRES_USER=(.+)$' | Select-Object -First 1
            if ($m) { $User = $m.Matches[0].Groups[1].Value.Trim().Trim('"') }
        }
    }
}
if ([string]::IsNullOrWhiteSpace($Database)) { $Database = 'sakurafilter' }
if ([string]::IsNullOrWhiteSpace($User)) { $User = 'postgres' }

# --- 前置: 容器运行中 ---
$running = @(& docker ps --format '{{.Names}}')
if ($running -notcontains $Container) {
    Write-Host "[FAIL] postgres 容器 $Container 未运行 — 请先 docker compose up -d postgres"
    exit 1
}

# 查询辅助: -t -A 输出裸值; 失败即抛 (不静默继续, 避免"查不到=通过"的假绿)
function Invoke-Psql([string]$sql) {
    $out = & docker exec -i $Container psql -U $User -d $Database -t -A -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql 执行失败: $sql`n$out"
    }
    return @($out | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

Write-Host "==> 部署前 schema 闸门: 容器=$Container 库=$Database 用户=$User"

# --- 迁移历史表存在性 (缺失说明连错库/环境未初始化, 直接中止) ---
$hasHist = (Invoke-Psql "SELECT to_regclass('public.__sakura_migrations') IS NOT NULL;") -join ''
if ($hasHist -ne 't') {
    Write-Host "[FAIL] 目标库缺 public.__sakura_migrations — 库未初始化或连错库 (禁止继续)"
    exit 1
}

$failed = $false

# --- 检查 1: 迁移登记完整性 ---
$dbApplied = @(Invoke-Psql 'SELECT basename FROM public.__sakura_migrations;')
$dirFiles = @(Get-ChildItem -Path $MigrationsDir -Filter '*.sql' -File | Sort-Object Name)
if ($dirFiles.Count -eq 0) {
    Write-Host "[FAIL] 未找到迁移脚本: $MigrationsDir"
    exit 1
}
$missingMigrations = @($dirFiles.Name | Where-Object { $dbApplied -notcontains $_ })
if ($missingMigrations.Count -gt 0) {
    Write-Host "[FAIL] 以下迁移未登记到 __sakura_migrations (生产未应用该迁移):"
    $missingMigrations | ForEach-Object { Write-Host "         - $_" }
    $failed = $true
}
else {
    Write-Host "    [OK] 迁移登记完整 ($($dirFiles.Count)/$($dirFiles.Count) 已在 __sakura_migrations)"
}

# --- 检查 2: 迁移声明的表/列在目标库真实存在 ---
if (-not $SkipDdlCheck) {
    $expectTables = New-Object System.Collections.Generic.List[string]
    $expectColumns = New-Object System.Collections.Generic.List[string]

    foreach ($f in $dirFiles) {
        $text = Get-Content -Path $f.FullName -Raw -Encoding UTF8

        # 本文件内被 DROP COLUMN 的列不再期望存在 (避免历史迁移的"加了又删"造成误报)
        $dropped = New-Object System.Collections.Generic.HashSet[string]
        foreach ($dm in [regex]::Matches($text, '(?im)DROP\s+COLUMN\s+(?:IF\s+EXISTS\s+)?([A-Za-z_]\w*)')) {
            [void]$dropped.Add($dm.Groups[1].Value.ToLowerInvariant())
        }

        # CREATE TABLE [IF NOT EXISTS] <a>[.<b>]  (无限定符时归入 public)
        foreach ($m in [regex]::Matches($text, '(?im)CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z_]\w*)(?:\s*\.\s*([A-Za-z_]\w*))?')) {
            if ($m.Groups[2].Success) { $key = "$($m.Groups[1].Value).$($m.Groups[2].Value)" }
            else { $key = "public.$($m.Groups[1].Value)" }
            if (-not $expectTables.Contains($key)) { $expectTables.Add($key) }
        }

        # ALTER TABLE <a>[.<b>] <语句体>;  → 语句体内所有 ADD COLUMN <col>
        foreach ($am in [regex]::Matches($text, '(?ism)ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?([A-Za-z_]\w*)(?:\s*\.\s*([A-Za-z_]\w*))?(.*?);')) {
            if ($am.Groups[2].Success) { $tblKey = "$($am.Groups[1].Value).$($am.Groups[2].Value)" }
            else { $tblKey = "public.$($am.Groups[1].Value)" }
            $body = $am.Groups[3].Value
            foreach ($cm in [regex]::Matches($body, '(?im)ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z_]\w*)')) {
                $col = $cm.Groups[1].Value
                if ($dropped.Contains($col.ToLowerInvariant())) { continue }
                $ck = "$tblKey.$col"
                if (-not $expectColumns.Contains($ck)) { $expectColumns.Add($ck) }
            }
        }
    }

    # 表存在性
    $missingTables = @()
    foreach ($t in $expectTables) {
        $parts = $t.Split('.')
        $sql = "SELECT 1 FROM information_schema.tables WHERE table_schema='$($parts[0])' AND table_name='$($parts[1])';"
        if ((Invoke-Psql $sql) -notcontains '1') { $missingTables += $t }
    }
    if ($missingTables.Count -gt 0) {
        Write-Host "[FAIL] 以下表在目标库不存在 (迁移声明的 DDL 未落地):"
        $missingTables | ForEach-Object { Write-Host "         - $_" }
        $failed = $true
    }
    else {
        Write-Host "    [OK] 迁移声明的表全部存在 ($($expectTables.Count) 张)"
    }

    # 列存在性
    $missingColumns = @()
    foreach ($c in $expectColumns) {
        $parts = $c.Split('.')
        $sql = "SELECT 1 FROM information_schema.columns WHERE table_schema='$($parts[0])' AND table_name='$($parts[1])' AND column_name='$($parts[2])';"
        if ((Invoke-Psql $sql) -notcontains '1') { $missingColumns += $c }
    }
    if ($missingColumns.Count -gt 0) {
        Write-Host "[FAIL] 以下列在目标库不存在 (实体/查询会报 42703 column does not exist):"
        $missingColumns | ForEach-Object { Write-Host "         - $_" }
        $failed = $true
    }
    else {
        Write-Host "    [OK] 迁移声明的列全部存在 ($($expectColumns.Count) 列)"
    }
}
else {
    Write-Host "    [SKIP] -SkipDdlCheck: 已跳过 DDL 落地检查"
}

if ($failed) {
    Write-Host '[FAIL] 部署前 schema 闸门未通过 — 禁止部署; 补应用缺失迁移后重跑本脚本'
    exit 1
}

Write-Host '[PASS] 部署前 schema 闸门通过'
exit 0
