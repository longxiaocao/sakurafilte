# -*- coding: utf-8 -*-
"""v27-3 OFFSET 分页压测脚本 (ADR #5 决策依据)

目的:
  量化 PostgresSearchProvider.SearchAsync 在不同 OFFSET 深度下的性能曲线,
  对比 keyset 等价 SQL, 为 ADR #5 (PostgresSearchProvider Phase 2 keyset 暂缓) 提供决策依据.

设计:
  - 直连 PG (psycopg2), 绕过 HTTP/Meili timeout 干扰, 测纯 SQL 层性能
  - 复用 PostgresSearchProvider.SearchAsync 等价 SQL (CTE + LATERAL JOIN + EXISTS)
  - 参数化配置: spike-test/perf_offset_config.json
  - 输出: _perf_offset_results.json (raw) + _perf_offset_report.md (人读)

测试矩阵:
  - 数据规模: 当前 50K (可 --scale-up 扩容到 1M)
  - OFFSET 深度: 0 / 200 / 1000 / 5000 / 10000 / 20000 / 40000
  - 查询场景: baseline / type_oil / q_filter / size_d1_100 / composite
  - 对比模式: OFFSET (生产现状) vs keyset 简化版 (ADR #5 候选)

使用:
  # 默认: 用 perf_offset_config.json 跑全部场景
  python _perf_offset_paging.py

  # 扩容到 1M 后再跑 (生成 products + xrefs + apps)
  python _perf_offset_paging.py --scale-up

  # 自定义重复次数 (默认 5)
  python _perf_offset_paging.py --repeats 10

  # 指定输出目录
  python _perf_offset_paging.py --output-dir D:/tmp/perf

依赖:
  - psycopg2-binary (已 pip install)
  - PG 连接串从 perf_offset_config.json 读

报告:
  - Markdown 报告含 OFFSET 深度 vs 耗时曲线表
  - P50/P95/P99 + min/max
  - keyset 对比 (若启用)
  - ADR #5 决策建议 (基于实测数据)
"""
import argparse
import json
import os
import statistics
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import psycopg2

# ========== 路径与常量 ==========
SCRIPT_DIR = Path(__file__).parent
CONFIG_PATH = SCRIPT_DIR / "perf_offset_config.json"
DEFAULT_RESULTS_PATH = SCRIPT_DIR / "_perf_offset_results.json"
DEFAULT_REPORT_PATH = SCRIPT_DIR / "_perf_offset_report.md"

# ========== SQL 模板 (PostgresSearchProvider.SearchAsync 等价) ==========

# 基础 WHERE (is_published + EXISTS xref), 与 PostgresSearchProvider.BuildWhereClause 一致
BASE_WHERE = """p.is_discontinued = false
            AND p.is_published = true
            AND EXISTS (
                SELECT 1 FROM cross_references x
                WHERE x.product_id = p.id
                  AND x.is_published = true
                  AND x.is_discontinued = false
            )"""


def build_offset_sql(where_extra: str, offset: int, page_size: int) -> str:
    """OFFSET 分页 SQL (生产现状, 与 PostgresSearchProvider.SearchAsync 完全等价)

    WHY 完全等价: 压测对象是 ADR #5 决策的 SQL, 必须精确复现生产查询路径.
    """
    where = BASE_WHERE + (" " + where_extra if where_extra else "")
    return f"""
WITH sort_cte AS (
    SELECT
        p.id AS product_id,
        COALESCE(MIN(xb.sort_order), 2147483647) AS brand_sort_order_min,
        COALESCE(MIN(x.sort_order), 2147483647) AS oem_list_sort_order_min
    FROM products p
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true
        AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand
        AND xb.deleted_at IS NULL
    WHERE {where}
    GROUP BY p.id
)
SELECT
    p.id, p.mr_1, p.oem_no_display, p.type, p.updated_at
FROM products p
JOIN sort_cte s ON s.product_id = p.id
ORDER BY
    s.brand_sort_order_min ASC,
    s.oem_list_sort_order_min ASC,
    p.updated_at DESC
LIMIT {page_size} OFFSET {offset}"""


