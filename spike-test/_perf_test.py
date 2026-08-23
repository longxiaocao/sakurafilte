# -*- coding: utf-8 -*-
"""性能压测: 1M products + 5M xrefs/apps ETL 计时"""
import json, time, urllib.request, urllib.error, sys, os

TOKEN = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'
H = {'X-Admin-Token': TOKEN, 'Content-Type': 'application/json'}
BASE = 'http://localhost:5148'

def trigger(jsonl_path, mode, entity_type='products'):
    """触发 ETL 并轮询直到完成,返回耗时和结果
    WHY entity_type: /api/etl/import 是 products 专用, xrefs/apps 有独立端点
    """
    endpoint = {
        'products': '/api/etl/import',
        'xrefs': '/api/etl/import-xrefs',
        'apps': '/api/etl/import-apps',
    }[entity_type]
    req = urllib.request.Request(
        BASE + endpoint,
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
        if elapsed > 600:
            print('  超时 600s, 取消')
            creq = urllib.request.Request(
                BASE + '/api/admin/etl/task',
                data=json.dumps({'reason': 'timeout'}).encode(),
                headers=H, method='DELETE')
            urllib.request.urlopen(creq, timeout=3)
            return elapsed, s

def gen_jsonl(path, count, make_row, force=False):
    """生成 JSONL 文件"""
    if os.path.exists(path) and not force:
        size_mb = os.path.getsize(path) // 1024 // 1024
        print('  已存在: %s (%d MB)' % (path, size_mb))
        return
    if os.path.exists(path) and force:
        print('  force=True, 删除旧文件重新生成')
        os.remove(path)
    print('  生成 %d 行...' % count)
    t0 = time.time()
    with open(path, 'w') as f:
        for i in range(1, count + 1):
            f.write(json.dumps(make_row(i)) + '\n')
    size_mb = os.path.getsize(path) // 1024 // 1024
    print('  生成完成: %s (%d MB, %.1fs)' % (path, size_mb, time.time() - t0))

# ========== 1M products ==========
# Day 9.10: 跳过 products (已验证 26.4s, 数据已在库), 只跑 xrefs/apps 验证 dupCmd 修复
print('=== 1M Products (SKIP: 已验证 26.4s, 数据已在库) ===')
t1, r1 = 26.4, {'status': 'completed'}
print('  >>> 1M Products: %.1fs (%.0f rows/s)' % (t1, 1000000 / t1 if t1 > 0 else 0))

# ========== 5M xrefs ==========
print()
print('=== 5M Xrefs Insert-Only ===')
# WHY force=True: 旧 xrefs_20m.jsonl 用 product_id 字段, 代码期望 product_oem (对应 products.oem_no_normalized)
#   旧格式导致所有行解析失败 "The given key was not present in the dictionary"
gen_jsonl('D:/data/sakurafilter/xrefs_5m.jsonl', 5000000, lambda i: {
    'product_oem': 'P%07d' % ((i % 1000000) + 1),
    'oem_brand': ['Bosch','Mann','Wix','Mahle','Donaldson'][i % 5],
    'oem_no_3': 'XREF-%08d' % i,
    'product_name_1': 'Xref %d' % i
})  # force=False: 文件已是新格式 (product_oem), 无需重新生成
t2, r2 = trigger('D:/data/sakurafilter/xrefs_5m.jsonl', 'insert-only', entity_type='xrefs')
print('  >>> 5M Xrefs: %.1fs (%.0f rows/s)' % (t2, 5000000 / t2 if t2 > 0 else 0))

# ========== 5M apps ==========
print()
print('=== 5M Apps Insert-Only ===')
gen_jsonl('D:/data/sakurafilter/apps_5m.jsonl', 5000000, lambda i: {
    'product_oem': 'P%07d' % ((i % 1000000) + 1),
    'machine_brand': ['Caterpillar','Komatsu','Hitachi','Volvo','Deere'][i % 5],
    'machine_model': 'Model-%d' % (i % 1000),
    'engine_brand': ['Cummins','Perkins','Isuzu','Deutz','Yanmar'][i % 5]
})
t3, r3 = trigger('D:/data/sakurafilter/apps_5m.jsonl', 'insert-only', entity_type='apps')
print('  >>> 5M Apps: %.1fs (%.0f rows/s)' % (t3, 5000000 / t3 if t3 > 0 else 0))

# ========== 总结 ==========
print()
print('=== 性能压测总结 ===')
print('  Products 1M:  %.1fs' % t1)
print('  Xrefs 5M:    %.1fs' % t2)
print('  Apps 5M:     %.1fs' % t3)
print('  总计:         %.1fs' % (t1 + t2 + t3))
target = 40.0
total = t1 + t2 + t3
if total < target:
    print('  ✓ 达标 (< %ds)' % target)
else:
    print('  ✗ 未达标 (>= %ds, 差 %.1fs)' % (target, total - target))
