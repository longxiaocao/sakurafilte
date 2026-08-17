"""聚合搜索压测 (生产前台主路径 POST /api/public/search/aggregate)
2026-08-16: 前端用户主路径是 /search/aggregate, 旧 /api/search 仅为降级/遗留路径,
  故压测应打 aggregate 端点才反映生产真实性能 (含 EnrichFromPgAsync 富化缓存).
用法: BASE_URL=http://localhost:55148 python spike-test/_bench_aggregate.py [--rounds 10]
"""
import argparse, json, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

BASE = None

def build_body(item):
    q_type, q_text, type_val, m_brand, m_model, h1, tol = item
    body = {"page": 1, "pageSize": 20}
    if q_text is not None: body["q"] = q_text
    if type_val is not None: body["type"] = type_val
    if h1 is not None: body["h1"] = h1
    body["tolerance"] = tol or 5
    return body

def agg_req(item):
    body = build_body(item)
    t = time.perf_counter()
    try:
        urllib.request.urlopen(urllib.request.Request(
            BASE + '/api/public/search/aggregate',
            data=json.dumps(body).encode(), headers={'Content-Type': 'application/json'}), timeout=15).read()
        return (time.perf_counter() - t) * 1000, 200
    except urllib.error.HTTPError as e:
        return (time.perf_counter() - t) * 1000, e.code
    except Exception:
        return (time.perf_counter() - t) * 1000, 0

def bench(cc, rounds):
    qs = json.load(open('spike-test/bench_queries_public.json', encoding='utf-8'))['queries']
    tasks = qs * rounds
    with ThreadPoolExecutor(max_workers=cc) as ex:
        res = list(ex.map(agg_req, tasks))
    lats = sorted(r[0] for r in res); n = len(lats)
    errs = [(r[1], i) for i, r in enumerate(res) if r[1] != 200]
    p50, p95, p99 = lats[int(n*0.5)], lats[min(n-1, int(n*0.95))], lats[min(n-1, int(n*0.99))]
    print(f"concurrency_{cc} | P50={p50:7.1f} | P95={p95:7.1f} | P99={p99:7.1f} | n={n} | errors={len(errs)}")
    return {"p50_ms": round(p50,1), "p95_ms": round(p95,1), "p99_ms": round(p99,1), "count": n, "errors": len(errs)}

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--rounds', type=int, default=10)
    ap.add_argument('--concurrency', type=int, default=None)
    args = ap.parse_args()
    import os
    BASE = os.environ.get('BASE_URL', 'http://localhost:55148')
    cc_list = [args.concurrency] if args.concurrency else [1, 10, 50, 100]
    print(f"aggregate 压测 BASE={BASE} queries=37 x {args.rounds} 轮/档")
    out = {"concurrency": {}}
    for cc in cc_list:
        out["concurrency"][f"concurrency_{cc}"] = bench(cc, args.rounds)
    json.dump(out, open('spike-test/_bench_aggregate_results.json', 'w'), indent=1)
    print("结果已写入 spike-test/_bench_aggregate_results.json")