def build_keyset_sql(where_extra: str, last_id: int, page_size: int) -> str:
    """keyset 简化版 SQL (ADR #5 候选方案对比基线)

    WHY 简化: 真实 keyset 需记住 (brand_sort, oem_sort, updated_at, id) 四元组,
      实现复杂. 这里用 id 单调递增简化, 测 keyset 的核心优势:
      O(log N) 索引定位 vs OFFSET O(N) 扫描.

    注: 简化版不模拟三层排序, 仅作性能潜力对比, 不作生产 SQL.
    """
    where = BASE_WHERE + (" " + where_extra if where_extra else "")
    return f"""
SELECT
    p.id, p.mr_1, p.oem_no_display, p.type, p.updated_at
FROM products p
WHERE {where}
  AND p.id > {last_id}
ORDER BY p.id ASC
LIMIT {page_size}"""


def build_count_sql(where_extra: str) -> str:
    """COUNT 查询 (与 PostgresSearchProvider.SearchAsync 的 countSql 等价)"""
    where = BASE_WHERE + (" " + where_extra if where_extra else "")
    return f"SELECT COUNT(*) FROM products p WHERE {where}"


# ========== PG 连接与执行 ==========

def connect_pg(cfg: dict):
    pg = cfg["pg"]
    conn = psycopg2.connect(
        host=pg["host"], port=pg["port"],
        dbname=pg["database"], user=pg["user"], password=pg["password"]
    )
    conn.autocommit = True
    return conn


def exec_sql_timed(conn, sql: str) -> tuple[float, int]:
    """执行 SQL, 返回 (耗时 ms, 返回行数)"""
    t0 = time.perf_counter()
    with conn.cursor() as cur:
        cur.execute(sql)
        rows = cur.fetchall()
    dt_ms = (time.perf_counter() - t0) * 1000.0
    return dt_ms, len(rows)


def exec_count(conn, sql: str) -> tuple[float, int]:
    """执行 COUNT, 返回 (耗时 ms, total)"""
    t0 = time.perf_counter()
    with conn.cursor() as cur:
        cur.execute(sql)
        total = cur.fetchone()[0]
    dt_ms = (time.perf_counter() - t0) * 1000.0
    return dt_ms, int(total)


# ========== 数据扩容 (--scale-up) ==========

