"""v30-27: Meili 压测脚本 (69000 文档)
   目标 1: 单次搜索 P95 < 200ms (20 查询词 × 串行)
   目标 2: 50/100 并发搜索 P95
   目标 3: Offset 深分页性能 (offset=0/1000/5000/10000)

   运行: python spike-test/perf/stress_meili.py [base_url]
"""
import asyncio
import time
import sys
import statistics
import json
import urllib.request
from concurrent.futures import ThreadPoolExecutor

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5148"
# v30-27: 自定义线程池 (默认 ThreadPoolExecutor 线程数受限于 CPU, 高并发时不够)
#   WHY 256: 足够覆盖 100 并发 + 余量, 避免线程池排队导致超时
EXECUTOR = ThreadPoolExecutor(max_workers=256)

# 20 个查询词: 覆盖短词/长词/常见/罕见/数字/混合
KEYWORDS = [
    "oil", "filter", "air", "fuel", "hydraulic",
    "engine", "diesel", "pump", "valve", "gear",
    "M24", "M36", "AC", "HiFi", "Metal",
    "OIL FILTER", "air filter", "fuel pump", "1.5", "x1.5",
]


def search_once(payload):
    """同步搜索, 返回 (elapsed_ms, total, provider)"""
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        f"{BASE}/api/search",
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            body = resp.read().decode("utf-8")
        t1 = time.time()
        r = json.loads(body)
        return (t1 - t0) * 1000, r.get("result", {}).get("total", 0), r.get("provider", "?")
    except Exception as e:
        t1 = time.time()
        return (t1 - t0) * 1000, 0, f"ERR:{type(e).__name__}:{e}"


async def bench_serial(label, payloads, rounds=1):
    """串行压测"""
    print(f"\n=== {label} ===")
    print(f"查询数: {len(payloads) * rounds}")
    latencies = []
    totals = []
    t0 = time.time()
    for r in range(rounds):
        for p in payloads:
            loop = asyncio.get_event_loop()
            ms, total, prov = await loop.run_in_executor(EXECUTOR, search_once, p)
            latencies.append(ms)
            totals.append(total)
    elapsed = time.time() - t0
    return _report(label, latencies, totals, elapsed, len(payloads) * rounds)


async def bench_concurrent(label, payloads, concurrency, total_reqs):
    """并发压测"""
    print(f"\n=== {label} ===")
    print(f"并发: {concurrency}  总请求: {total_reqs}")
    latencies = []
    errors = 0
    first_error = None
    sem = asyncio.Semaphore(concurrency)

    async def task(i):
        p = payloads[i % len(payloads)]
        async with sem:
            loop = asyncio.get_event_loop()
            ms, total, prov = await loop.run_in_executor(EXECUTOR, search_once, p)
            if "ERR" in prov:
                nonlocal errors, first_error
                errors += 1
                if first_error is None:
                    first_error = prov[:200]
            else:
                latencies.append(ms)

    t0 = time.time()
    tasks = [task(i) for i in range(total_reqs)]
    await asyncio.gather(*tasks)
    elapsed = time.time() - t0
    if first_error:
        print(f"首个错误: {first_error}")
    return _report(label, latencies, [0] * len(latencies), elapsed, total_reqs, errors, concurrency)


def _report(label, latencies, totals, elapsed, count, errors=0, concurrency=1):
    if not latencies:
        print("ERROR: 无成功请求")
        return {"label": label, "error": True}

    latencies.sort()
    p50 = latencies[int(len(latencies) * 0.5)]
    p95 = latencies[int(len(latencies) * 0.95)]
    p99 = latencies[min(int(len(latencies) * 0.99), len(latencies) - 1)]
    avg = statistics.mean(latencies)
    mx = max(latencies)
    rps = len(latencies) / elapsed if elapsed > 0 else 0
    err_rate = errors / count * 100 if count > 0 else 0

    # total 范围 (仅串行有意义)
    total_info = ""
    if totals and totals[0] != 0:
        total_info = f"  total范围: {min(totals)}-{max(totals)}"

    print(f"耗时: {elapsed:.2f}s")
    print(f"成功: {len(latencies)}  失败: {errors}  错误率: {err_rate:.3f}%")
    print(f"RPS: {rps:.1f}")
    print(f"P50: {p50:.1f}ms  P95: {p95:.1f}ms  P99: {p99:.1f}ms")
    print(f"AVG: {avg:.1f}ms  MAX: {mx:.1f}ms{total_info}")

    if p95 < 200:
        print(f"[PASS] P95={p95:.1f}ms < 200ms")
    else:
        print(f"[WARN] P95={p95:.1f}ms >= 200ms")

    return {
        "label": label,
        "count": len(latencies),
        "errors": errors,
        "elapsed_s": round(elapsed, 2),
        "rps": round(rps, 1),
        "p50": round(p50, 1),
        "p95": round(p95, 1),
        "p99": round(p99, 1),
        "avg": round(avg, 1),
        "max": round(mx, 1),
        "concurrency": concurrency,
        "pass": p95 < 200,
    }


