# API production deployment test script v3 (pure ASCII for PS 5.1)
# Fixes: login ordering (avoid rate limit cascade), Bearer for /api/perf, correct routes
$ErrorActionPreference = 'Continue'
$BASE = 'http://localhost:5148'
$ADMIN_TOKEN = $env:ADMIN_TOKEN
if (-not $ADMIN_TOKEN) { $ADMIN_TOKEN = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C' }

$results = [System.Collections.Generic.List[object]]::new()
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# 唯一后缀: 避免软删除残留占用 username 导致 409 (DELETE 为 DeactivateAsync 软删除, 用户名仍被占用)
$qaUser = "qa_tmp_$([DateTimeOffset]::Now.ToUnixTimeSeconds())"
$viewerUser = "qa_view_$([DateTimeOffset]::Now.ToUnixTimeSeconds())"

function Add-Result {
    param($Id, $Name, $Method, $Url, $Ok, $Expected, $Actual, $Ms, $Detail)
    $results.Add([pscustomobject]@{ id=$Id; name=$Name; ok=$Ok; expected=($Expected -join '|'); actual=$Actual; ms=$Ms; detail=$Detail; url=$Url })
    $line = "{0} {1} [{2}] {3} -> {4} ({5}ms){6}" -f ($(if($Ok){'PASS'}else{'FAIL'})), $Id, $Method, $Name, $Actual, $Ms, $(if(-not $Ok){' | ' + $Detail}else{''})
    Write-Host $line
}

function Test-Case {
    param([string]$Id, [string]$Name, [string]$Method, [string]$Url, $Body = $null, [int[]]$Expected, [hashtable]$Headers = @{}, [int]$TimeoutMs = 20000)
    $s = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $params = @{ Uri = $Url; Method = $Method; TimeoutSec = [math]::Max(1, $TimeoutMs / 1000); UseBasicParsing = $true }
        if ($Headers.Count -gt 0) { $params.Headers = $Headers }
        if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 6 -Compress); $params.ContentType = 'application/json' }
        $resp = Invoke-WebRequest @params -ErrorAction Stop
        $ok = $Expected -contains [int]$resp.StatusCode
        $ms = $s.ElapsedMilliseconds
        $detail = ''
        if (-not $ok) { $detail = $resp.Content.Substring(0, [Math]::Min(200, $resp.Content.Length)) }
        Add-Result $Id $Name $Method $Url $ok $Expected ([int]$resp.StatusCode) $ms $detail
    } catch {
        $ms = $s.ElapsedMilliseconds
        $status = 0
        $detail = $_.Exception.Message
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        $ok = $Expected -contains $status
        Add-Result $Id $Name $Method $Url $ok $Expected $status $ms $detail
    }
    $s.Stop()
}

function Test-Json {
    param([string]$Id, [string]$Name, [string]$Method, [string]$Url, $Body = $null, [int[]]$Expected, [string[]]$Fields, [hashtable]$Headers = @{})
    $s = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $params = @{ Uri = $Url; Method = $Method; TimeoutSec = 20; UseBasicParsing = $true }
        if ($Headers.Count -gt 0) { $params.Headers = $Headers }
        if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 6 -Compress); $params.ContentType = 'application/json' }
        $resp = Invoke-WebRequest @params -ErrorAction Stop
        $ok = $Expected -contains [int]$resp.StatusCode
        $missing = @()
        if ($ok -and $Fields) {
            try { $data = $resp.Content | ConvertFrom-Json } catch { $data = $null }
            if ($null -eq $data) { $ok = $false; $missing += 'NOT_JSON' }
            else { foreach ($f in $Fields) { if ($null -eq $data.$f) { $missing += $f } } }
        }
        $ok = $ok -and ($missing.Count -eq 0)
        $ms = $s.ElapsedMilliseconds
        Add-Result $Id $Name $Method $Url $ok $Expected ([int]$resp.StatusCode) $ms $(if($missing){"missing: $($missing -join ',')"}else{''})
    } catch {
        $ms = $s.ElapsedMilliseconds
        $status = 0
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        $ok = $Expected -contains $status
        Add-Result $Id $Name $Method $Url $ok $Expected $status $ms $_.Exception.Message
    }
    $s.Stop()
}

