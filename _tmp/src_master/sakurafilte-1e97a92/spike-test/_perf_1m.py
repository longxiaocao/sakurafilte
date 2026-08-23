# -*- coding: utf-8 -*-
"""1M products 性能测试 + Meili 同步验证"""
import json, time, urllib.request, urllib.error

TOKEN = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'
H = {'X-Admin-Token': TOKEN, 'Content-Type': 'application/json'}
BASE = 'http://localhost:5148'

# 触发
print('=== 1M Products Full-Load (Meili 同步修复后) ===')
req = urllib.request.Request(
    BASE + '/api/etl/import',
    data=json.dumps({'jsonlPath': 'D:/data/sakurafilter/products_1m.jsonl', 'mode': 'full-load'}).encode(),
    headers=H, method='POST')
try:
    r = urllib.request.urlopen(req, timeout=5)
    print('触发: %d' % r.status)
except urllib.error.HTTPError as e:
    if e.code == 409:
        print('已有 ETL 在跑, 等待...')
        time.sleep(10)
        r = urllib.request.urlopen(req, timeout=5)
    else:
        raise

t0 = time.time()
while True:
    time.sleep(2)
    sreq = urllib.request.Request(BASE + '/api/etl/status', headers=H)
    s = json.loads(urllib.request.urlopen(sreq, timeout=3).read())
    elapsed = time.time() - t0
    status = s['status']
    if status != 'running':
        print('完成: status=%s, elapsed=%.1fs' % (status, elapsed))
        print('  read=%d inserted=%d updated=%d skipped=%d errors=%d' % (
            s['read'], s['inserted'], s['updated'], s.get('skipped', 0), s['errors']))
        print('  indexed=%d pending=%d' % (s['indexed'], s['indexPending']))
        if s.get('lastError'):
            print('  lastError: ' + str(s['lastError'])[:300])
        else:
            print('  lastError: (none)')
        # 判定 Meili 同步是否成功
        if s['indexed'] > 0 and s['indexPending'] == 0:
            print('  >>> Meili 同步: 成功 (indexed=%d)' % s['indexed'])
        elif s['indexPending'] > 0:
            print('  >>> Meili 同步: 部分入队 (pending=%d)' % s['indexPending'])
        else:
            print('  >>> Meili 同步: 未执行 (indexed=0)')
        break
    print('  [%.0fs] read=%d inserted=%d stage=%s' % (
        elapsed, s['read'], s['inserted'], s.get('stage', '?')))
    if elapsed > 120:
        print('超时, 取消')
        creq = urllib.request.Request(
            BASE + '/api/admin/etl/task',
            data=json.dumps({'reason': 'timeout'}).encode(),
            headers=H, method='DELETE')
        urllib.request.urlopen(creq, timeout=3)
        break

print('总计: %.1fs' % (time.time() - t0))
