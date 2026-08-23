"""冷缓存压测: 每档全部使用不重复的真实 mr1 查询 (响应缓存按请求签名/富化缓存按 mr1 → 全 miss)
测的是完整链路 (Meili 搜索 + PG 富化 + 响应构建) 在并发下的真实能力, 而非内存缓存查+序列化。
用法: python spike-test/_bench_aggregate_cold.py --rounds 10
"""
import argparse, json, time, urllib.request, subprocess
from concurrent.futures import ThreadPoolExecutor

BASE = 'http://localhost:55148'

def load_pool(limit=2000):
    """从 PG 采样 limit 条唯一 mr1 (真实必命中)"""
    out = subprocess.run(['docker', 'exec', 'sakurafilter-perf-postgres-1', 'sh', '-c',
        f'psql -U $POSTGRES_USER -d $POSTGRES_DB -t -A -c "SELECT mr_1 FROM products WHERE mr_1 IS NOT NULL ORDER BY random() LIMIT {limit};"'],
        capture_output=True, text=True, timeout=120)
    vals = [l.strip() for l in out.stdout.strip().split('\n') if l.strip()]
    return vals

def build_body(mr1):
    return {"page": 1, "pageSize": 20, "q": mr1, "tolerance": 5}

def agg_req(mr1):
    body = json.dumps(build_body(mr1)).encode()
    t = time.perf_counter()
    try:
        urllib.request.urlopen(urllib.request.Request(
            BASE + '/api/public/search/aggregate',
            data=body, headers={'Content-Type': 'application/json'}), timeout=15).read()
        return (time.perf_counter() - t) * 1000, 200
    except urllib.error.HTTPError as e:
        return (time.perf_counter() - t) * 1000, e.code
    except Exception:
        return (time.perf_counter() - t) * 1000, 0

def bench(cc, mr1s):
    tasks = mr1s
    with ThreadPoolExecutor(max_workers=cc) as ex:
        res = list(ex.map(agg_req, tasks))
    lats = sorted(r[0] for r in res); n = len(lats)
    errs = [(r[1], i) for i, r in enumerate(res) if r[1] != 200]
    p50, p95, p99 = lats[int(n*0.5)], lats[min(n-1, int(n*0.95))], lats[min(n-1, int(n*0.99))]
    print(f"cc={cc:3d} | P50={p50:7.1f} | P95={p95:7.1f} | P99={p99:7.1f} | n={n} | errors={len(errs)}")
    return {"p50_ms": round(p50,1), "p95_ms": round(p95,1), "p99_ms": round(p99,1), "count": n, "errors": len(errs)}

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--rounds', type=int, default=10)
    ap.add_argument('--queries-per-round', type=int, default=37)
    args = ap.parse_args()
    pool = load_pool()
    per_cc = args.rounds * args.queries_per_round
    need = per_cc * 4
    print(f"采样池 {len(pool)} 条唯一 mr1 | 每档 {per_cc} 条不重复 | 4 档共需 {need} 条")
    if len(pool) < need:
        print(f"!! 采样池不足 (需 {need}), 降低 rounds 或 queries-per-round 后重跑"); raise SystemExit(1)
    out = {"concurrency": {},
           "note": "冷缓存: 每档全部不重复真实 mr1 查询, 响应缓存(按请求签名)/富化缓存(按 mr1) 全 miss, 测完整链路 (Meili+PG富化+构建)"}
    offset = 0
    for cc in [1, 10, 50, 100]:
        chunk = pool[offset:offset+per_cc]; offset += per_cc
        out["concurrency"][f"concurrency_{cc}"] = bench(cc, chunk)
    json.dump(out, open('spike-test/_bench_aggregate_cold_results.json', 'w'), indent=1)
    print("结果 -> spike-test/_bench_aggregate_cold_results.json")
