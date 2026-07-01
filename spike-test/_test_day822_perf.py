"""Day 8.2.2 性能对比: 合并 EXISTS 后的延迟基准

测量在 1M products + ~5M xref + ~5M machine_application 数据规模下:
  - countMode=exact:   跑 COUNT + 翻页, 测全链路延迟
  - countMode=estimated: 跑 reltuples 统计 O(1), 测延迟
  - countMode=none:    不跑 COUNT, 测纯翻页延迟

测试场景: 1) 6 字段全开 (5 machine + 1 oem3)
         2) 7 字段全开 (5 machine + 2 xref)
         3) 0 字段 (全表扫)
"""
import time
import requests

API = 'http://localhost:5148'

def measure(params, label, n=3):
    """跑 n 次取平均"""
    durations = []
    total = None
    items_count = 0
    for _ in range(n):
        t0 = time.perf_counter()
        r = requests.get(f'{API}/api/admin/products/search', params={**params, 'pageSize': 50})
        dt = (time.perf_counter() - t0) * 1000
        durations.append(dt)
        data = r.json()
        total = data['total']
        items_count = len(data['items'])
    avg = sum(durations) / len(durations)
    print(f'  {label:40s} avg={avg:6.0f}ms (min={min(durations):.0f}, max={max(durations):.0f}), total={total}, items={items_count}')
    return avg, total

# 测 1: 全表 (countMode=exact)  - 基准
print('=== 全表扫基准 (countMode=exact) ===')
measure({'countMode': 'exact', 'pagingMode': 'offset'}, '全表 + COUNT exact')

# 测 2: 全表 (countMode=estimated)
print('\n=== 全表扫 + reltuples 统计 (countMode=estimated) ===')
measure({'countMode': 'estimated', 'pagingMode': 'offset'}, '全表 + COUNT estimated')

# 测 3: 全表 (countMode=none)
print('\n=== 全表扫 + 不算 COUNT (countMode=none) ===')
measure({'countMode': 'none', 'pagingMode': 'offset'}, '全表 + 不算 COUNT (none)')

# 测 4: 5 字段机器应用 + oem3 = 6 字段交集
print('\n=== 6 字段交集 (5 machine + 1 oem3) ===')
measure({
    'machineBrand': 'CATERPILLAR',
    'machineModel': '320D',
    'modelName': '320D',
    'engineBrand': 'CATERPILLAR',
    'engineType': 'C6.4',
    'oem3Batch': 'P550425,LF9009',  # 任一命中即返回
    'countMode': 'exact',
    'pagingMode': 'offset'
}, '6 字段交集 + COUNT exact')
measure({
    'machineBrand': 'CATERPILLAR',
    'machineModel': '320D',
    'modelName': '320D',
    'engineBrand': 'CATERPILLAR',
    'engineType': 'C6.4',
    'oem3Batch': 'P550425,LF9009',
    'countMode': 'estimated',
    'pagingMode': 'offset'
}, '6 字段交集 + COUNT estimated')
measure({
    'machineBrand': 'CATERPILLAR',
    'machineModel': '320D',
    'modelName': '320D',
    'engineBrand': 'CATERPILLAR',
    'engineType': 'C6.4',
    'oem3Batch': 'P550425,LF9009',
    'countMode': 'none',
    'pagingMode': 'offset'
}, '6 字段交集 + COUNT none')

# 测 5: 7 字段交集 (5 machine + 2 xref)
print('\n=== 7 字段交集 (5 machine + 2 xref) ===')
measure({
    'machineBrand': 'CATERPILLAR',
    'machineModel': '320D',
    'modelName': '320D',
    'engineBrand': 'CATERPILLAR',
    'engineType': 'C6.4',
    'oemBrand': 'BOSCH',
    'oem3Batch': 'P550425,LF9009',
    'countMode': 'exact',
    'pagingMode': 'offset'
}, '7 字段交集 + COUNT exact')
measure({
    'machineBrand': 'CATERPILLAR',
    'machineModel': '320D',
    'modelName': '320D',
    'engineBrand': 'CATERPILLAR',
    'engineType': 'C6.4',
    'oemBrand': 'BOSCH',
    'oem3Batch': 'P550425,LF9009',
    'countMode': 'none',
    'pagingMode': 'offset'
}, '7 字段交集 + COUNT none')

# 测 6: cursor 翻页
print('\n=== cursor 翻页 (keyset O(1)) ===')
t0 = time.perf_counter()
r = requests.get(f'{API}/api/admin/products/search', params={
    'countMode': 'none', 'pagingMode': 'cursor', 'pageSize': 50
})
data = r.json()
cursor = data['nextCursor']
items_p1 = data['items']
print(f'  page 1: {(time.perf_counter() - t0)*1000:.0f}ms, items={len(items_p1)}, cursor={bool(cursor)}')

if cursor:
    t0 = time.perf_counter()
    r = requests.get(f'{API}/api/admin/products/search', params={
        'countMode': 'none', 'pagingMode': 'cursor', 'pageSize': 50, 'cursor': cursor
    })
    data = r.json()
    print(f'  page 2: {(time.perf_counter() - t0)*1000:.0f}ms, items={len(data["items"])}, hasMore={data["hasMore"]}')

# 测 7: offset 翻页 (page=100) - 验证深翻页
print('\n=== offset 翻页 (深翻页 page=100, 5000 条后) ===')
t0 = time.perf_counter()
r = requests.get(f'{API}/api/admin/products/search', params={
    'countMode': 'none', 'pagingMode': 'offset', 'pageSize': 50, 'page': 100
})
data = r.json()
print(f'  page=100 pageSize=50: {(time.perf_counter() - t0)*1000:.0f}ms, items={len(data["items"])}, hasMore={data["hasMore"]}')

print('\n========== 性能基准完成 ==========')
