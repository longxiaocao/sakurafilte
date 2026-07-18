# V24-F74: 前后端联动验证 - API 契约测试
#   测试关键 API 链路: 搜索/产品详情/对比/字典/产品列表/typeahead/错误处理
$ErrorActionPreference = 'Continue'
$baseUrl = 'http://localhost:5148'
$adminToken = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

function Test-Api($method, $path, $headers=@{}, $body=$null, $desc) {
    $url = "$baseUrl$path"
    try {
        $params = @{
            Method = $method
            Uri = $url
            Headers = $headers
            TimeoutSec = 10
        }
        if ($body) { $params.Body = $body; $params.ContentType = 'application/json' }
        $resp = Invoke-WebRequest @params -ErrorAction Stop
        $status = $resp.StatusCode
        $len = $resp.RawContentLength
        Write-Host "[OK] $method $path → $status ($len bytes) - $desc" -ForegroundColor Green
        return @{ status=$status; ok=$true; resp=$resp }
    } catch {
        $status = $_.Exception.Response.StatusCode.value__
        $msg = $_.Exception.Message.Substring(0, [Math]::Min(100, $_.Exception.Message.Length))
        Write-Host "[ERR] $method $path → $status - $desc | $msg" -ForegroundColor Red
        return @{ status=$status; ok=$false; error=$msg }
    }
}

Write-Host "===== 1. 公开 API (无需认证) =====" -ForegroundColor Cyan

# 1.1 搜索 API
Test-Api GET '/api/search?q=oil&page=1&pageSize=5' -desc '公开搜索'

# 1.2 搜索聚合
Test-Api GET '/api/search/aggregate?q=oil' -desc '搜索聚合'

# 1.3 typeahead
Test-Api GET '/api/public/typeahead?field=oem_brand&q=B' -desc 'OEM 品牌 typeahead'

# 1.4 sitemap
Test-Api GET '/sitemap.xml' -desc 'sitemap'

# 1.5 health
Test-Api GET '/api/search/health' -desc '搜索健康检查'

Write-Host ""
Write-Host "===== 2. 后台 API (X-Admin-Token 认证) =====" -ForegroundColor Cyan

$adminHeaders = @{ 'X-Admin-Token' = $adminToken }

# 2.1 产品列表
Test-Api GET '/api/admin/products?page=1&pageSize=5' $adminHeaders -desc '产品列表'

# 2.2 字典 - OEM 品牌
Test-Api GET '/api/admin/dict/oem-brands?page=1&pageSize=5' $adminHeaders -desc '字典 OEM 品牌'

# 2.3 字典 - 类型
Test-Api GET '/api/admin/dict/types?page=1&pageSize=5' $adminHeaders -desc '字典类型'

# 2.4 ETL 历史
Test-Api GET '/api/admin/etl/history?limit=5' $adminHeaders -desc 'ETL 历史'

# 2.5 告警规则
Test-Api GET '/api/admin/alerts/rules' $adminHeaders -desc '告警规则'

# 2.6 用户列表
Test-Api GET '/api/admin/users' $adminHeaders -desc '用户列表'

# 2.7 XrefReorder Brands (V24-F73 验证)
Test-Api GET '/api/admin/xrefs/reorder/brands' $adminHeaders -desc 'XrefReorder Brands (V24-F73)'

# 2.8 XrefReorder by brand (V24-F73 验证)
Test-Api GET '/api/admin/xrefs/reorder?oemBrand=Bosch' $adminHeaders -desc 'XrefReorder by brand (V24-F73)'

Write-Host ""
Write-Host "===== 3. 错误处理验证 =====" -ForegroundColor Cyan

# 3.1 401 - 无 token 访问后台
Test-Api GET '/api/admin/products' -desc '401 无 token 访问后台'

# 3.2 401 - 错误 token
Test-Api GET '/api/admin/products' @{ 'X-Admin-Token' = 'wrong-token' } -desc '401 错误 token'

# 3.3 400 - 参数缺失
Test-Api GET '/api/admin/xrefs/reorder' $adminHeaders -desc '400 缺少 oemBrand 参数'

# 3.4 404 - 不存在的产品
Test-Api GET '/api/public/product/NONEXISTENT-OEM-12345' -desc '404 不存在的产品'

Write-Host ""
Write-Host "===== 4. JWT 认证链路 =====" -ForegroundColor Cyan

# 4.1 登录获取 JWT
$loginBody = '{"username":"admin","password":"Admin@2026"}'
$loginResult = Test-Api POST '/api/auth/login' @{} $loginBody 'JWT 登录'
if ($loginResult.ok) {
    $jwt = ($loginResult.resp.Content | ConvertFrom-Json).accessToken
    if ($jwt) {
        Write-Host "  [OK] JWT 获取成功 ($($jwt.Length) 字符)" -ForegroundColor Green
        $jwtHeaders = @{ 'Authorization' = "Bearer $jwt" }
        Test-Api GET '/api/admin/products?page=1&pageSize=3' $jwtHeaders -desc 'JWT 访问后台'
        Test-Api GET '/api/admin/auth/status' $jwtHeaders -desc 'JWT auth/status'
    } else {
        Write-Host "  [WARN] 登录响应无 accessToken" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "===== 5. 兜底 UI 验证 (前端路由) =====" -ForegroundColor Cyan

# 5.1 404 页面
Test-Api GET '/nonexistent-page-404' -desc '前端 404 路由 (返回 index.html)'

Write-Host ""
Write-Host "===== 验证完成 =====" -ForegroundColor Cyan
