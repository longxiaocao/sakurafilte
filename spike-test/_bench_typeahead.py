#!/usr/bin/env python3
# 复测 typeahead 各字段延迟 (验证 P95<100ms 红线 + 高基数字段守卫短路)
# 响应格式: {"count":N,"items":[...]}
# 重构说明 (2026-08-20): 原 benchmark 逻辑在模块顶层, import 即执行, 无法被 CI 复用。
#   现改为 main() + __main__ 守卫, 支持 --base / --json, 供 perf_regression.py 调用。
import json, time, urllib.request, statistics, sys, argparse, urllib.parse

BASE = "http://localhost:55148"

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

def call(field, q, base=BASE):
    url = f"{base}/api/public/typeahead/{field}?q={urllib.parse.quote(q)}&limit=20"
    t0 = time.perf_counter()
    req = urllib.request.Request(url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        body = r.read().decode()
        dt = (time.perf_counter() - t0) * 1000.0
    try:
        d = json.loads(body)
        cnt = d.get("count", len(d.get("items", [])))
    except Exception:
        cnt = -1
    return dt, cnt

def bench(rounds, base=BASE):
    print(f"{'field':<14} {'rounds':>6} {'p50(ms)':>9} {'p95(ms)':>9} {'max(ms)':>9} {'items':>7}")
    results = {}
    all_ok = True
    for field, q in FIELDS.items():
        lats, items = [], []
        for _ in range(rounds):
            dt, cnt = call(field, q, base)
            lats.append(dt)
            items.append(cnt)
        p50 = statistics.median(lats)
        p95 = sorted(lats)[max(0, int(rounds * 0.95) - 1)]
        mx = max(lats)
        last_items = items[-1]
        flag = "OK" if mx < 100 else "!! >100ms"
        if mx >= 100:
            all_ok = False
        print(f"{field:<14} {rounds:>6} {p50:>9.1f} {p95:>9.1f} {mx:>9.1f} {last_items:>7}  {flag}")
        results[field] = {"p50_ms": round(p50, 1), "p95_ms": round(p95, 1),
                          "max_ms": round(mx, 1), "last_items": last_items, "ok": mx < 100}
    print("\n结论:", "全部 <100ms ✅" if all_ok else "存在 >100ms 字段 ❌")
    return results, all_ok

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('rounds', type=int, nargs='?', default=12, help='每字段轮数')
    ap.add_argument('--base', default=BASE, help='backend base URL')
    ap.add_argument('--json', default=None, help='输出 JSON 结果路径')
    args = ap.parse_args()
    results, ok = bench(args.rounds, args.base)
    if args.json:
        json.dump({"fields": results, "all_ok": ok}, open(args.json, 'w'), indent=1)
        print(f"结果已写入 {args.json}")
    sys.exit(0 if ok else 1)