# ============ 0. Basic connectivity ============
Test-Json 'A01' 'GET /api/info' 'GET' "$BASE/api/info" $null @(200) @('name','version','status')
Test-Json 'A02' 'GET /health/live' 'GET' "$BASE/health/live" $null @(200) @('status')
Test-Json 'A03' 'GET /health/ready' 'GET' "$BASE/health/ready" $null @(200) @('status','checks')
Test-Case 'A04' 'GET swagger json' 'GET' "$BASE/swagger/v1/swagger.json" $null @(200)

# ============ 1. Auth: FIRST login only (rate limit 5/min) ============
$adminLogin = $null
try { $adminLogin = Invoke-RestMethod -Uri "$BASE/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ username='admin'; password='Admin@2026' } | ConvertTo-Json) -TimeoutSec 15 } catch { Write-Host "WARN admin login failed: $($_.Exception.Message)" }
if ($adminLogin) {
    Test-Case 'B01' 'POST login ok' 'POST' "$BASE/api/auth/login" @{ username='admin'; password='Admin@2026' } @(200)
    $adminToken = $adminLogin.accessToken
} else {
    $adminToken = $null
}
$adminHdr = @{ Authorization = "Bearer $adminToken" }
$staticHdr = @{ 'X-Admin-Token' = $ADMIN_TOKEN }

if ($adminToken) {
    Test-Json 'B06' 'GET /api/auth/me' 'GET' "$BASE/api/auth/me" $null @(200) @('username','role') $adminHdr
    Test-Case 'B07' 'GET me no token -> 401' 'GET' "$BASE/api/auth/me" $null @(401)
    Test-Case 'B08' 'GET me forged token -> 401' 'GET' "$BASE/api/auth/me" $null @(401) @{ Authorization = 'Bearer fake.token.here' }
    Test-Json 'B09' 'GET turnstile-config' 'GET' "$BASE/api/auth/turnstile-config" $null @(200) @('siteKey')
    Test-Case 'B10' 'POST change-pwd wrong old -> 400' 'POST' "$BASE/api/auth/change-password" @{ oldPassword='wrong'; newPassword='NewPass@2026' } @(400) $adminHdr
    Test-Case 'B11' 'POST change-pwd weak -> 400' 'POST' "$BASE/api/auth/change-password" @{ oldPassword='Admin@2026'; newPassword='123' } @(400) $adminHdr
    Test-Case 'B12' 'POST refresh invalid -> 401' 'POST' "$BASE/api/auth/refresh" @{ refreshToken='invalid-token' } @(401)
} else { Write-Host "SKIP B06-B12" }

# ============ 2. Public search ============
Test-Json 'C01' 'POST search empty q' 'POST' "$BASE/api/search" @{ q=''; pageSize=10 } @(200) @('result')
Test-Json 'C02' 'POST search single char' 'POST' "$BASE/api/search" @{ q='a' } @(200) @('result')
Test-Json 'C03' 'POST search pct char' 'POST' "$BASE/api/search" @{ q='%' } @(200)
Test-Json 'C04' 'POST search sql inject' 'POST' "$BASE/api/search" @{ q="' OR 1=1 --" } @(200)
Test-Json 'C05' 'POST search xss payload' 'POST' "$BASE/api/search" @{ q='<script>alert(1)</script>' } @(200)
Test-Case 'C06' 'POST search 500 chars' 'POST' "$BASE/api/search" @{ q=('x' * 500) } @(200,400)
Test-Json 'C07' 'POST search pageSize=50' 'POST' "$BASE/api/search" @{ q='filter'; pageSize=50 } @(200) @('result')
Test-Case 'C08' 'POST search page=-1 -> 200(归一化)' 'POST' "$BASE/api/search" @{ page=-1 } @(200)
Test-Case 'C09' 'POST search page=999999 -> 200/400' 'POST' "$BASE/api/search" @{ page=999999 } @(200,400)
Test-Json 'C10' 'GET search health' 'GET' "$BASE/api/search/health" $null @(200) @('provider','healthy')
Test-Json 'C11' 'POST public aggregate' 'POST' "$BASE/api/public/search/aggregate" @{ q='filter'; pageSize=10 } @(200) @('hits')
Test-Case 'C12' 'POST batch-oem' 'POST' "$BASE/api/public/search/batch-oem" @{ oems=@('SH630239','1234567890') } @(200)

# ============ 3. Public products ============
Test-Case 'D01' 'GET product detail known oem' 'GET' "$BASE/api/products/SH630239" $null @(200)
Test-Case 'D02' 'GET product detail unknown -> 404' 'GET' "$BASE/api/products/NO_SUCH_OEM_XYZ" $null @(404)
Test-Case 'D03' 'GET product detail blank -> 404' 'GET' "$BASE/api/products/%20" $null @(404)
Test-Case 'D04' 'GET product detail long -> 400/404' 'GET' "$BASE/api/products/123456789012345678901234567890" $null @(400,404)
Test-Case 'D05' 'GET legacy /product/{oem} 后端404(nginx 生产301)' 'GET' "$BASE/product/SH630239" $null @(404)
Test-Case 'D06' 'GET by-type' 'GET' "$BASE/api/public/by-type?oem1=Hydraulic%20Filter&oem2=Spin-On" $null @(200)
Test-Json 'D07' 'GET sibling-oem3' 'GET' "$BASE/api/public/products/SH630239/sibling-oem3" $null @(200) @('items')

# ============ 4. Dict module ============
$dicts = @('oem-brands','product-name1s','product-name2s','types','oem-no3s','medias','machines','engines')
foreach ($d in $dicts) {
    Test-Case "E01-$d" "GET dict list $d" 'GET' "$BASE/api/admin/dict/$d" $null @(200) $adminHdr
    Test-Case "E02-$d" "GET dict typeahead $d" 'GET' "$BASE/api/admin/dict/$d/typeahead?q=a" $null @(200) $adminHdr
}
Test-Case 'E03' 'GET dict export' 'GET' "$BASE/api/admin/dict/oem-brands/export" $null @(200) $adminHdr
Test-Case 'E04' 'GET dict export-xlsx' 'GET' "$BASE/api/admin/dict/oem-brands/export-xlsx" $null @(200) $adminHdr
Test-Case 'E05' 'GET dict no auth -> 401' 'GET' "$BASE/api/admin/dict/oem-brands" $null @(401)
# 唯一品牌名: 避免软删除残留占用唯一索引导致 409
$qaBrand = "QA_BRAND_$([DateTimeOffset]::Now.ToUnixTimeSeconds())"
Test-Case 'E06' 'POST dict create' 'POST' "$BASE/api/admin/dict/oem-brands" @{ brand=$qaBrand } @(200,201) $adminHdr

# ============ 5. Admin products ============
Test-Json 'F01' 'GET admin products list' 'GET' "$BASE/api/admin/products" $null @(200) @('items','total') $adminHdr
Test-Json 'F02' 'GET admin products search' 'GET' "$BASE/api/admin/products/search?q=filter" $null @(200) @('items') $adminHdr
Test-Case 'F03' 'GET admin product id=1' 'GET' "$BASE/api/admin/products/1" $null @(200) $adminHdr
Test-Case 'F04' 'GET admin product id=999999 -> 404' 'GET' "$BASE/api/admin/products/999999" $null @(404) $adminHdr
Test-Case 'F05' 'GET admin product id=abc -> 404(路由约束)' 'GET' "$BASE/api/admin/products/abc" $null @(404) $adminHdr
Test-Case 'F06' 'GET admin products no auth -> 401' 'GET' "$BASE/api/admin/products" $null @(401)
Test-Case 'F07' 'POST create product empty -> 400' 'POST' "$BASE/api/admin/products/" @{} @(400) $adminHdr
Test-Case 'F08' 'POST create dup MR1 -> 409' 'POST' "$BASE/api/admin/products/" @{ mr1='MR000021'; oem1='Hydraulic Filter'; oem2='Spin-On'; oem3='SH630239' } @(409) $adminHdr
Test-Json 'F09' 'POST products compare' 'POST' "$BASE/api/admin/products/compare" @{ ids=@(1,2) } @(200) @('items') $adminHdr
Test-Case 'F10' 'GET product images' 'GET' "$BASE/api/admin/products/MR000021/images" $null @(200) $adminHdr
Test-Case 'F11' 'GET product history id=1' 'GET' "$BASE/api/admin/products/1/history" $null @(200) $adminHdr

# ============ 6. ETL ============
Test-Case 'G01' 'GET etl status' 'GET' "$BASE/api/etl/status" $null @(200) $staticHdr
Test-Case 'G02' 'GET etl no token -> 401' 'GET' "$BASE/api/etl/status" $null @(401)
Test-Case 'G03' 'GET etl progress' 'GET' "$BASE/api/admin/etl/progress" $null @(200) $staticHdr
Test-Case 'G04' 'GET etl history' 'GET' "$BASE/api/admin/etl/history" $null @(200) $staticHdr
Test-Case 'G05' 'GET etl history aggregate' 'GET' "$BASE/api/admin/etl/history/aggregate" $null @(200) $staticHdr
Test-Case 'G06' 'GET etl template' 'GET' "$BASE/api/admin/etl/template" $null @(200) $staticHdr
Test-Case 'G07' 'POST etl trigger empty -> 400' 'POST' "$BASE/api/admin/etl/trigger" @{ filePath='' } @(400) $staticHdr
Test-Case 'G08' 'POST etl trigger missing -> 400/404' 'POST' "$BASE/api/admin/etl/trigger" @{ filePath='f:/no/such/file.xlsx' } @(400,404) $staticHdr

# ============ 7. Users ============
Test-Json 'H01' 'GET users list' 'GET' "$BASE/api/admin/users" $null @(200) @('items') $adminHdr
Test-Case 'H02' 'GET users no auth -> 401' 'GET' "$BASE/api/admin/users" $null @(401)
Test-Case 'H03' 'POST create weak pwd -> 400' 'POST' "$BASE/api/admin/users" @{ username=$qaUser; password='123'; role='viewer' } @(400) $adminHdr
Test-Case 'H04' 'POST create bad role -> 400' 'POST' "$BASE/api/admin/users" @{ username=$qaUser; password='QaPass@2026'; role='superadmin' } @(400) $adminHdr
Test-Case 'H05' 'POST create user ok' 'POST' "$BASE/api/admin/users" @{ username=$qaUser; password='QaPass@2026'; role='viewer' } @(200,201) $adminHdr
$tmpUserId = $null
try { $rr = Invoke-RestMethod -Uri "$BASE/api/admin/users" -Method Get -Headers $adminHdr -TimeoutSec 15; $tmpUserId = $rr.items | Where-Object { $_.username -eq $qaUser } | Select-Object -First 1 -ExpandProperty id } catch {}
if ($tmpUserId) {
    Test-Case 'H06' "GET user detail $tmpUserId" 'GET' "$BASE/api/admin/users/$tmpUserId" $null @(200) $adminHdr
    Test-Case 'H07' "POST reset-password $tmpUserId" 'POST' "$BASE/api/admin/users/$tmpUserId/reset-password" @{ newPassword='NewQa@2026' } @(200) $adminHdr
    Test-Case 'H08' "DELETE user $tmpUserId" 'DELETE' "$BASE/api/admin/users/$tmpUserId" $null @(200) $adminHdr
    Test-Case 'H09' "GET deleted user -> 404" 'GET' "$BASE/api/admin/users/$tmpUserId" $null @(404) $adminHdr
} else { Write-Host "SKIP H06-H09" }
Test-Case 'H10' 'GET audit login' 'GET' "$BASE/api/admin/audit/login" $null @(200) $adminHdr
Test-Case 'H11' 'GET audit no auth -> 401' 'GET' "$BASE/api/admin/audit/login" $null @(401)

# ============ 8. Ops / metrics (Bearer JWT required) ============
Test-Case 'I01' 'GET /metrics' 'GET' "$BASE/metrics" $null @(200) $staticHdr
Test-Json 'I02' 'GET /api/perf' 'GET' "$BASE/api/perf" $null @(200) -Fields @() -Headers $adminHdr
Test-Case 'I03' 'GET perf alerts' 'GET' "$BASE/api/admin/perf/alerts?limit=10" $null @(200) $adminHdr
Test-Case 'I04' 'GET meili snapshot' 'GET' "$BASE/api/admin/perf/meili/snapshot" $null @(200) $adminHdr
Test-Case 'I05' 'GET auth status' 'GET' "$BASE/api/admin/auth/status" $null @(200) $staticHdr
Test-Case 'I06' 'GET /metrics no token' 'GET' "$BASE/metrics" $null @(200)  # documented public for prometheus scraping

# ============ 9. Alerts ============
Test-Case 'J01' 'GET alerts history' 'GET' "$BASE/api/admin/alerts/history?limit=10&offset=0" $null @(200) $adminHdr
Test-Case 'J02' 'GET alerts stats' 'GET' "$BASE/api/admin/alerts/stats" $null @(200) $adminHdr
Test-Case 'J03' 'GET alerts rules' 'GET' "$BASE/api/admin/alerts/rules" $null @(200) $adminHdr
Test-Case 'J04' 'GET alerts history id=999 -> 404' 'GET' "$BASE/api/admin/alerts/history/999" $null @(404) $adminHdr

# ============ 10. Reorder / machine ============
Test-Case 'K01' 'GET xref brands' 'GET' "$BASE/api/admin/xrefs/reorder/brands" $null @(200) $staticHdr
Test-Case 'K02' 'GET machine-tree' 'GET' "$BASE/api/admin/machine-tree" $null @(200) $staticHdr
# K03: 空 body 时 machineId=0 -> service 查无机型抛 MACHINE_NOT_FOUND -> 404 (设计行为)
Test-Case 'K03' 'POST batch-bind empty -> 404(MACHINE_NOT_FOUND)' 'POST' "$BASE/api/admin/machine-apps/batch-bind" @{} @(400,404) $staticHdr
# K03b: 有效 machineId 但不存在 -> 404 错误码路径
Test-Case 'K03b' 'POST batch-bind missing machine -> 404' 'POST' "$BASE/api/admin/machine-apps/batch-bind" @{ machineId=999999; mr1List=@('SH630239') } @(404) $staticHdr
Test-Json 'K04' 'GET machine-brands aggregated' 'GET' "$BASE/api/public/machine-brands/aggregated" $null @(200)
Test-Json 'K05' 'GET machine-brands catalog' 'GET' "$BASE/api/public/machine-brands/catalog" $null @(200) @('categories')

# ============ 11. Dead letter ============
Test-Case 'L01' 'GET deadletter list' 'GET' "$BASE/api/admin/dead-letter/" $null @(200) $staticHdr
Test-Case 'L02' 'POST deadletter recover missing -> 404' 'POST' "$BASE/api/admin/deadletter/999999/recover" $null @(404) $staticHdr

# ============ 12. Storage / site / sitemap ============
Test-Case 'M01' 'GET storage config' 'GET' "$BASE/api/admin/storage/config" $null @(200) $staticHdr
Test-Case 'M02' 'GET site-content' 'GET' "$BASE/api/admin/site-content" $null @(200) $staticHdr
Test-Case 'M03' 'GET sitemap.xml' 'GET' "$BASE/sitemap.xml" $null @(200)
Test-Json 'M04' 'GET public typeahead oem-no3' 'GET' "$BASE/api/public/typeahead/oem-no3?q=SH" $null @(200) @('items')
Test-Json 'M05' 'GET public featured' 'GET' "$BASE/api/public/featured" $null @(200) @('items')
Test-Case 'M06' 'GET public compare' 'GET' "$BASE/api/public/compare?ids=1,2" $null @(200)

# ============ 13. RBAC viewer (2nd login within window) ============
$viewerToken = $null
try { Invoke-RestMethod -Uri "$BASE/api/admin/users" -Method Post -Headers $adminHdr -ContentType 'application/json' -Body (@{ username=$viewerUser; password='QaPass@2026'; role='viewer' } | ConvertTo-Json) -TimeoutSec 15 | Out-Null } catch {}
try { $r = Invoke-RestMethod -Uri "$BASE/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ username=$viewerUser; password='QaPass@2026' } | ConvertTo-Json) -TimeoutSec 15; $viewerToken = $r.accessToken } catch { Write-Host "WARN viewer login failed: $($_.Exception.Message)" }
if ($viewerToken) {
    $viewerHdr = @{ Authorization = "Bearer $viewerToken" }
    Test-Case 'N01' 'viewer GET users -> 403' 'GET' "$BASE/api/admin/users" $null @(403) $viewerHdr
    Test-Case 'N02' 'viewer GET products -> 200' 'GET' "$BASE/api/admin/products" $null @(200) $viewerHdr
    Test-Case 'N03' 'viewer DELETE product -> 403' 'DELETE' "$BASE/api/admin/products/1" $null @(403) $viewerHdr
    try { $rr = Invoke-RestMethod -Uri "$BASE/api/admin/users" -Method Get -Headers $adminHdr -TimeoutSec 15; $vid = $rr.items | Where-Object { $_.username -eq $viewerUser } | Select-Object -First 1 -ExpandProperty id; if ($vid) { Invoke-RestMethod -Uri "$BASE/api/admin/users/$vid" -Method Delete -Headers $adminHdr -TimeoutSec 15 | Out-Null } } catch {}
} else { Write-Host "SKIP N01-N03" }

# ============ 14. Wrong-password + rate limit (end of script) ============
Test-Case 'B02' 'POST login wrong pwd -> 401' 'POST' "$BASE/api/auth/login" @{ username='admin'; password='WrongPass123' } @(401,429)
Test-Case 'B03' 'POST login empty user -> 400' 'POST' "$BASE/api/auth/login" @{ username=''; password='x' } @(400,429)
Test-Case 'B04' 'POST login unknown user -> 401' 'POST' "$BASE/api/auth/login" @{ username='no_such_user'; password='Admin@2026' } @(401,429)
Test-Case 'B05' 'POST login sql inject -> 401' 'POST' "$BASE/api/auth/login" @{ username="admin' OR '1'='1"; password='x' } @(400,401,429)
$rlCount = 0
for ($i = 0; $i -lt 8; $i++) {
    try {
        Invoke-RestMethod -Uri "$BASE/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{ username='admin'; password='WrongPass'; } | ConvertTo-Json) -TimeoutSec 10 | Out-Null
    } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 429) { $rlCount++ }
    }
}
$rlOk = $rlCount -ge 1
$results.Add([pscustomobject]@{ id='O01'; name='rate limit 429 after burst'; ok=$rlOk; expected='429>=1'; actual="429x$rlCount"; ms=0; detail=''; url="$BASE/api/auth/login" })
Write-Host ("{0} O01 [POST] rate limit -> 429x{1}" -f ($(if($rlOk){'PASS'}else{'FAIL'})), $rlCount)

