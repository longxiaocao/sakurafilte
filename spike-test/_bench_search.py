# -*- coding: utf-8 -*-
"""Day 10+ P1.4 Search 性能基准 (Task 5)

目的: 建立可重复执行的搜索性能基准, 输出 P50/P95/P99 延迟表, 验证:
  - 搜索接口 P95 < 200ms (1M products + 5M xref 规模下)
  - Typeahead P95 < 100ms (字典自动补全)
  - 退化告警: 比 baseline 慢 30% 触发

覆盖查询类别 (8 类, 共 50 个):
  1) OEM 模糊 (Q LIKE '%xxx%')
  2) OEM 精确 (Q = 'xxx')
  3) Type 分类 (filter by type=oil)
  4) 尺寸 H1 ±5mm (默认容差)
  5) 尺寸 H1 ±10mm (宽容差)
  6) machine brand (filter, 走 /api/admin/products/search)
  7) machine model (filter, 走 /api/admin/products/search)
  8) 全文搜索 (Q 命中 remark / product_name)

并发模式: 1 (warmup) / 10 / 50 / 100, 输出 P50/P95/P99
typeahead: 单独测, 用 /api/admin/dict/oem-brands/typeahead

依赖:
  - 后端跑在 http://localhost:5148 (默认)
  - X-Admin-Token 匹配 appsettings.json:Auth:DevStaticToken
  - PG 数据库 spike_test_v3 已有 1M products + 5M xref

使用:
  # 默认: 1/10/50/100 并发各跑一遍
  python _bench_search.py

  # 只跑 warmup
  python _bench_search.py --warmup-only

  # 只跑指定并发
  python _bench_search.py --concurrency=10

  # CI: 阈值检查, 超阈打印 ::error:: 注解 + 退出码 1
  python _bench_search.py --concurrency=10 --threshold-p95=200 --threshold-typeahead-p95=100
"""
import argparse
import json
import os
import statistics
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed

BASE = os.environ.get("BASE_URL", "http://localhost:5148")
TOKEN = os.environ.get(
    "ADMIN_TOKEN", "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
)
H_ADMIN = {"X-Admin-Token": TOKEN, "Content-Type": "application/json"}

# 输出文件
RESULTS_FILE = os.path.join(os.path.dirname(__file__), "_bench_results.json")

# ========== HTTP 客户端 (与 _test_day10_oem_brands.py 风格一致) ==========
def http(method, path, body=None, headers=None, timeout=5):
    """统一 HTTP 客户端."""
    url = BASE + path
    h = {"Content-Type": "application/json"}
    if headers:
        h.update(headers)
    data = None
    if body is not None:
        if isinstance(body, (bytes, bytearray)):
            data = bytes(body)
        elif isinstance(body, str):
            data = body.encode("utf-8")
        else:
            data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        r = urllib.request.urlopen(req, timeout=timeout)
        return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")
    except Exception as e:
        # 超时 / 连接错误 → 返 599
        return 599, str(e)


# ========== 50 个查询 ==========
# WHY 50: 8 类 × 6-7 个, 覆盖主要真实使用场景
#   1) OEM 模糊 (Q LIKE '%xxx%')        7 个
#   2) OEM 精确 (Q = 'xxx')             6 个
#   3) Type 分类 (Type=oil)              6 个
#   4) 尺寸 H1 ±5mm                      6 个
#   5) 尺寸 H1 ±10mm                     6 个
#   6) machine brand filter              6 个
#   7) machine model filter              6 个
#   8) 全文搜索 (Q 命中 remark)          7 个
# 合计: 7+6+6+6+6+6+6+7 = 50

