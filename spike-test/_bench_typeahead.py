#!/usr/bin/env python3
# 复测 typeahead 各字段延迟 (验证 P95<100ms 红线 + 高基数字段守卫短路)
# 响应格式: {"count":N,"items":[...]}
import json, time, urllib.request, statistics, sys

BASE = "http://localhost:55148"
ROUNDS = int(sys.argv[1]) if len(sys.argv) > 1 else 12

# field -> 取样 query (尽量命中, 也覆盖高基数字段)
FIELDS = {
    "oem-brand": "bo",
    "oem-no2": "AB",
    "oem-no3": "TX",        # 守卫: 近唯一, 应瞬时返空
    "machine-brand": "ka",  # dict 无 'ka' 品牌 -> 返空, 但不应回退大表
    "machine-model": "PC",
    "model-name": "ZX",
    "engine-brand": "ko",
    "engine-type": "D1",    # 守卫: 近唯一, 应瞬时返空
}

def call(field, q):
    url = f"{BASE}/api/public/typeahead/{field}?q={urllib.parse.quote(q)}&limit=20"
    t0 = time.perf_counter()
    req = urllib.request.Request(url, headers={"Accept":"application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        body = r.read().decode()
        dt = (time.perf_counter() - t0) * 1000.0
    try:
        d = json.loads(body)
        cnt = d.get("count", len(d.get("items", [])))
    except Exception:
        cnt = -1
    return dt, cnt

import urllib.parse

print(f"{'field':<14} {'rounds':>6} {'p50(ms)':>9} {'p95(ms)':>9} {'max(ms)':>9} {'items':>7}")
all_ok = True
for field, q in FIELDS.items():
    lats, items = [], []
    for _ in range(ROUNDS):
        dt, cnt = call(field, q)
        lats.append(dt)
        items.append(cnt)
    p50 = statistics.median(lats)
    p95 = sorted(lats)[max(0, int(ROUNDS*0.95)-1)]
    mx = max(lats)
    last_items = items[-1]
    flag = "OK" if mx < 100 else "!! >100ms"
    if mx >= 100:
        all_ok = False
    print(f"{field:<14} {ROUNDS:>6} {p50:>9.1f} {p95:>9.1f} {mx:>9.1f} {last_items:>7}  {flag}")

print("\n结论:", "全部 <100ms ✅" if all_ok else "存在 >100ms 字段 ❌")
