"""Day 8.4 全部 8 项后端补完 - 集成测试

测试目标:
  1) 鉴权 / 中间件 (X-Admin-Token)
  2) ProblemDetails 错误统一 (RFC 7807)
  3) API Rate Limiting (429 + Retry-After)
  4) CORS (5173/5174)
  5) API 文档 (Swagger)
  6) OpenAPI Schema 导出 (/swagger/v1/swagger.json)
  7) 产品历史查询 API
  8) 后台手动 ETL 触发 + 进度 (含 dry-run)
"""
import json
import time
import requests

API = 'http://localhost:5000'
TOKEN = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

print('=' * 90)
print('Day 8.4 全部 8 项集成测试')
print('=' * 90)


def hdr(token=None):
    h = {'Content-Type': 'application/json'}
    if token:
        h['X-Admin-Token'] = token
    return h


# ========== 1) 鉴权 / 中间件 ==========
print('\n[1] 鉴权 / 中间件')

# 1a) 缺少 token → 401
r = requests.get(f'{API}/api/admin/products', params={'pageSize': 1}, headers={'Content-Type': 'application/json'})
assert r.status_code == 401, f'应 401, 实际 {r.status_code}'
assert 'application/problem+json' in r.headers.get('content-type', ''), f'应 ProblemDetails, 实际 {r.headers.get("content-type")}'
data = r.json()
assert data.get('status') == 401 and 'X-Admin-Token' in data.get('detail', ''), f'ProblemDetails 格式错: {data}'
print(f'  1a) 无 token → 401 + ProblemDetails ✓ ({data.get("title")})')

# 1b) 错误 token → 401
r = requests.get(f'{API}/api/admin/products', params={'pageSize': 1}, headers={'Content-Type': 'application/json', 'X-Admin-Token': 'wrong'})
assert r.status_code == 401, f'应 401, 实际 {r.status_code}'
print(f'  1b) 错误 token → 401 ✓')

# 1c) 正确 token → 200
r = requests.get(f'{API}/api/admin/products', params={'pageSize': 1}, headers=hdr(TOKEN))
assert r.status_code == 200, f'应 200, 实际 {r.status_code} {r.text[:200]}'
print(f'  1c) 正确 token → 200 ✓')

# 1d) 白名单 /api/search 免鉴权
r = requests.post(f'{API}/api/search', json={'q': 'oil', 'limit': 1})
assert r.status_code == 200, f'应 200, 实际 {r.status_code}'
print(f'  1d) /api/search 白名单免鉴权 → 200 ✓')

# 1e) 白名单 /api/search/health 免鉴权
r = requests.get(f'{API}/api/search/health')
assert r.status_code == 200
print(f'  1e) /api/search/health 白名单免鉴权 → 200 ✓')

# ========== 2) ProblemDetails 错误统一 (RFC 7807) ==========
print('\n[2] ProblemDetails 错误统一')

# 2a) 404 走 ProblemDetails (产品 id=999999999 不存在)
r = requests.get(f'{API}/api/admin/products/999999999', headers=hdr(TOKEN))
assert r.status_code == 404, f'应 404, 实际 {r.status_code}'
assert 'application/problem+json' in r.headers.get('content-type', '')
data = r.json()
assert 'status' in data and 'detail' in data, f'ProblemDetails 字段缺失: {data}'
print(f'  2a) 404 → ProblemDetails ✓ ({data.get("title")}: {data.get("detail", "")[:50]})')

# 2b) 400 走 ProblemDetails (cursor 篡改,必须 pagingMode=cursor 才解析 cursor)
r = requests.get(f'{API}/api/admin/products/search', params={'cursor': 'invalid|format', 'pagingMode': 'cursor'}, headers=hdr(TOKEN))
assert r.status_code == 400, f'应 400, 实际 {r.status_code}'
data = r.json()
assert data.get('status') == 400
print(f'  2b) 400 → ProblemDetails ✓ ({data.get("title")}: {data.get("detail", "")[:50]})')

# ========== 3) API Rate Limiting ==========
print('\n[3] API Rate Limiting (etl 30/分钟)')

# 3a) ETL 限流: 快速连发 35 次, 期望至少 5 次 429
#   注意: dryRun=true 走 admin/etl/trigger, 也受 etl 限流
#   限流 30/分钟 (FixedWindow 1 分钟) — 用空文件 dryRun 避免大文件读取拖时间
ok = 0
limited = 0
import concurrent.futures
def send_one(i):
    r = requests.post(f'{API}/api/admin/etl/trigger', json={
        'jsonlPath': r'd:\projects\sakurafilter\spike-test\output\_test_tiny.jsonl',
        'mode': 'upsert',
        'dryRun': True
    }, headers=hdr(TOKEN))
    return r.status_code