QUERIES = [
    # ===== 1) OEM 模糊 (7) =====
    #   走 POST /api/search, Q LIKE '%xxx%'
    ("oem_fuzzy", "P02", None, None, None, None, 5),
    ("oem_fuzzy", "1142", None, None, None, None, 5),
    ("oem_fuzzy", "A0", None, None, None, None, 5),
    ("oem_fuzzy", "XYZ", None, None, None, None, 5),
    ("oem_fuzzy", "B0", None, None, None, None, 5),
    ("oem_fuzzy", "K9", None, None, None, None, 5),
    ("oem_fuzzy", "M5", None, None, None, None, 5),
    # ===== 2) OEM 精确 (6) =====
    ("oem_exact", "P0272887", None, None, None, None, 5),
    ("oem_exact", "P0825818", None, None, None, None, 5),
    ("oem_exact", "P0650835", None, None, None, None, 5),
    ("oem_exact", "P0937936", None, None, None, None, 5),
    ("oem_exact", "P0817122", None, None, None, None, 5),
    ("oem_exact", "A1234567", None, None, None, None, 5),
    # ===== 3) Type 分类 (6) =====
    ("type_filter", None, "Oil", None, None, None, 5),
    ("type_filter", None, "Fuel", None, None, None, 5),
    ("type_filter", None, "Air", None, None, None, 5),
    ("type_filter", None, "Hydraulic", None, None, None, 5),
    ("type_filter", None, "Coolant", None, None, None, 5),
    ("type_filter", None, "Cabin", None, None, None, 5),
    # ===== 4) H1 ±5mm (6) =====
    ("size_h1_5mm", None, None, None, None, 100, 5),
    ("size_h1_5mm", None, None, None, None, 150, 5),
    ("size_h1_5mm", None, None, None, None, 200, 5),
    ("size_h1_5mm", None, None, None, None, 250, 5),
    ("size_h1_5mm", None, None, None, None, 300, 5),
    ("size_h1_5mm", None, None, None, None, 75, 5),
    # ===== 5) H1 ±10mm (6) =====
    ("size_h1_10mm", None, None, None, None, 100, 10),
    ("size_h1_10mm", None, None, None, None, 150, 10),
    ("size_h1_10mm", None, None, None, None, 200, 10),
    ("size_h1_10mm", None, None, None, None, 250, 10),
    ("size_h1_10mm", None, None, None, None, 300, 10),
    ("size_h1_10mm", None, None, None, None, 75, 10),
    # ===== 6) machine brand (6) — 走 /api/admin/products/search =====
    ("machine_brand", None, None, "Komatsu", None, None, 5),
    ("machine_brand", None, None, "Deere", None, None, 5),
    ("machine_brand", None, None, "Volvo", None, None, 5),
    ("machine_brand", None, None, "Caterpillar", None, None, 5),
    ("machine_brand", None, None, "Hitachi", None, None, 5),
    ("machine_brand", None, None, "KOM", None, None, 5),  # 模糊前缀
    # ===== 7) machine model (6) — 走 /api/admin/products/search =====
    ("machine_model", None, None, None, "PC200", None, 5),
    ("machine_model", None, None, None, "320D", None, 5),
    ("machine_model", None, None, None, "EC210", None, 5),
    ("machine_model", None, None, None, "L150", None, 5),
    ("machine_model", None, None, None, "EX200", None, 5),
    ("machine_model", None, None, None, "SANY", None, 5),
    # ===== 8) 全文搜索 (7) — Q 命中 remark / product_name =====
    #   本地数据 remark 多数为 NULL, 用 product_name 模糊测试
    ("fulltext", "FILTER", None, None, None, None, 5),
    ("fulltext", "TEST", None, None, None, None, 5),
    ("fulltext", "SPARE", None, None, None, None, 5),
    ("fulltext", "OEM", None, None, None, None, 5),
    ("fulltext", "REPLACEMENT", None, None, None, None, 5),
    ("fulltext", "ORIGINAL", None, None, None, None, 5),
    ("fulltext", "QUALITY", None, None, None, None, 5),
]