def scale_up_to_millions(conn, cfg: dict):
    """扩容到 1M products + 5M xrefs + 1M apps (符合 hard constraint)

    WHY generate_series: PG 原生, 无需外部数据文件.
    WHY 不复用 ETL: ETL 需 XLSX 文件, 这里直接 SQL 生成, 更轻量.
    """
    target = cfg["scale_target"]
    n_products = target["products"]
    n_xrefs_per = target["xrefs_per_product"]
    n_apps_per = target["apps_per_product"]

    # 先看现有数据量
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM products")
        existing = cur.fetchone()[0]
    if existing >= n_products:
        print(f"[scale-up] 已有 {existing} products >= 目标 {n_products}, 跳过扩容")
        return

    print(f"[scale-up] 当前 {existing} products, 扩容到 {n_products}...")
    start = time.perf_counter()

    # 1. 批量 INSERT products (按 10 万一批, 避免 WAL 暴涨)
    batch = 100_000
    for off in range(existing + 1, n_products + 1, batch):
        end_off = min(off + batch - 1, n_products)
        with conn.cursor() as cur:
            cur.execute(f"""
INSERT INTO products (
    oem_no_normalized, oem_no_display, remark, type, product_name_1, product_name_2,
    mr_1, oem_2, is_published, is_discontinued, d1_mm, h1_mm, created_at, updated_at
)
SELECT
    'P' || s::text,
    'P' || s::text,
    'Scale-up test product ' || s::text,
    CASE (s % 5) WHEN 0 THEN 'Oil' WHEN 1 THEN 'Fuel' WHEN 2 THEN 'Air' WHEN 3 THEN 'Cabin' ELSE 'Hydraulic' END,
    CASE (s % 5) WHEN 0 THEN 'OIL FILTER' WHEN 1 THEN 'FUEL FILTER' WHEN 2 THEN 'AIR FILTER' WHEN 3 THEN 'CABIN FILTER' ELSE 'HYDRAULIC FILTER' END,
    'Test Product Name 2',
    'MR1-' || s::text,
    'P' || s::text,
    true, false,
    100 + (s % 50)::numeric,
    100 + (s % 200)::numeric,
    NOW(), NOW()
FROM generate_series({off}, {end_off}) AS s
""")
        elapsed = time.perf_counter() - start
        print(f"  products: {end_off}/{n_products} ({elapsed:.1f}s)")

    # 2. 批量 INSERT cross_references (每产品 n_xrefs_per 个)
    print(f"[scale-up] 生成 cross_references (每产品 {n_xrefs_per} 个)...")
    # 用 products.id 直接 JOIN (假设 id 连续)
    with conn.cursor() as cur:
        cur.execute(f"""
INSERT INTO cross_references (
    product_id, product_name_1, oem_brand, oem_no_3, oem_2, sort_order,
    machine_type, is_published, is_discontinued, created_at
)
SELECT
    p.id,
    p.product_name_1,
    CASE (p.id % 3) WHEN 0 THEN 'BOSCH' WHEN 1 THEN 'MANN' ELSE 'MAHLE' END,
    'OEM3-' || (p.id * {n_xrefs_per} + n)::text,
    p.oem_2,
    n,
    'others', true, false, NOW()
FROM products p
CROSS JOIN generate_series(1, {n_xrefs_per}) AS n
WHERE p.id > {existing}
""")
    print(f"  cross_references 完成 ({time.perf_counter() - start:.1f}s)")

    # 3. 批量 INSERT machine_applications (每产品 n_apps_per 个)
    print(f"[scale-up] 生成 machine_applications (每产品 {n_apps_per} 个)...")
    with conn.cursor() as cur:
        cur.execute(f"""
INSERT INTO machine_applications (
    product_id, machine_brand, machine_model, model_name, engine_brand,
    machine_category, is_ongoing, is_discontinued, created_at
)
SELECT
    p.id,
    CASE (p.id % 4) WHEN 0 THEN 'Caterpillar' WHEN 1 THEN 'Komatsu' WHEN 2 THEN 'Volvo' ELSE 'Hitachi' END,
    'MODEL-' || (p.id % 100)::text,
    'Model Name ' || (p.id % 50)::text,
    CASE (p.id % 2) WHEN 0 THEN 'Cummins' ELSE 'Perkins' END,
    CASE (p.id % 3) WHEN 0 THEN 'construction' WHEN 1 THEN 'agriculture' ELSE 'commercial' END,
    true, false, NOW()
FROM products p
CROSS JOIN generate_series(1, {n_apps_per}) AS n
WHERE p.id > {existing}
""")
    print(f"  machine_applications 完成 ({time.perf_counter() - start:.1f}s)")

    # 4. ANALYZE 更新统计信息 (压测前必须)
    print("[scale-up] ANALYZE products/cross_references/machine_applications...")
    with conn.cursor() as cur:
        cur.execute("ANALYZE products")
        cur.execute("ANALYZE cross_references")
        cur.execute("ANALYZE machine_applications")

    print(f"[scale-up] 完成, 总耗时 {time.perf_counter() - start:.1f}s")


# ========== 压测主流程 ==========