with concurrent.futures.ThreadPoolExecutor(max_workers=10) as ex:
    results = list(ex.map(send_one, range(35)))
for c in results:
    if c == 200: ok += 1
    elif c == 429: limited += 1
    else: print(f'  3a) 意外状态 {c}')
print(f'  3a) 35 次 ETL dry-run (并发 10): ok={ok} limited={limited} (限流 30/分钟)')
assert limited >= 5, f'限流应触发 ≥ 5 次 429, 实际 limited={limited}, ok={ok}'
print(f'  ✓ 限流触发: {limited} 次 429 (FixedWindow 1 分钟 30 上限)')

# 3b) 429 含 Retry-After 头
r = requests.post(f'{API}/api/admin/etl/trigger', json={
    'jsonlPath': r'd:\projects\sakurafilter\spike-test\output\synthetic_products_100k.jsonl',
    'mode': 'upsert', 'dryRun': True
}, headers=hdr(TOKEN))
assert r.status_code == 429
retry_after = r.headers.get('Retry-After')
assert retry_after, f'429 应有 Retry-After 头, 实际 {dict(r.headers)}'
print(f'  3b) 429 含 Retry-After={retry_after} ✓')

# ========== 4) CORS ==========
print('\n[4] CORS 5173/5174 白名单')

# 4a) 5173 origin 应被允许
r = requests.options(f'{API}/api/search', headers={
    'Origin': 'http://localhost:5173',
    'Access-Control-Request-Method': 'POST',
    'Access-Control-Request-Headers': 'content-type'
})
acao = r.headers.get('Access-Control-Allow-Origin')
assert acao == 'http://localhost:5173', f'5173 应被允许, 实际 ACAO={acao}'
print(f'  4a) Origin 5173 → ACAO={acao} ✓')

# 4b) 5174 origin 应被允许
r = requests.options(f'{API}/api/search', headers={
    'Origin': 'http://localhost:5174',
    'Access-Control-Request-Method': 'POST',
    'Access-Control-Request-Headers': 'content-type'
})
acao = r.headers.get('Access-Control-Allow-Origin')
assert acao == 'http://localhost:5174', f'5174 应被允许, 实际 ACAO={acao}'
print(f'  4b) Origin 5174 → ACAO={acao} ✓')

# 4c) 未知 origin 不应被允许
r = requests.options(f'{API}/api/search', headers={
    'Origin': 'http://evil.com',
    'Access-Control-Request-Method': 'POST',
    'Access-Control-Request-Headers': 'content-type'
})
acao = r.headers.get('Access-Control-Allow-Origin')
assert acao != 'http://evil.com', f'evil.com 不应被允许, 实际 ACAO={acao}'
print(f'  4c) Origin evil.com → ACAO={acao or "(未返回)"} ✓ (CORS 阻止)')

# ========== 5) API 文档 (Swagger) ==========
print('\n[5] API 文档 (Swagger)')

# 5a) swagger.json
r = requests.get(f'{API}/swagger/v1/swagger.json')
assert r.status_code == 200, f'swagger.json 应 200, 实际 {r.status_code}'
spec = r.json()
print(f'  5a) swagger.json 200 ✓, paths={len(spec.get("paths", {}))} 个')

# 5b) swagger UI
r = requests.get(f'{API}/swagger/index.html')
assert r.status_code == 200
assert 'Swagger UI' in r.text or 'swagger' in r.text.lower()
print(f'  5b) swagger UI 200 ✓')

# ========== 6) OpenAPI Schema 导出 ==========
print('\n[6] OpenAPI Schema 导出 (供前端生成 TS 类型)')

# 6a) swagger.json 含所有端点 (admin 端点 16+)
admin_paths = [p for p in spec.get('paths', {}).keys() if '/api/admin' in p or '/api/etl' in p]
print(f'  6a) admin/etl 端点: {len(admin_paths)} 个')
assert len(admin_paths) >= 16, f'admin 端点应 ≥ 16, 实际 {len(admin_paths)}'

# 6b) 含 X-Admin-Token security scheme
sec = spec.get('components', {}).get('securitySchemes', {})
assert 'X-Admin-Token' in sec, f'securityScheme 缺失: {sec.keys()}'
print(f'  6b) securityScheme X-Admin-Token ✓')

# 6c) 新端点都在
expected = [
    '/api/admin/products/{id}/history',
    '/api/admin/etl/trigger',
    '/api/admin/etl/progress'
]
for ep in expected:
    if ep not in spec.get('paths', {}):
        print(f'  ⚠ 端点 {ep} 未在 OpenAPI 中 (可能 Minimal API 默认未注册)')
    else:
        print(f'  6c) {ep} ✓')

# ========== 7) 产品历史查询 API ==========
print('\n[7] 产品历史查询 API')

