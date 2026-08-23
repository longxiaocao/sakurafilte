"""Day 8.3 基准测试: 100K 数据下验证 Day 8.2.2 优化效果

对比维度:
  1) countMode 三态对比: exact / estimated / none
  2) pagingMode 二态对比: offset / cursor (深翻页 page=10/20/50)
  3) 17 字段全开 vs 仅文本 vs 仅尺寸 vs 仅机器应用

数据规模: 101,568 products + 1.34M xref + 1.66M apps

每个场景跑 3 轮取中位数, 避免冷热缓存干扰
"""
import json
import statistics
import time
import requests

API = 'http://localhost:5000'

# 跑测试前先预热一次 (避免 JIT/缓存冷启动影响第一轮)
requests.get(f'{API}/api/admin/products/search', params={'pageSize': 1})


def hit(params, repeats=3):
    """跑 N 轮取中位数 + 最小值"""
    times = []
    for _ in range(repeats):
        t0 = time.perf_counter()
        r = requests.get(f'{API}/api/admin/products/search', params=params)
        dt = (time.perf_counter() - t0) * 1000
        assert r.status_code == 200, f'{params} → {r.status_code} {r.text[:200]}'
        times.append(dt)
    return {
        'median': round(statistics.median(times), 1),
        'min': round(min(times), 1),
        'max': round(max(times), 1),
        'total': r.json().get('total'),
        'items': len(r.json().get('items', []))
    }


def fmt(r):
    return f"med={r['median']:>6.1f}ms  min={r['min']:>6.1f}ms  total={r.get('total'):>8}  items={r['items']}"


print('=' * 90)
print('Day 8.3 基准: 101,568 products + 1.34M xref + 1.66M apps')
print('=' * 90)

# ========== 1) countMode 三态对比 (无其他条件, 测 COUNT 本身开销) ==========
print('\n[1] countMode 对比 (全表 count, 无其他条件)')
print('-' * 90)
for mode in ['none', 'estimated', 'exact']:
    r = hit({'pagingMode': 'cursor', 'countMode': mode, 'pageSize': 20})
    print(f'  countMode={mode:<10}  {fmt(r)}')

# ========== 2) pagingMode offset vs cursor (中等条件) ==========
print('\n[2] pagingMode offset vs cursor (type=oil + isPublished=true)')
print('-' * 90)
for mode in ['offset', 'cursor']:
    r = hit({'pagingMode': mode, 'countMode': 'exact', 'pageSize': 50, 'type': 'oil', 'isPublished': 'true'})
    print(f'  pagingMode={mode:<7}  {fmt(r)}')

# ========== 3) 深翻页: offset 越翻越慢, cursor 应保持 O(1) ==========
print('\n[3] 深翻页对比 (type=oil, 测 page=1/10/20/50)')
print('-' * 90)
for mode in ['offset', 'cursor']:
    for page in [1, 10, 20, 50]:
        params = {'pagingMode': mode, 'countMode': 'none', 'pageSize': 50, 'type': 'oil'}
        if mode == 'offset':
            params['page'] = page
        # cursor 模式: 走 page-1 次 nextCursor 翻页累积
        if mode == 'cursor' and page > 1:
            cursor = None
            for _ in range(page - 1):
                p = {'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 50, 'type': 'oil'}
                if cursor:
                    p['cursor'] = cursor
                r = requests.get(f'{API}/api/admin/products/search', params=p).json()
                cursor = r.get('nextCursor')
                if not cursor:
                    break
            params['cursor'] = cursor
        r = hit(params)
        print(f'  {mode:<7} page={page:<3}  {fmt(r)}')

# ========== 4) 17 字段全开 (压力最大场景) ==========
print('\n[4] 17 字段全开 (type + 尺寸 + 机型 + xref + 发布状态)')
print('-' * 90)
heavy = {
    'pagingMode': 'cursor', 'countMode': 'none', 'pageSize': 50,
    'type': 'oil', 'isPublished': 'true',
    'd1Min': 50, 'd1Max': 200, 'sizeTolerance': 5,
    'h1Min': 50, 'h1Max': 300, 'sizeTolerance': 5,
    'machineBrand': 'MANN', 'machineModel': 'W',
    'oem3Batch': 'OEM3-001,OEM3-002,OEM3-003',
    'oemBrand': 'MANN',
    'mediaName': 'Cell',
    'productName1': 'Filter',
}
r = hit(heavy, repeats=3)
print(f'  17 字段全开     {fmt(r)}')

# ========== 5) 单维度对比: 仅文本 / 仅尺寸 / 仅机型 ==========
print('\n[5] 单维度 vs 组合维度')
print('-' * 90)
scenarios = [
    ('仅文本 type=oil', {'type': 'oil', 'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50}),
    ('仅尺寸 D1∈[50,200]', {'d1Min': 50, 'd1Max': 200, 'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50}),
    ('仅机型 MB=MANN', {'machineBrand': 'MANN', 'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50}),
    ('仅 xref oem3', {'oem3Batch': 'OEM3-001', 'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50}),
    ('文本+尺寸', {'type': 'oil', 'd1Min': 50, 'd1Max': 200, 'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50}),
    ('文本+机型', {'type': 'oil', 'machineBrand': 'MANN', 'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50}),
    ('文本+机型+xref', {'type': 'oil', 'machineBrand': 'MANN', 'oem3Batch': 'OEM3-001',
                       'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50}),
]
for name, p in scenarios:
    r = hit(p, repeats=3)
    print(f'  {name:<22}  {fmt(r)}')

# ========== 6) 冷热缓存: 同一查询连跑 5 次, 看是否稳定 ==========
print('\n[6] 同一查询 5 次连续执行 (查 PG 缓存复用率)')
print('-' * 90)
p = {'pagingMode': 'cursor', 'countMode': 'exact', 'pageSize': 50, 'type': 'oil', 'isPublished': 'true'}
times = []
for i in range(5):
    t0 = time.perf_counter()
    requests.get(f'{API}/api/admin/products/search', params=p)
    dt = (time.perf_counter() - t0) * 1000
    times.append(dt)
    print(f'  run {i+1}: {dt:>6.1f}ms')
print(f'  5 次: med={statistics.median(times):.1f}ms  min={min(times):.1f}ms  max={max(times):.1f}ms')

print('\n' + '=' * 90)
print('基准测试完成')
print('=' * 90)