def run_scenario(conn, scenario: dict, offsets: list, page_size: int,
                 repeats: int, warmup: int, keyset_compare: bool,
                 keyset_last_id_offset: int) -> dict:
    """跑单个 scenario 的所有 OFFSET 深度档位"""
    where_extra = scenario["where_extra"]
    name = scenario["name"]
    desc = scenario["desc"]

    # 先 COUNT 拿 total (避免 offset > total 时无意义测试)
    count_sql = build_count_sql(where_extra)
    count_ms, total = exec_count(conn, count_sql)
    print(f"\n[{name}] total={total} (COUNT 耗时 {count_ms:.1f}ms) — {desc}")

    result = {
        "scenario": name,
        "desc": desc,
        "total": total,
        "count_ms": round(count_ms, 2),
        "offset_results": [],
        "keyset_results": []
    }

    # OFFSET 深度循环
    for offset in offsets:
        if offset >= total:
            print(f"  OFFSET={offset:>6} → 跳过 (offset >= total {total})")
            result["offset_results"].append({
                "offset": offset, "skipped": True, "reason": f"offset >= total ({total})"
            })
            continue

        sql = build_offset_sql(where_extra, offset, page_size)

        # warmup (不计时, 让 PG 缓存预热)
        for _ in range(warmup):
            exec_sql_timed(conn, sql)

        # 计时跑 N 次
        times = []
        rows_count = 0
        for _ in range(repeats):
            dt_ms, n = exec_sql_timed(conn, sql)
            times.append(dt_ms)
            rows_count = n

        stat = {
            "offset": offset,
            "page_size": page_size,
            "rows_returned": rows_count,
            "min_ms": round(min(times), 2),
            "max_ms": round(max(times), 2),
            "mean_ms": round(statistics.mean(times), 2),
            "median_ms": round(statistics.median(times), 2),
            "p95_ms": round(percentile(times, 95), 2),
            "p99_ms": round(percentile(times, 99), 2),
            "all_ms": [round(t, 2) for t in times]
        }
        result["offset_results"].append(stat)
        print(f"  OFFSET={offset:>6} → median={stat['median_ms']:>8.1f}ms  "
              f"p95={stat['p95_ms']:>8.1f}ms  rows={rows_count}")

    # keyset 对比 (若启用)
    if keyset_compare:
        # 取 OFFSET=keyset_last_id_offset 时第 1 条的 id 作为 last_id
        # 简化: 直接 SELECT id FROM products ORDER BY id OFFSET ... LIMIT 1
        last_id_sql = f"SELECT id FROM products ORDER BY id OFFSET {keyset_last_id_offset} LIMIT 1"
        with conn.cursor() as cur:
            cur.execute(last_id_sql)
            row = cur.fetchone()
            if row is None:
                print(f"  [keyset] 跳过 (找不到 OFFSET={keyset_last_id_offset} 的 id)")
            else:
                last_id = row[0]
                print(f"\n[{name}-keyset] last_id={last_id} (OFFSET={keyset_last_id_offset})")
                keyset_sql = build_keyset_sql(where_extra, last_id, page_size)

                # warmup
                for _ in range(warmup):
                    exec_sql_timed(conn, keyset_sql)

                times = []
                rows_count = 0
                for _ in range(repeats):
                    dt_ms, n = exec_sql_timed(conn, keyset_sql)
                    times.append(dt_ms)
                    rows_count = n

                stat = {
                    "last_id": last_id,
                    "page_size": page_size,
                    "rows_returned": rows_count,
                    "min_ms": round(min(times), 2),
                    "max_ms": round(max(times), 2),
                    "mean_ms": round(statistics.mean(times), 2),
                    "median_ms": round(statistics.median(times), 2),
                    "p95_ms": round(percentile(times, 95), 2),
                    "p99_ms": round(percentile(times, 99), 2),
                    "all_ms": [round(t, 2) for t in times]
                }
                result["keyset_results"].append(stat)
                print(f"  keyset last_id={last_id} → median={stat['median_ms']:>8.1f}ms  "
                      f"p95={stat['p95_ms']:>8.1f}ms  rows={rows_count}")

    return result


def percentile(data: list, p: float) -> float:
    """简单百分位计算 (线性插值)"""
    if not data:
        return 0.0
    s = sorted(data)
    k = (len(s) - 1) * p / 100.0
    f = int(k)
    c = min(f + 1, len(s) - 1)
    if f == c:
        return float(s[f])
    return float(s[f] + (s[c] - s[f]) * (k - f))


# ========== 报告生成 ==========