# ============ 15. Perf sampling ============
$perfSamples = @{}
foreach ($p in @(@('search','/api/search'), @('detail','/api/products/SH630239'), @('dict','/api/admin/dict/oem-brands'), @('health','/health/ready'))) {
    $times = [System.Collections.Generic.List[int]]::new()
    for ($i = 0; $i -lt 10; $i++) {
        $s2 = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $hp = @{ Uri = "$BASE$($p[1])"; Method = 'GET'; TimeoutSec = 20; UseBasicParsing = $true }
            if ($p[0] -eq 'search') { $hp.Method = 'POST'; $hp.Body = '{"q":"filter","pageSize":10}'; $hp.ContentType = 'application/json' }
            if ($p[0] -eq 'dict') { $hp.Headers['Authorization'] = "Bearer $adminToken" }
            Invoke-WebRequest @hp -ErrorAction Stop | Out-Null
        } catch {}
        $s2.Stop(); $times.Add([int]$s2.ElapsedMilliseconds)
    }
    $sorted = $times | Sort-Object
    $idx = [int]($sorted.Count * 0.95) - 1; if ($idx -lt 0) { $idx = 0 }
    $p95 = $sorted[$idx]
    $avg = [int](($times | Measure-Object -Average).Average)
    $perfSamples[$p[0]] = @{ avg = $avg; p95 = $p95; max = $sorted[-1] }
    Write-Host ("PERF {0}: avg={1}ms p95={2}ms max={3}ms" -f $p[0], $avg, $p95, $sorted[-1])
}