# Typeahead 查询 (字典自动补全)
# 7 个常见前缀, 测 P50/P95/P99
TYPEAHEAD_QUERIES = [
    "B", "C", "D", "F", "H", "K", "M"
]


# ========== 查询构造与执行 ==========
def build_query_body(q_type, q_text, type_val, machine_brand, machine_model, h1_val, tolerance):
    """根据查询类型构造 POST /api/search body 或 admin query string.
    Returns: (method, path, body, headers) or (method, path, query_string, headers) for admin.
    6-7 个 machine 类别走 admin, 其它走 public /api/search.
    """
    body = {
        "page": 1,
        "pageSize": 20,
        "includeDiscontinued": False,
    }
    if q_text is not None:
        body["q"] = q_text
    if type_val is not None:
        body["type"] = type_val
    if h1_val is not None:
        body["h1"] = h1_val
    body["tolerance"] = tolerance or 5
    return ("POST", "/api/search", body, None)


def build_admin_query(machine_brand, machine_model):
    """构造 admin search URL (GET /api/admin/products/search)."""
    qs = {"page": 1, "pageSize": 20, "countMode": "none"}
    if machine_brand is not None:
        qs["machineBrand"] = machine_brand
    if machine_model is not None:
        qs["machineModel"] = machine_model
    return ("GET", "/api/admin/products/search?" + urllib.parse.urlencode(qs), None, H_ADMIN)


def execute_query(item):
    """执行单个搜索查询, 返回耗时 (ms) 或 -1 (失败)."""
    q_type, q_text, type_val, m_brand, m_model, h1, tol = item
    t0 = time.perf_counter()
    if q_type in ("machine_brand", "machine_model"):
        method, path, body, headers = build_admin_query(m_brand, m_model)
    else:
        method, path, body, headers = build_query_body(q_type, q_text, type_val, m_brand, m_model, h1, tol)
    code, _ = http(method, path, body=body, headers=headers, timeout=10)
    elapsed_ms = (time.perf_counter() - t0) * 1000
    if code != 200:
        return elapsed_ms, code
    return elapsed_ms, 200


def execute_typeahead(q_text):
    """执行 typeahead 查询, 返回耗时 (ms)."""
    t0 = time.perf_counter()
    qs = urllib.parse.urlencode({"q": q_text, "limit": 20})
    code, _ = http("GET", f"/api/admin/dict/oem-brands/typeahead?{qs}",
                   headers=H_ADMIN, timeout=5)
    elapsed_ms = (time.perf_counter() - t0) * 1000
    if code != 200:
        return elapsed_ms, code
    return elapsed_ms, 200


# ========== 并发执行 ==========
def run_concurrent(queries, concurrency, label):
    """在指定并发下跑所有查询 1 遍, 返回 (latencies, errors) 元组.
    latencies: 全部单请求耗时列表 (ms)
    errors: 失败请求列表 (code, query)
    """
    latencies = []
    errors = []
    with ThreadPoolExecutor(max_workers=concurrency) as executor:
        futures = {executor.submit(execute_query, q): q for q in queries}
        for fut in as_completed(futures):
            elapsed, code = fut.result()
            latencies.append(elapsed)
            if code != 200:
                errors.append((code, futures[fut]))
    return latencies, errors


def run_typeahead_concurrent(queries, concurrency):
    """并发跑 typeahead 查询."""
    latencies = []
    errors = []
    with ThreadPoolExecutor(max_workers=concurrency) as executor:
        futures = {executor.submit(execute_typeahead, q): q for q in queries}
        for fut in as_completed(futures):
            elapsed, code = fut.result()
            latencies.append(elapsed)
            if code != 200:
                errors.append((code, futures[fut]))
    return latencies, errors