async def main():
    print(f"=== SakuraFilter Meili 压测 (69000 文档) ===")
    print(f"目标: {BASE}")
    print(f"时间: {time.strftime('%Y-%m-%d %H:%M:%S')}")

    # 预热 (首次 Meili 连接 + schema 加载)
    print("\n--- 预热 ---")
    search_once({"q": "oil", "limit": 1})
    print("预热完成")

    results = []

    # ========== 压测 1: 单次搜索 P95 (20 查询词 × 3 轮 = 60 次) ==========
    payloads = [{"q": kw, "limit": 20} for kw in KEYWORDS]
    results.append(await bench_serial("压测1: 单次搜索 (20词×3轮=60次)", payloads, rounds=3))

    # ========== 压测 2a: 50 并发搜索 (1000 次) ==========
    payloads_c50 = [{"q": kw, "limit": 20} for kw in KEYWORDS]
    results.append(await bench_concurrent("压测2a: 50并发 (1000次)", payloads_c50, 50, 1000))

    # ========== 压测 2b: 100 并发搜索 (1000 次) ==========
    payloads_c100 = [{"q": kw, "limit": 20} for kw in KEYWORDS]
    results.append(await bench_concurrent("压测2b: 100并发 (1000次)", payloads_c100, 100, 1000))

    # ========== 压测 3: Offset 深分页 ==========
    # 69000 文档, offset=0/1000/5000/10000 (limit=20)
    print(f"\n=== 压测3: Offset 深分页 ===")
    offset_results = []
    for offset in [0, 1000, 5000, 10000]:
        # 每个 offset 跑 10 次
        payloads_off = [{"q": "", "limit": 20, "page": offset // 20 + 1}] * 10
        r = await bench_serial(f"  offset={offset} (page={offset // 20 + 1})", payloads_off, rounds=1)
        offset_results.append(r)

    # ========== 汇总 ==========
    print(f"\n{'='*60}")
    print(f"=== 压测汇总 ===")
    print(f"{'='*60}")
    print(f"{'场景':<30} {'P50':>8} {'P95':>8} {'P99':>8} {'MAX':>8} {'RPS':>8} {'结果':>6}")
    print(f"{'-'*30} {'-'*8} {'-'*8} {'-'*8} {'-'*8} {'-'*8} {'-'*6}")
    for r in results:
        if "error" in r:
            print(f"{r['label']:<30} ERROR")
            continue
        status = "PASS" if r["pass"] else "WARN"
        print(f"{r['label']:<30} {r['p50']:>7.1f}m {r['p95']:>7.1f}m {r['p99']:>7.1f}m {r['max']:>7.1f}m {r['rps']:>7.1f} {status:>6}")

    print(f"\n--- Offset 深分页 ---")
    print(f"{'Offset':<15} {'P50':>8} {'P95':>8} {'P99':>8} {'MAX':>8} {'结果':>6}")
    print(f"{'-'*15} {'-'*8} {'-'*8} {'-'*8} {'-'*8} {'-'*6}")
    for r in offset_results:
        if "error" in r:
            continue
        status = "PASS" if r["pass"] else "WARN"
        print(f"{r['label'].strip():<15} {r['p50']:>7.1f}m {r['p95']:>7.1f}m {r['p99']:>7.1f}m {r['max']:>7.1f}m {status:>6}")

    # 保存 JSON 结果
    with open("spike-test/perf/_stress_results.json", "w", encoding="utf-8") as f:
        json.dump({"results": results, "offset": offset_results, "timestamp": time.strftime('%Y-%m-%d %H:%M:%S')}, f, ensure_ascii=False, indent=2)
    print(f"\n结果已保存: spike-test/perf/_stress_results.json")


if __name__ == "__main__":
    asyncio.run(main())