# ============ Summary ============
$sw.Stop()

# 物理清理本次创建的 QA 测试用户 (DELETE 为软删除, 残留会占用 username 导致下次 409)
try {
    $cleaned = docker exec sakurafilter-perf-postgres-1 psql -U postgres -d spike_test_v3 -t -A -c "DELETE FROM users WHERE username LIKE 'qa\_%';" 2>&1
    Write-Host "QA user cleanup: $cleaned"
} catch { Write-Host "WARN qa cleanup failed: $($_.Exception.Message)" }

$pass = ($results | Where-Object { $_.ok }).Count
$fail = ($results | Where-Object { -not $_.ok }).Count
Write-Host ""
Write-Host "=================== SUMMARY ==================="
Write-Host "PASS: $pass / FAIL: $fail / TOTAL: $($results.Count) / ELAPSED: $([math]::Round($sw.Elapsed.TotalSeconds,1))s"
Write-Host "================================================"
$results | Where-Object { -not $_.ok } | Select-Object id, name, expected, actual, detail | Format-Table -Wrap -AutoSize | Out-String -Width 200

$report = @{ timestamp = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'); base = $BASE; pass = $pass; fail = $fail; total = $results.Count; elapsedSec = $sw.Elapsed.TotalSeconds; perf = $perfSamples; results = $results }
$report | ConvertTo-Json -Depth 6 | Out-File -FilePath "$PSScriptRoot\api_test_report.json" -Encoding UTF8
Write-Host "Report: $PSScriptRoot\api_test_report.json"