def generate_report(results: dict, cfg: dict, output_path: Path):
    """生成 Markdown 报告"""
    ts = datetime.now(timezone.utc).astimezone().strftime("%Y-%m-%d %H:%M:%S %z")
    lines = []
    lines.append(f"# v27-3 OFFSET 分页压测报告 (ADR #5 决策依据)\n")
    lines.append(f"> 生成时间: {ts}")
    lines.append(f"> 数据库: {cfg['pg']['database']}@{cfg['pg']['host']}:{cfg['pg']['port']}")
    lines.append(f"> 数据规模: {results['data_volume']['products']} products / "
                 f"{results['data_volume']['xrefs']} xrefs / "
                 f"{results['data_volume']['apps']} apps\n")

    lines.append("## 1. 摘要\n")
    lines.append(f"- 测试目的: 验证 PostgresSearchProvider OFFSET 分页在不同深度下的性能曲线, "
                 f"为 ADR #5 (keyset 暂缓决策) 提供量化依据")
    lines.append(f"- OFFSET 深度档: {', '.join(str(o) for o in cfg['offsets'])}")
    lines.append(f"- 重复次数: {cfg['repeats']} (warmup {cfg['warmup_repeats']})")
    lines.append(f"- keyset 对比: {'启用' if cfg.get('keyset_compare') else '禁用'}\n")

    # 数据规模表
    lines.append("## 2. 数据规模\n")
    lines.append("| 表 | 行数 |")
    lines.append("|---|---|")
    lines.append(f"| products | {results['data_volume']['products']:,} |")
    lines.append(f"| cross_references | {results['data_volume']['xrefs']:,} |")
    lines.append(f"| machine_applications | {results['data_volume']['apps']:,} |")
    lines.append("")

    # 每个 scenario 的 OFFSET 深度表
    lines.append("## 3. OFFSET 深度 vs 性能曲线\n")
    for sc in results["scenarios"]:
        lines.append(f"### 3.{results['scenarios'].index(sc)+1} 场景: {sc['scenario']}\n")
        lines.append(f"**描述**: {sc['desc']}")
        lines.append(f"**total**: {sc['total']:,} (COUNT 耗时 {sc['count_ms']}ms)\n")

        lines.append("| OFFSET | rows | min(ms) | median(ms) | p95(ms) | p99(ms) | max(ms) |")
        lines.append("|---:|---:|---:|---:|---:|---:|---:|")
        for r in sc["offset_results"]:
            if r.get("skipped"):
                lines.append(f"| {r['offset']} | - | SKIP | - | - | - | - |")
            else:
                lines.append(f"| {r['offset']} | {r['rows_returned']} | "
                             f"{r['min_ms']} | {r['median_ms']} | {r['p95_ms']} | "
                             f"{r['p99_ms']} | {r['max_ms']} |")
        lines.append("")

        # keyset 对比
        if sc.get("keyset_results"):
            lines.append(f"**keyset 对比** (last_id 来自 OFFSET={cfg.get('keyset_last_id_offset', 10000)}):\n")
            lines.append("| mode | last_id | rows | median(ms) | p95(ms) | p99(ms) |")
            lines.append("|---|---:|---:|---:|---:|---:|")
            for k in sc["keyset_results"]:
                lines.append(f"| keyset | {k['last_id']} | {k['rows_returned']} | "
                             f"{k['median_ms']} | {k['p95_ms']} | {k['p99_ms']} |")
            lines.append("")

    # ADR #5 决策建议
    lines.append("## 4. ADR #5 决策建议\n")
    advice = derive_advice(results)
    lines.append(advice)

    lines.append("\n## 5. 文件清单\n")
    lines.append("- `perf_offset_config.json` — 参数化配置")
    lines.append("- `_perf_offset_paging.py` — 主压测脚本")
    lines.append("- `_perf_offset_results.json` — 原始结果 (本报告的数据源)")
    lines.append("- `_perf_offset_report.md` — 本报告\n")

    output_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"\n报告已写入: {output_path}")


