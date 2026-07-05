"""P2-3: 快速压测 (Python 异步)
   目标: 50 并发, 1000 次公开搜索, 验证 P95 < 200ms
   运行: python spike-test/perf/quick_stress.py [base_url] [concurrency] [total_requests]
"""
import asyncio
import time
import sys
import statistics
from urllib.request import urlopen
from urllib.parse import urlencode

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5148"
CONCURRENCY = int(sys.argv[2]) if len(sys.argv) > 2 else 50
TOTAL = int(sys.argv[3]) if len(sys.argv) > 3 else 1000

KEYWORDS = ['AC', 'OC', 'OF', 'OEM-1', 'OEM-2', 'MR.1', 'filter']
PRODUCT_IDS = [1001, 5001, 10000, 20000, 30000, 40000, 49960]

latencies = []
errors = 0

async def fetch_search(session, idx):
    global errors
    kw = KEYWORDS[idx % len(KEYWORDS)]
    # 公开搜索是 8 字段查询, 用 oemNo3 作为测试字段
    url = f"{BASE}/api/public/search?oemNo3={kw}&pageSize=20"
    t0 = time.time()
    try:
        loop = asyncio.get_event_loop()
        resp = await loop.run_in_executor(None, lambda: urlopen(url, timeout=5))
        status = resp.status
        data = resp.read()
    except Exception as e:
        errors += 1
        return
    t1 = time.time()
    if status != 200:
        errors += 1
        return
    latencies.append((t1 - t0) * 1000)

async def fetch_product(session, idx):
    global errors
    pid = PRODUCT_IDS[idx % len(PRODUCT_IDS)]
    url = f"{BASE}/api/public/product/{pid}"
    t0 = time.time()
    try:
        loop = asyncio.get_event_loop()
        resp = await loop.run_in_executor(None, lambda: urlopen(url, timeout=5))
        status = resp.status
        data = resp.read()
    except Exception as e:
        errors += 1
        return
    t1 = time.time()
    if status not in (200, 404):
        errors += 1
        return
    latencies.append((t1 - t0) * 1000)

async def main():
    print(f"=== SakuraFilter 快速压测 ===")
    print(f"目标: {BASE}  并发: {CONCURRENCY}  总请求: {TOTAL}")
    print(f"开始时间: {time.strftime('%H:%M:%S')}")
    t0 = time.time()

    sem = asyncio.Semaphore(CONCURRENCY)

    async def task(i):
        async with sem:
            if i % 2 == 0:
                await fetch_search(None, i)
            else:
                await fetch_product(None, i)

    tasks = [task(i) for i in range(TOTAL)]
    await asyncio.gather(*tasks)
    elapsed = time.time() - t0

    if not latencies:
        print("ERROR: 无成功请求")
        return

    latencies.sort()
    p50 = latencies[int(len(latencies) * 0.5)]
    p95 = latencies[int(len(latencies) * 0.95)]
    p99 = latencies[int(len(latencies) * 0.99)]
    rps = len(latencies) / elapsed
    err_rate = errors / TOTAL * 100

    print(f"\n=== 结果 ===")
    print(f"耗时: {elapsed:.2f}s")
    print(f"成功: {len(latencies)}  失败: {errors}  错误率: {err_rate:.3f}%")
    print(f"RPS: {rps:.1f}")
    print(f"P50: {p50:.1f}ms  P95: {p95:.1f}ms  P99: {p99:.1f}ms")
    print(f"AVG: {statistics.mean(latencies):.1f}ms  MAX: {max(latencies):.1f}ms")

    # 验收
    if p95 < 200:
        print(f"\n[PASS] P95={p95:.1f}ms < 200ms ✓")
    else:
        print(f"\n[WARN] P95={p95:.1f}ms >= 200ms (建议优化)")

if __name__ == "__main__":
    asyncio.run(main())
