# SakuraFilter E2E 冒烟测试报告
- 测试时间: 2026-07-06
- 后端: dotnet run (重构后)
- 监听: http://localhost:5148
- 测试者: Claude (mini-max M3)

## 1. 基础探活
EOF

test() {
    local name="$1"; local method="$2"; local path="$3"; local data="$4"; local headers="$5"
    local code body
    if [ -n "$data" ]; then
        body=$(curl -s -o /tmp/_body -w "%{http_code}" -X "$method" "http://localhost:5148$path" -H "Content-Type: application/json" $headers -d "$data")
    else
        body=$(curl -s -o /tmp/_body -w "%{http_code}" -X "$method" "http://localhost:5148$path" $headers)
    fi
    code=$body
    echo "### $name" >> $REPORT
    echo "- $method $path → HTTP $code" >> $REPORT
    if [ -s /tmp/_body ]; then
        head -c 300 /tmp/_body >> $REPORT
        echo >> $REPORT
    fi
    echo >> $REPORT
}

REPORT="/tmp/smoke-test-report.md"
echo "" > $REPORT

# 1. 基础探活
test "根路径" GET "/"
test "Liveness 探活" GET "/health/live"
test "Readiness 探活" GET "/health/ready"
test "Prometheus Metrics" GET "/metrics"

# 2. 公开搜索（无需鉴权）
test "搜索健康检查" GET "/api/search/health"
test "公开搜索" POST "/api/search" '{"q":"","page":1,"pageSize":3}'

# 3. 公开性能上报
test "性能埋点批量上报" POST "/api/perf/ingest" '{"samples":[{"path":"/","method":"GET","statusCode":200,"durationMs":12.3,"ts":"2026-07-06T00:00:00Z"},{"path":"/api/search","method":"POST","statusCode":200,"durationMs":45.6,"ts":"2026-07-06T00:00:01Z"}]}'
test "性能埋点空 samples (期望 400)" POST "/api/perf/ingest" '{"samples":[]}'

# 4. 认证
test "登录 (admin / Admin@2026)" POST "/api/auth/login" '{"username":"admin","password":"Admin@2026"}'

# 提取 token
TOKEN=$(curl -s -X POST http://localhost:5148/api/auth/login -H "Content-Type: application/json" -d '{"username":"admin","password":"Admin@2026"}' | python -c "import sys, json; print(json.load(sys.stdin).get('token',''))" 2>/dev/null)
echo "TOKEN length: ${#TOKEN}" >> $REPORT
echo "" >> $REPORT

AUTH=" -H \"Authorization: Bearer $TOKEN\""
ADMINH="-H \"X-Admin-Token: dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C\""

# 5. 鉴权保护
test "无 token 访问 admin (期望 401)" GET "/api/admin/products/" "" ""
test "用 JWT 访问 admin/products" GET "/api/admin/products/?page=1&pageSize=3" "" "-H \"Authorization: Bearer $TOKEN\""
test "X-Admin-Token 访问 admin/auth/status" GET "/api/admin/auth/status" "" "-H \"X-Admin-Token: dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C\""

# 6. 后台 - 字典 schema 契约
test "字典 schema 契约" GET "/api/admin/dict/_schema" "" "-H \"Authorization: Bearer $TOKEN\""

# 7. 后台 - 各字典列表
test "字典: oem-brands 列表" GET "/api/admin/dict/oem-brands/?limit=5" "" "-H \"Authorization: Bearer $TOKEN\""
test "字典: product-name1s 列表" GET "/api/admin/dict/product-name1s/?limit=5" "" "-H \"Authorization: Bearer $TOKEN\""
test "字典: types 列表" GET "/api/admin/dict/types/?limit=5" "" "-H \"Authorization: Bearer $TOKEN\""
test "字典: machines 列表" GET "/api/admin/dict/machines/?limit=5" "" "-H \"Authorization: Bearer $TOKEN\""

# 8. 后台 - 死信队列
test "死信队列分页" GET "/api/admin/dead-letter/?limit=5" "" "-H \"Authorization: Bearer $TOKEN\""

# 9. 后台 - 产品管理
test "后台产品列表" GET "/api/admin/products/?page=1&pageSize=3" "" "-H \"Authorization: Bearer $TOKEN\""
test "后台产品搜索" GET "/api/admin/products/search?page=1&pageSize=3" "" "-H \"Authorization: Bearer $TOKEN\""

# 10. ETL 端点
test "ETL 状态查询" GET "/api/etl/status" "" "-H \"X-Admin-Token: dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C\""
test "ETL dry-run (期望文件不存在)" POST "/api/admin/etl/trigger" '{"jsonlPath":"D:/nonexistent.jsonl","dryRun":true,"entityType":"products"}' "-H \"X-Admin-Token: dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C\""
test "ETL 历史" GET "/api/admin/etl/history?limit=5" "" "-H \"X-Admin-Token: dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C\""

# 11. Auth Status
test "Auth Token 状态" GET "/api/admin/auth/status" "" "-H \"X-Admin-Token: dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C\""

# 12. 性能快照 (v30-19: /api/perf 加 RequireAuthorization, 需 X-Admin-Token)
test "性能快照" GET "/api/perf" "" "-H \"X-Admin-Token: dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C\""

cat $REPORT