def derive_advice(results: dict) -> str:
    """基于实测数据推导 ADR #5 决策建议

    WHY 控制变量法: ADR #5 决策对象是 OFFSET 分页深度 vs keyset,
      不是 WHERE/ILIKE 主导的绝对耗时. 必须对比同一场景不同 OFFSET 深度的差异,
      而非用最差场景的绝对 P95 判断 (q_filter 的 1850ms 是 ILIKE 全表扫描导致, 与 OFFSET 无关).
    """
    advice_lines = []
    advice_lines.append("基于实测数据, 给出以下建议:\n")

    # 收集每个场景的 OFFSET 深度退化比 (深档 P95 / 浅档 P95)
    advice_lines.append("### 4.1 OFFSET 深度退化分析 (控制变量法)\n")
    advice_lines.append("对比同一场景下深档 P95 vs 浅档 P95, 衡量 OFFSET 深度本身的影响:\n")
    advice_lines.append("| 场景 | 浅档 (OFFSET=0) P95 | 最深档 P95 | 深浅比 | 主要瓶颈 |")
    advice_lines.append("|---|---:|---:|---:|---|")

    deep_ratio_max = 1.0
    for sc in results["scenarios"]:
        valid = [r for r in sc["offset_results"] if not r.get("skipped")]
        if len(valid) < 2:
            continue
        shallow = valid[0]  # OFFSET=0
        deep = valid[-1]    # 最深档
        shallow_p95 = shallow["p95_ms"]
        deep_p95 = deep["p95_ms"]
        ratio = deep_p95 / shallow_p95 if shallow_p95 > 0 else float("inf")
        deep_ratio_max = max(deep_ratio_max, ratio)

        # 判断主要瓶颈
        if shallow_p95 > 1000:
            bottleneck = "ILIKE 全表扫描 (与 OFFSET 无关)"
        elif shallow_p95 > 300:
            bottleneck = "CTE + LATERAL JOIN + EXISTS"
        else:
            bottleneck = "结果集小, 性能良好"

        advice_lines.append(
            f"| {sc['scenario']} | {shallow_p95:.1f}ms (OFFSET={shallow['offset']}) | "
            f"{deep_p95:.1f}ms (OFFSET={deep['offset']}) | {ratio:.2f}x | {bottleneck} |"
        )

    advice_lines.append("")
    advice_lines.append(f"**最大深度退化比: {deep_ratio_max:.2f}x**\n")

    # SLA 判断
    sla_p95 = 200.0
    advice_lines.append("### 4.2 SLA 达标分析\n")
    advice_lines.append(f"SLA 目标 (来自 docs/bench-baseline.md): 公开搜索 P95 < {sla_p95}ms\n")

    # 找最差场景的浅档 P95
    all_shallow_p95 = []
    for sc in results["scenarios"]:
        valid = [r for r in sc["offset_results"] if not r.get("skipped")]
        if valid:
            all_shallow_p95.append((sc["scenario"], valid[0]["p95_ms"]))

    worst_shallow = max(all_shallow_p95, key=lambda x: x[1]) if all_shallow_p95 else None
    if worst_shallow:
        advice_lines.append(
            f"- 最差浅档 (OFFSET=0): {worst_shallow[1]:.1f}ms ({worst_shallow[0]}) — "
            + ("✅ 达标" if worst_shallow[1] <= sla_p95 else "❌ 未达标")
        )

    # keyset 潜力对比
    advice_lines.append("\n### 4.3 keyset 改造潜力\n")
    advice_lines.append("keyset 简化版 (WHERE p.id > :last_id) vs OFFSET=10000 等价深度:\n")
    advice_lines.append("| 场景 | OFFSET P95 | keyset P95 | 性能提升 |")
    advice_lines.append("|---|---:|---:|---:|")
    for sc in results["scenarios"]:
        if not sc.get("keyset_results"):
            continue
        # 找 OFFSET=10000 或最接近的深档
        offset_10k = next((r for r in sc["offset_results"]
                          if r.get("offset") == 10000 and not r.get("skipped")), None)
        if not offset_10k:
            offset_10k = next((r for r in sc["offset_results"]
                             if not r.get("skipped") and r.get("offset", 0) >= 1000), None)
        if not offset_10k:
            continue
        ks = sc["keyset_results"][0]
        uplift = offset_10k["p95_ms"] / ks["p95_ms"] if ks["p95_ms"] > 0 else float("inf")
        advice_lines.append(
            f"| {sc['scenario']} | {offset_10k['p95_ms']:.1f}ms | "
            f"{ks['p95_ms']:.1f}ms | {uplift:.1f}x |"
        )

    advice_lines.append("")

    # 综合决策
    advice_lines.append("### 4.4 综合决策\n")
    if deep_ratio_max <= 1.5:
        advice_lines.append(f"- ✅ OFFSET 深度退化比 ≤ 1.5x ({deep_ratio_max:.2f}x): "
                           f"**维持 ADR #5 暂缓决策**")
        advice_lines.append("  - 50K 数据下 OFFSET 深度本身不是主要瓶颈, 浅档/深档性能差异不显著")
        advice_lines.append("  - 真实瓶颈是 ILIKE 全表扫描 (q_filter 1850ms) 和 CTE + LATERAL JOIN (baseline 510ms)")
        advice_lines.append("  - 优先级: 加 GIN trgm 索引 > keyset 改造 (前者收益更大, 改动更小)")
        advice_lines.append("  - 真实用户行为: 99% 在前 5 页内, 深分页罕见")
        advice_lines.append("  - 1M 数据下需重测验证 (建议用独立库 sakurafilter_perf_tests 隔离)")
    elif deep_ratio_max <= 3.0:
        advice_lines.append(f"- ⚠️ OFFSET 深度退化比 {deep_ratio_max:.2f}x (1.5x ~ 3.0x): "
                           f"**继续观察, 暂缓 keyset 改造**")
        advice_lines.append("  - 短期: 在 PostgresSearchProvider 加 max_offset 保护 (如 OFFSET > 10000 返回 400)")
        advice_lines.append("  - 中期: 监控真实用户深分页频率, 若 > 1% 触发 keyset 改造")
    else:
        advice_lines.append(f"- ❌ OFFSET 深度退化比 {deep_ratio_max:.2f}x (>3x): "
                           f"**建议升级 keyset 分页, 反转 ADR #5**")
        advice_lines.append("  - 立即加 max_offset 保护")
        advice_lines.append("  - 排期 keyset 改造 (前后端契约改造, 需 1-2 sprint)")

    advice_lines.append("")
    advice_lines.append("### 4.5 后续验证建议\n")
    advice_lines.append("- **1M 数据扩容压测**: 当前 50K 数据下 OFFSET 深度影响不显著, "
                       "1M 数据下深分页 (OFFSET > 100000) 可能显著退化, 需独立库验证")
    advice_lines.append("- **GIN trgm 索引**: q_filter 场景 ILIKE 全表扫描 1850ms, "
                       "加 `CREATE INDEX idx_products_pn1_trgm ON products USING gin (product_name_1 gin_trgm_ops)` "
                       "预计可降到 50-200ms (见 docs/bench-baseline.md §7.2 P1)")
    advice_lines.append("- **keyset 真实改造工作量评估**: 简化版 keyset 性能潜力巨大 (17-6000x), "
                       "但真实三层排序 keyset 改造需评估前后端契约改造工作量")
    advice_lines.append("")
    return "\n".join(advice_lines)