# ========== 分位计算 ==========
def quantiles(values):
    """计算 P50/P95/P99 延迟 (ms), 输入为数值列表."""
    if not values:
        return {"p50_ms": 0, "p95_ms": 0, "p99_ms": 0, "min_ms": 0, "max_ms": 0, "count": 0}
    s = sorted(values)
    n = len(s)
    # 限制 n=1 的边界
    p50 = s[max(0, int(n * 0.50))] if n > 1 else s[0]
    p95 = s[min(n - 1, int(n * 0.95))] if n > 1 else s[0]
    p99 = s[min(n - 1, int(n * 0.99))] if n > 1 else s[0]
    return {
        "p50_ms": round(p50, 1),
        "p95_ms": round(p95, 1),
        "p99_ms": round(p99, 1),
        "min_ms": round(min(s), 1),
        "max_ms": round(max(s), 1),
        "count": n,
    }


# ========== 健康检查 ==========
def health_check():
    """验证后端 /api/search/health 返 200."""
    code, body = http("GET", "/api/search/health", timeout=5)
    if code != 200:
        print(f"  ::error::Search health check failed: {code} {body[:200]}")
        return False
    print(f"  ✓ /api/search/health: {code} {body[:120]}")
    return True


# ========== 主流程 ==========
def main():
    parser = argparse.ArgumentParser(description="Search performance benchmark (P1.4)")
    parser.add_argument(
        "--concurrency", type=int, default=None,
        help="只跑指定并发档 (1/10/50/100), 省略则跑全部"
    )
    parser.add_argument(
        "--warmup-only", action="store_true",
        help="只跑 1 并发 warmup, 不发实际并发请求 (验证脚本启动)"
    )
    parser.add_argument(
        "--threshold-p95", type=float, default=200.0,
        help="搜索 P95 阈值 (ms), 默认 200, 超过 ::error:: + exit 1"
    )
    parser.add_argument(
        "--threshold-typeahead-p95", type=float, default=100.0,
        help="Typeahead P95 阈值 (ms), 默认 100, 超过 ::error:: + exit 1"
    )
    parser.add_argument(
        "--skip-typeahead", action="store_true",
        help="跳过 typeahead 基准 (CI 默认测, 本地可省)"
    )
    parser.add_argument(
        "--out", default=RESULTS_FILE,
        help=f"结果 JSON 输出路径, 默认 {RESULTS_FILE}"
    )
    args = parser.parse_args()

    print("=" * 80)
    print("Day 10+ P1.4 Search 性能基准 (Task 5)")
    print(f"  BASE = {BASE}")
    print(f"  queries = {len(QUERIES)} (8 类)")
    print(f"  threshold P95 search = {args.threshold_p95}ms")
    print(f"  threshold P95 typeahead = {args.threshold_typeahead_p95}ms")
    print("=" * 80)

    # 1) 健康检查
    print("\n[1] 后端健康检查 ...")
    if not health_check():
        return 2

    # 2) 决定并发档
    if args.warmup_only:
        concurrency_list = [1]
    elif args.concurrency is not None:
        concurrency_list = [args.concurrency]
    else:
        concurrency_list = [1, 10, 50, 100]

    # 3) 跑各并发档
    results = {
        "meta": {
            "base": BASE,
            "queries": len(QUERIES),
            "typeahead_queries": len(TYPEAHEAD_QUERIES),
            "concurrency_list": concurrency_list,
            "thresholds": {
                "search_p95_ms": args.threshold_p95,
                "typeahead_p95_ms": args.threshold_typeahead_p95,
            },
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        },
        "concurrency": {},
        "typeahead": {},
    }

    print("\n[2] 跑搜索并发基准 ...")
    for cc in concurrency_list:
        print(f"\n  --- concurrency={cc} ---")
        latencies, errors = run_concurrent(QUERIES, cc, f"c{cc}")
        q = quantiles(latencies)
        results["concurrency"][f"concurrency_{cc}"] = q
        results["concurrency"][f"concurrency_{cc}"]["errors"] = len(errors)
        print(
            f"    P50={q['p50_ms']:7.1f}ms | P95={q['p95_ms']:7.1f}ms | "
            f"P99={q['p99_ms']:7.1f}ms | n={q['count']} | errors={len(errors)}"
        )
        if errors and cc <= 10:
            # 高并发下偶发 429 限流不打印, 减少噪音
            for code, item in errors[:3]:
                print(f"    [error] code={code} query={item[0]}")

    # 4) Typeahead (单独测, 用 10 并发, 50 个请求 = 7 query × 7 轮)
    if not args.skip_typeahead:
        print("\n[3] 跑 typeahead 基准 (并发=10, 50 请求) ...")
        # 7 query × 7 轮 = 49 个, 实际 7 × 7 ≈ 50
        typeahead_pool = TYPEAHEAD_QUERIES * 7
        latencies, errors = run_typeahead_concurrent(typeahead_pool, 10)
        tq = quantiles(latencies)
        results["typeahead"] = {
            "p50_ms": tq["p50_ms"],
            "p95_ms": tq["p95_ms"],
            "p99_ms": tq["p99_ms"],
            "min_ms": tq["min_ms"],
            "max_ms": tq["max_ms"],
            "count": tq["count"],
            "errors": len(errors),
            "concurrency": 10,
        }
        print(
            f"    P50={tq['p50_ms']:7.1f}ms | P95={tq['p95_ms']:7.1f}ms | "
            f"P99={tq['p99_ms']:7.1f}ms | n={tq['count']} | errors={len(errors)}"
        )

    # 5) 输出 JSON
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    print(f"\n[4] 结果已写入: {args.out}")

    # 6) 控制台表格汇总
    print("\n" + "=" * 80)
    print("汇总表 (P50/P95/P99 ms)")
    print("=" * 80)
    print(f"{'并发档':<14} | {'P50':>8} | {'P95':>8} | {'P99':>8} | {'errors':>7}")
    print("-" * 50)
    for cc in concurrency_list:
        key = f"concurrency_{cc}"
        r = results["concurrency"][key]
        print(
            f"{key:<14} | {r['p50_ms']:>7.1f} | {r['p95_ms']:>7.1f} | "
            f"{r['p99_ms']:>7.1f} | {r['errors']:>7}"
        )
    if results.get("typeahead"):
        t = results["typeahead"]
        print(
            f"{'typeahead-10':<14} | {t['p50_ms']:>7.1f} | {t['p95_ms']:>7.1f} | "
            f"{t['p99_ms']:>7.1f} | {t['errors']:>7}"
        )

    # 7) 阈值检查 (CI gate)
    failed = []
    # 检查 P95 搜索: 用 concurrency_10 档 (CI 标准档, 不打爆后端)
    cc_for_check = 10 if 10 in concurrency_list else concurrency_list[0]
    key = f"concurrency_{cc_for_check}"
    p95 = results["concurrency"][key]["p95_ms"]
    if p95 > args.threshold_p95:
        msg = f"P1.4 FAIL: search P95 ({p95}ms) > threshold ({args.threshold_p95}ms) at concurrency={cc_for_check}"
        print(f"\n::error::{msg}")
        failed.append(("search", p95, args.threshold_p95))

    if results.get("typeahead"):
        tp95 = results["typeahead"]["p95_ms"]
        if tp95 > args.threshold_typeahead_p95:
            msg = f"P1.4 FAIL: typeahead P95 ({tp95}ms) > threshold ({args.threshold_typeahead_p95}ms)"
            print(f"\n::error::{msg}")
            failed.append(("typeahead", tp95, args.threshold_typeahead_p95))

    if failed:
        print(f"\n=== FAIL: {len(failed)} 项超过阈值 ===")
        for name, actual, threshold in failed:
            print(f"  - {name}: {actual}ms > {threshold}ms")
        return 1

    print("\n=== PASS: 所有阈值达标 ===")
    return 0


if __name__ == "__main__":
    sys.exit(main())