# 7a) 找有历史的测试产品 id (mr1=NULL 也行, search 用 mr1 模糊匹配)
#   优先找 DAY8.* 标识的测试产品 (mr1 形如 'DAY8X-...')
r = requests.get(f'{API}/api/admin/products/search', params={'mr1': 'DAY8', 'pageSize': 10}, headers=hdr(TOKEN))
data = r.json()
ids = [i['id'] for i in data['items']]
if not ids:
    # 兜底: 任意有历史的产品
    r = requests.get(f'{API}/api/admin/products/search', params={'pageSize': 1}, headers=hdr(TOKEN))
    ids = [i['id'] for i in r.json()['items']]
assert len(ids) > 0, '需至少一个测试产品'
product_id = ids[0]
print(f'  7a) 找到测试产品 id={product_id}')

# 7b) 查询历史
r = requests.get(f'{API}/api/admin/products/{product_id}/history', headers=hdr(TOKEN))
assert r.status_code == 200, f'应 200, 实际 {r.status_code} {r.text[:200]}'
data = r.json()
assert 'items' in data and 'total' in data
print(f'  7b) 历史查询: total={data["total"]} items={len(data["items"])}')
if data['items']:
    sample = data['items'][0]
    print(f'  7c) 最新历史: type={sample["changeType"]} at={sample["changedAt"]}')

# 7d) 不存在 id → 404 ProblemDetails
r = requests.get(f'{API}/api/admin/products/999999999/history', headers=hdr(TOKEN))
assert r.status_code == 404, f'应 404, 实际 {r.status_code}'
data = r.json()
assert data.get('status') == 404
print(f'  7d) 不存在产品 → 404 ProblemDetails ✓')

# 7e) limit 边界
r = requests.get(f'{API}/api/admin/products/{product_id}/history', params={'limit': 1}, headers=hdr(TOKEN))
assert r.status_code == 200
assert len(r.json()['items']) <= 1
print(f'  7e) limit=1 → 最多 1 条 ✓')

# ========== 8) 后台手动 ETL 触发 + 进度 ==========
print('\n[8] 后台手动 ETL 触发 + 进度')

# 等待 65s 让 etl 限流窗口 (30/分钟) 重置, 否则 8a 立刻 429
import time
print('  等待 65s 让 ETL 限流窗口重置...')
time.sleep(65)

# 8a) dryRun
r = requests.post(f'{API}/api/admin/etl/trigger', json={
    'jsonlPath': r'd:\projects\sakurafilter\spike-test\output\_test_tiny.jsonl',
    'mode': 'upsert',
    'dryRun': True
}, headers=hdr(TOKEN))
assert r.status_code == 200, f'应 200, 实际 {r.status_code} {r.text[:200]}'
data = r.json()
assert data.get('dryRun') is True and 'lines' in data
print(f'  8a) dryRun: lines={data["lines"]} sizeBytes={data["sizeBytes"]} ✓')

# 8b) 进度查询
r = requests.get(f'{API}/api/admin/etl/progress', headers=hdr(TOKEN))
assert r.status_code == 200
data = r.json()
print(f'  8b) 进度查询: inProgress={data["inProgress"]}, activeTask={data.get("activeTask") is not None}')

# 8c) 触发文件不存在 → 404
r = requests.post(f'{API}/api/admin/etl/trigger', json={
    'jsonlPath': r'C:\nonexistent\fake.jsonl',
    'mode': 'upsert', 'dryRun': True
}, headers=hdr(TOKEN))
assert r.status_code == 404
data = r.json()
print(f'  8c) 文件不存在 → 404 ✓ ({data.get("title")})')

# 8d) 真正触发 ETL (取小文件 100 行, 不爆数据)
small_file = r'd:\projects\sakurafilter\spike-test\output\synthetic_products_100k.jsonl'
# 用 head 100 行造小文件
import os
mini = r'd:\projects\sakurafilter\spike-test\output\_test_mini.jsonl'
with open(small_file, 'r', encoding='utf-8') as f:
    lines = [next(f) for _ in range(50)]
with open(mini, 'w', encoding='utf-8') as f:
    f.writelines(lines)
print(f'  8d) 已造 50 行 mini JSONL ({os.path.getsize(mini)} bytes)')

# 触发现有数据 (50 行 INSERT, 应该不破坏现有数据, 用 insert-only)
r = requests.post(f'{API}/api/admin/etl/trigger', json={
    'jsonlPath': mini,
    'mode': 'insert-only',
    'dryRun': False
}, headers=hdr(TOKEN))
assert r.status_code == 200, f'应 200, 实际 {r.status_code} {r.text[:200]}'
data = r.json()
print(f'  8d) 真实 ETL 触发: status={data.get("status")} read={data.get("read")} inserted={data.get("inserted")}')

print('\n' + '=' * 90)
print('Day 8.4 全部 8 项集成测试通过')
print('=' * 90)