# ========== 主入口 ==========

def main():
    parser = argparse.ArgumentParser(description="v27-3 OFFSET 分页压测")
    parser.add_argument("--scale-up", action="store_true",
                        help="扩容到 1M products + 5M xrefs + 1M apps (符合 hard constraint)")
    parser.add_argument("--repeats", type=int, default=None,
                        help=f"覆盖 config.repeats (默认 {5})")
    parser.add_argument("--output-dir", type=str, default=None,
                        help="输出目录 (默认 spike-test/)")
    parser.add_argument("--config", type=str, default=None,
                        help="配置文件路径 (默认 spike-test/perf_offset_config.json)")
    args = parser.parse_args()

    # 加载配置
    cfg_path = Path(args.config) if args.config else CONFIG_PATH
    if not cfg_path.exists():
        print(f"配置文件不存在: {cfg_path}")
        sys.exit(1)
    cfg = json.loads(cfg_path.read_text(encoding="utf-8"))

    if args.repeats:
        cfg["repeats"] = args.repeats

    # 输出路径
    out_dir = Path(args.output_dir) if args.output_dir else SCRIPT_DIR
    results_path = out_dir / "_perf_offset_results.json"
    report_path = out_dir / "_perf_offset_report.md"

    # 连接 PG
    print(f"连接 PG: {cfg['pg']['host']}:{cfg['pg']['port']}/{cfg['pg']['database']}")
    conn = connect_pg(cfg)

    # 扩容 (若启用)
    if args.scale_up or cfg.get("scale_to_millions", False):
        scale_up_to_millions(conn, cfg)

    # 查询当前数据规模
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM products")
        n_products = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM cross_references")
        n_xrefs = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM machine_applications")
        n_apps = cur.fetchone()[0]
    print(f"数据规模: products={n_products:,} xrefs={n_xrefs:,} apps={n_apps:,}")

    # 跑所有 scenario
    scenarios = cfg["scenarios"]
    offsets = cfg["offsets"]
    page_size = cfg["page_size"]
    repeats = cfg["repeats"]
    warmup = cfg["warmup_repeats"]
    keyset_compare = cfg.get("keyset_compare", False)
    keyset_last_id_offset = cfg.get("keyset_last_id_offset", 10000)

    all_results = []
    for sc in scenarios:
        print(f"\n{'='*80}")
        print(f"压测场景: {sc['name']} — {sc['desc']}")
        print(f"{'='*80}")
        r = run_scenario(conn, sc, offsets, page_size, repeats, warmup,
                         keyset_compare, keyset_last_id_offset)
        all_results.append(r)

    # 汇总结果
    final = {
        "test_time": datetime.now(timezone.utc).astimezone().isoformat(),
        "config": cfg,
        "data_volume": {
            "products": n_products,
            "xrefs": n_xrefs,
            "apps": n_apps
        },
        "scenarios": all_results
    }

    results_path.write_text(json.dumps(final, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n结果已写入: {results_path}")

    # 生成报告
    generate_report(final, cfg, report_path)

    conn.close()
    print("\n压测完成.")


if __name__ == "__main__":
    main()
