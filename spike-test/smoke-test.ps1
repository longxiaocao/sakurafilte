# SakuraFilter E2E 冒烟测试 (纯 PowerShell 5.1)
$ErrorActionPreference = "Continue"
$base = "http://localhost:5148"
$adminToken = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
$reportLines = @()

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        [string]$Body = "",
        [hashtable]$Headers = @{}
    )
    # 构建 curl 参数
    $argList = @("-s", "-o", "$env:TEMP\_body.txt", "-w", "%{http_code}", "-X", $Method, "$base$Path")
    foreach ($k in $Headers.Keys) {
        $argList += @("-H", "$($k): $($Headers[$k])")
    }
    if ($Body) {
        # 写 body 到临时文件,curl 用 -d @file 引用
        $bodyFile = "$env:TEMP\_smoke_body.json"
        [System.IO.File]::WriteAllText($bodyFile, $Body, [System.Text.Encoding]::UTF8)
        $argList += @("-H", "Content-Type: application/json", "-d", "@$bodyFile")
    }
    # 启动 curl 进程并捕获 stdout
    $pinfo = New-Object System.Diagnostics.ProcessStartInfo
    $pinfo.FileName = "curl.exe"
    $pinfo.UseShellExecute = $false
    $pinfo.RedirectStandardOutput = $true
    $pinfo.RedirectStandardError = $true
    # 用单字符串参数 (PS 5.1 兼容: 手动 quoting)
    $quoted = @()
    foreach ($a in $argList) {
        if ($a -match '\s') { $quoted += "`"$a`"" } else { $quoted += $a }
    }
    $pinfo.Arguments = [string]::Join(" ", $quoted)
    $p = [System.Diagnostics.Process]::Start($pinfo)
    $stdout = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit()
    $code = $stdout.Trim()
    $body = ""
    if (Test-Path "$env:TEMP\_body.txt") { $body = Get-Content "$env:TEMP\_body.txt" -Raw }
    $shortBody = ($body -replace "`r`n", " ").Trim()
    if ($shortBody.Length -gt 300) { $shortBody = $shortBody.Substring(0, 300) + "..." }
    $script:reportLines += "### $Name"
    $script:reportLines += "- $Method $Path -> HTTP **$code**"
    if ($shortBody) { $script:reportLines += "- body: " + $shortBody }
    $script:reportLines += ""
    return @{ Code = $code; Body = $body }
}

$reportLines += "# SakuraFilter E2E Smoke Test Report"
$reportLines += "- Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$reportLines += "- Backend: dotnet run (refactored), listening on $base"
$reportLines += ""

# 1. 基础探活
$r = Test-Endpoint "Root" "GET" "/"
$r = Test-Endpoint "Liveness" "GET" "/health/live"
$r = Test-Endpoint "Readiness" "GET" "/health/ready"
$r = Test-Endpoint "Metrics" "GET" "/metrics"

# 2. 公开搜索
$r = Test-Endpoint "Search health" "GET" "/api/search/health"
$r = Test-Endpoint "Public search" "POST" "/api/search" '{"q":"","page":1,"pageSize":3}'

# 3. 性能上报
$r = Test-Endpoint "Perf ingest (empty, expect 400)" "POST" "/api/perf/ingest" '{"samples":[]}'
$r = Test-Endpoint "Perf ingest batch" "POST" "/api/perf/ingest" '{"samples":[{"path":"/","method":"GET","statusCode":200,"durationMs":12.3,"ts":"2026-07-06T00:00:00Z"}]}'

# 4. 认证
$r = Test-Endpoint "Login admin" "POST" "/api/auth/login" '{"username":"admin","password":"Admin@2026"}'
$token = ""
try {
    $j = $r.Body | ConvertFrom-Json
    $token = if ($j.accessToken) { $j.accessToken } elseif ($j.token) { $j.token } else { "" }
} catch {}
$reportLines += "JWT token length: $($token.Length)"
$reportLines += ""

# 5. 鉴权保护
$r = Test-Endpoint "No token admin (expect 401)" "GET" "/api/admin/products/"
$r = Test-Endpoint "JWT products" "GET" "/api/admin/products/?page=1&pageSize=3" "" @{ "Authorization" = "Bearer $token" }
$r = Test-Endpoint "X-Admin-Token auth status" "GET" "/api/admin/auth/status" "" @{ "X-Admin-Token" = $adminToken }

# 6. 字典
$r = Test-Endpoint "Dict schema contract" "GET" "/api/admin/dict/_schema" "" @{ "Authorization" = "Bearer $token" }
$r = Test-Endpoint "Dict oem-brands" "GET" "/api/admin/dict/oem-brands/?limit=3" "" @{ "Authorization" = "Bearer $token" }
$r = Test-Endpoint "Dict product-name1s" "GET" "/api/admin/dict/product-name1s/?limit=3" "" @{ "Authorization" = "Bearer $token" }
$r = Test-Endpoint "Dict types" "GET" "/api/admin/dict/types/?limit=3" "" @{ "Authorization" = "Bearer $token" }
$r = Test-Endpoint "Dict machines" "GET" "/api/admin/dict/machines/?limit=3" "" @{ "Authorization" = "Bearer $token" }

# 7. 死信
$r = Test-Endpoint "Dead letter" "GET" "/api/admin/dead-letter/?limit=3" "" @{ "Authorization" = "Bearer $token" }

# 8. 后台产品
$r = Test-Endpoint "Admin products list" "GET" "/api/admin/products/?page=1&pageSize=3" "" @{ "Authorization" = "Bearer $token" }
$r = Test-Endpoint "Admin products search" "GET" "/api/admin/products/search?page=1&pageSize=3" "" @{ "Authorization" = "Bearer $token" }

# 9. ETL
$r = Test-Endpoint "ETL status" "GET" "/api/etl/status" "" @{ "X-Admin-Token" = $adminToken }
$r = Test-Endpoint "ETL dry-run (no file, expect 404)" "POST" "/api/admin/etl/trigger" '{"jsonlPath":"D:/nonexistent.jsonl","dryRun":true,"entityType":"products"}' @{ "X-Admin-Token" = $adminToken }
$r = Test-Endpoint "ETL history" "GET" "/api/admin/etl/history?limit=3" "" @{ "X-Admin-Token" = $adminToken }
$r = Test-Endpoint "ETL history aggregate" "GET" "/api/admin/etl/history/aggregate" "" @{ "X-Admin-Token" = $adminToken }

# 10. Perf
$r = Test-Endpoint "Perf snapshot" "GET" "/api/perf" "" @{}

$reportLines -join "`n" | Out-File -FilePath "d:\projects\sakurafilter\spike-test\smoke-test-report.md" -Encoding utf8
Write-Host "=== DONE ==="
Write-Host "Report: d:\projects\sakurafilter\spike-test\smoke-test-report.md"
