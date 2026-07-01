# -*- coding: utf-8 -*-
"""性能压测: 1M products + 20M xrefs/apps ETL 计时"""
import json, time, urllib.request, urllib.error, sys, os

TOKEN = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'
H = {'X-Admin-Token': TOKEN, 'Content-Type': 'application/json'}
BASE = 'http://localhost:5148'

def trigger(jsonl_path, mode):
    """触发 ETL 并轮询直到完成,返回耗时和结果"""
    req = urllib.request.Request(
        BASE + '/api/etl/import',
        data=json.dumps({'jsonlPath': jsonl_path, 'mode': mode}).encode(),
        headers=H, method='POST')
    try:
        r = urllib.request.urlopen(req, timeout=5)
        print('  触发成功: ' + str(r.status))
    except urllib.error.HTTPError as e:
        if e.code == 409:
            print('  已有 ETL 在跑, 等待完成...')
            time.sleep(10)
            r = urllib.request.urlopen(req, timeout=5)
        else:
            raise

    t0 = time.time()
    while True:
        time.sleep(1)
        sreq = urllib.request.Request(BASE + '/api/etl/status', headers=H)
        s = json.loads(urllib.request.urlopen(sreq, timeout=3).read())
        elapsed = time.time() - t0
        status = s['status']
        if status != 'running':
            print('  完成: status=%s, elapsed=%.1fs' % (status, elapsed))
            print('  read=%d inserted=%d updated=%d skipped=%d errors=%d' % (
                s['read'], s['inserted'], s['updated'], s.get('skipped', 0), s['errors']))
            print('  indexed=%d pending=%d' % (s['indexed'], s['indexPending']))
            if s.get('lastError'):
                print('  lastError: ' + str(s['lastError'])[:200])
            return elapsed, s
        pct = s.get('progressPct', '?')
        stage = s.get('stage', '?')
        print('  [%.0fs] read=%d inserted=%d stage=%s pct=%s' % (
            elapsed, s['read'], s['inserted'], stage, pct))
        if elapsed > 120:
            print('  超时 120s, 取消')
            creq = urllib.request.Request(
                BASE + '/api/admin/etl/task',
                data=json.dumps({'reason': 'timeout'}).encode(),
                headers=H, method='DELETE')
            urllib.request.urlopen(creq, timeout=3)
            return elapsed, s

def gen_jsonl(path, count, make_row):
    """生成 JSONL 文件"""
    if os.path.exists(path):
        size_mb = os.path.getsize(path) // 1024 // 1024
        print('  已存在: %s (%d MB)' % (path, size_mb))
        return
    print('  生成 %d 行...' % count)
    t0 = time.time()
    with open(path, 'w') as f:
        for i in range(1, count + 1):
            f.write(json.dumps(make_row(i)) + '\n')
    size_mb = os.path.getsize(path) // 1024 // 1024
    print('  生成完成: %s (%d MB, %.1fs)' % (path, size_mb, time.time() - t0))

# ========== 1M products ==========
print('=== 1M Products Full-Load ===')
gen_jsonl('D:/data/sakurafilter/products_1m.jsonl', 1000000, lambda i: {
    'oem_no_normalized': 'P%07d' % i,
    'oem_no_display': 'P%07d' % i,
    'product_name_1': 'Product %d' % i,
    'type': ['Hydraulic','Air','Fuel','Oil','Coolant'][i % 5],
    'media': ['Synthetic','Cellulose','Glass Fiber','Cotton','Stainless'][i % 5],
    'd1_mm': 30.0 + (i % 100),
    'h1_mm': 100.0 + (i % 200),
    'd2_mm': 50.0 + (i % 80),
    'h2_mm': 150.0 + (i % 150)
})
t1, r1 = trigger('D:/data/sakurafilter/products_1m.jsonl', 'full-load')
print('  >>> 1M Products: %.1fs (%.0f rows/s)' % (t1, 1000000 / t1 if t1 > 0 else 0))

# ========== 20M xrefs ==========
print()
print('=== 20M Xrefs Insert-Only ===')
gen_jsonl('D:/data/sakurafilter/xrefs_20m.jsonl', 20000000, lambda i: {
    'product_id': (i % 1000000) + 1,
    'oem_brand': ['Bosch','Mann','Wix','Mahle','Donaldson'][i % 5],
    'oem_no_3': 'XREF-%08d' % i,
    'product_name_1': 'Xref %d' % i
})
t2, r2 = trigger('D:/data/sakurafilter/xrefs_20m.jsonl', 'insert-only')
print('  >>> 20M Xrefs: %.1fs (%.0f rows/s)' % (t2, 20000000 / t2 if t2 > 0 else 0))

# ========== 20M apps ==========
print()
print('=== 20M Apps Insert-Only ===')
gen_jsonl('D:/data/sakurafilter/apps_20m.jsonl', 20000000, lambda i: {
    'product_id': (i % 1000000) + 1,
    'machine_brand': ['Caterpillar','Komatsu','Hitachi','Volvo','Deere'][i % 5],
    'machine_model': 'Model-%d' % (i % 1000),
    'engine_brand': ['Cummins','Perkins','Isuzu','Deutz','Yanmar'][i % 5]
})
t3, r3 = trigger('D:/data/sakurafilter/apps_20m.jsonl', 'insert-only')
print('  >>> 20M Apps: %.1fs (%.0f rows/s)' % (t3, 20000000 / t3 if t3 > 0 else 0))

# ========== 总结 ==========
print()
print('=== 性能压测总结 ===')
print('  Products 1M:  %.1fs' % t1)
print('  Xrefs 20M:    %.1fs' % t2)
print('  Apps 20M:     %.1fs' % t3)
print('  总计:         %.1fs' % (t1 + t2 + t3))
target = 40.0
total = t1 + t2 + t3
if total < target:
    print('  ✓ 达标 (< %ds)' % target)
else:
    print('  ✗ 未达标 (>= %ds, 差 %.1fs)' % (target, total - target))
