"""V24-F94 (v28-2) 端到端压测: 应用 GIN trgm 索引 + 跑 5 场景对比 baseline vs 新 SQL
对比:
  1. baseline (旧 SQL): OR + EXISTS xref + EXISTS machine (P95=1827ms)
  2. v28-2 新 SQL: CTE UNION 拆分 + 三表 GIN trgm (P95=305ms 预期)

5 场景:
  - baseline: 无 q (基础过滤, 走 type/d1 等不涉 q_match)
  - q_single: 单 token q='oil'
  - q_multi: 多 token q='oil filter'
  - q_filter_type: q='oil' + type='bearing'
  - q_xref_match: q='CAT' (主要命中 xref.oem_brand)
"""
import os
import time
import statistics
import psycopg2
import json
from datetime import datetime

CONN = os.environ.get(
    "PG_TEST_CONNECTION_STRING",
    "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533"
)


def ensure_gin_trgm_indexes(conn):
    """应用 V24-F94 migration: 10 个 GIN trgm 索引 (5 新 + 5 已有 017)"""
    with conn.cursor() as cur:
        cur.execute("CREATE EXTENSION IF NOT EXISTS pg_trgm;")
        # products 5 字段
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_product_name_1_trgm ON products USING gin (product_name_1 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_product_name_2_trgm ON products USING gin (product_name_2 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_oem_2_trgm ON products USING gin (oem_2 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_mr_1_trgm ON products USING gin (mr_1 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_remark_trgm ON products USING gin (remark gin_trgm_ops);")
        # cross_references 3 字段
        cur.execute("CREATE INDEX IF NOT EXISTS ix_xrefs_oem_brand_trgm ON cross_references USING gin (oem_brand gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_xrefs_oem_no_3_trgm ON cross_references USING gin (oem_no_3 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_xrefs_oem_2_trgm ON cross_references USING gin (oem_2 gin_trgm_ops);")
        # machine_applications 2 字段
        cur.execute("CREATE INDEX IF NOT EXISTS ix_machine_apps_brand_trgm ON machine_applications USING gin (machine_brand gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_machine_apps_model_trgm ON machine_applications USING gin (machine_model gin_trgm_ops);")
        cur.execute("ANALYZE products;")
        cur.execute("ANALYZE cross_references;")
        cur.execute("ANALYZE machine_applications;")
        conn.commit()


def build_baseline_sql(q=None, type_filter=None):
    """旧 SQL (V24-F80 baseline): OR + EXISTS xref + EXISTS machine"""
    conditions = ["p.is_discontinued = false", "p.is_published = true"]
    conditions.append("EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)")

    if q:
        qe = q.replace("'", "''")
        conditions.append(f"""(
            p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR
            p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\' OR
            p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR
            p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR
            p.remark ILIKE '%{qe}%' ESCAPE '\\' OR
            EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id
                    AND (x.oem_brand ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_no_3 ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_2 ILIKE '%{qe}%' ESCAPE '\\')) OR
            EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id
                    AND (m.machine_brand ILIKE '%{qe}%' ESCAPE '\\' OR m.machine_model ILIKE '%{qe}%' ESCAPE '\\'))
        )""")

    if type_filter:
        conditions.append(f"p.type = '{type_filter.replace(chr(39), chr(39) + chr(39))}'")

    where = " AND ".join(conditions)
    return f"SELECT COUNT(*) FROM products p WHERE {where}"


def build_v28_2_sql(q=None, type_filter=None):
    """v28-2 新 SQL: CTE UNION 拆分 + 三表 GIN trgm"""
    base_conditions = ["p.is_discontinued = false", "p.is_published = true"]
    base_conditions.append("EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)")

    if type_filter:
        base_conditions.append(f"p.type = '{type_filter.replace(chr(39), chr(39) + chr(39))}'")

    base_where = " AND ".join(base_conditions)
    has_q = q is not None and q.strip() != ""

    if has_q:
        tokens = q.strip().split()
        cte_parts = []
        for i, token in enumerate(tokens):
            qe = token.replace("'", "''").replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")
            cte_name = "q_match" if len(tokens) == 1 else f"q_match_{i}"
            cte_parts.append(f"""{cte_name} AS (
    SELECT p.id AS product_id FROM products p
    WHERE p.is_discontinued = false AND p.is_published = true AND (
        p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\'
        OR p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR p.remark ILIKE '%{qe}%' ESCAPE '\\'
    )
    UNION
    SELECT DISTINCT x.product_id FROM cross_references x
    WHERE x.is_published = true AND x.is_discontinued = false AND (
        x.oem_brand ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_no_3 ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_2 ILIKE '%{qe}%' ESCAPE '\\'
    )
    UNION
    SELECT DISTINCT m.product_id FROM machine_applications m
    WHERE m.machine_brand ILIKE '%{qe}%' ESCAPE '\\' OR m.machine_model ILIKE '%{qe}%' ESCAPE '\\'
)""")
        if len(tokens) > 1:
            intersect_parts = [f"SELECT product_id FROM q_match_{i}" for i in range(len(tokens))]
            cte_parts.append(f"q_match AS ({' INTERSECT '.join(intersect_parts)})")

        cte_prefix = "WITH " + ", ".join(cteParts := cte_parts)
        sort_join = "JOIN q_match ON q_match.product_id = p.id"
        return f"""{cte_prefix}, sort_cte AS (
    SELECT p.id AS product_id FROM products p
    {sort_join}
    LEFT JOIN cross_references x ON x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand AND xb.deleted_at IS NULL
    WHERE {base_where}
    GROUP BY p.id
)
SELECT COUNT(*) FROM sort_cte"""
    else:
        return f"""WITH sort_cte AS (
    SELECT p.id AS product_id FROM products p
    LEFT JOIN cross_references x ON x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand AND xb.deleted_at IS NULL
    WHERE {base_where}
    GROUP BY p.id
)
SELECT COUNT(*) FROM sort_cte"""


def measure_p95(conn, sql, rounds=10):
    times = []
    with conn.cursor() as cur:
        for _ in range(rounds):
            t0 = time.perf_counter()
            cur.execute(sql)
            cur.fetchone()
            t1 = time.perf_counter()
            times.append((t1 - t0) * 1000)
    p50 = statistics.median(times)
    p95 = sorted(times)[max(0, int(len(times) * 0.95) - 1)]
    return p50, p95, times


def main():
    print("=" * 80)
    print(f"V24-F94 (v28-2) 端到端压测: baseline vs 新 SQL (CTE UNION + GIN trgm)")
    print(f"时间: {datetime.now().isoformat()}")
    print("=" * 80)

    conn = psycopg2.connect(CONN)

    # 数据规模
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM products;")
        total_products = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM cross_references;")
        total_xrefs = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM machine_applications;")
        total_machines = cur.fetchone()[0]
    print(f"\n数据规模: products={total_products}, xrefs={total_xrefs}, machines={total_machines}")

    # 应用 GIN trgm 索引
    print(f"\n[阶段 1] 应用 V24-F94 GIN trgm 索引 (10 个)")
    ensure_gin_trgm_indexes(conn)
    print(f"  10 个 GIN trgm 索引已创建 (IF NOT EXISTS)")

    # 5 场景对比
    scenarios = [
        ("baseline (无 q)", None, None),
        ("q_single (q='oil')", "oil", None),
        ("q_multi (q='oil filter')", "oil filter", None),
        ("q_filter_type (q='oil', type='bearing')", "oil", "bearing"),
        ("q_xref_match (q='CAT')", "CAT", None),
    ]

    print(f"\n[阶段 2] 5 场景对比 — 10 轮 P95")
    print(f"  {'场景':<40} | {'baseline':<14} | {'v28-2 新 SQL':<16} | {'提升':<10}")
    print("  " + "-" * 90)
    results = []
    for label, q, type_filter in scenarios:
        b_sql = build_baseline_sql(q, type_filter)
        n_sql = build_v28_2_sql(q, type_filter)

        _, b_p95, _ = measure_p95(conn, b_sql, rounds=10)
        _, n_p95, _ = measure_p95(conn, n_sql, rounds=10)

        speedup = b_p95 / n_p95 if n_p95 > 0 else 0
        print(f"  {label:<40} | {b_p95:>10.0f}ms | {n_p95:>12.0f}ms | {speedup:>6.2f}x")
        results.append({
            "scenario": label,
            "q": q,
            "type": type_filter,
            "baseline_p95_ms": b_p95,
            "v28_2_p95_ms": n_p95,
            "speedup": speedup
        })

    # 汇总
    print("\n" + "=" * 80)
    print("汇总")
    print("=" * 80)
    avg_b = statistics.mean(r["baseline_p95_ms"] for r in results)
    avg_n = statistics.mean(r["v28_2_p95_ms"] for r in results)
    avg_speedup = statistics.mean(r["speedup"] for r in results)
    print(f"5 场景平均 P95:")
    print(f"  baseline:    {avg_b:.0f}ms")
    print(f"  v28-2 新 SQL: {avg_n:.0f}ms ({avg_speedup:.2f}x)")
    print(f"\n结论:")
    print(f"  v28-2 目标: P95 ≤ 50ms (32x+ 提升, baseline=1611ms)")
    if avg_n < 50:
        print(f"  ✅ 达成目标! v28-2 新 SQL P95 = {avg_n:.0f}ms ({avg_speedup:.2f}x)")
    elif avg_n < avg_b / 4:
        print(f"  ✅ 达 4x 目标! v28-2 新 SQL P95 = {avg_n:.0f}ms ({avg_speedup:.2f}x)")
    elif avg_n < avg_b:
        print(f"  ⚠️ 部分提升: v28-2 新 SQL P95 = {avg_n:.0f}ms ({avg_speedup:.2f}x, 未达 4x 目标)")
    else:
        print(f"  ❌ 无提升: v28-2 新 SQL P95 = {avg_n:.0f}ms")

    # 保存结果
    output_path = os.path.join(os.path.dirname(__file__), "_perf_v28_2_e2e_results.json")
    result = {
        "timestamp": datetime.now().isoformat(),
        "scenarios": results,
        "avg_baseline_p95_ms": avg_b,
        "avg_v28_2_p95_ms": avg_n,
        "avg_speedup": avg_speedup
    }
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(f"\n结果已保存: {output_path}")

    conn.close()


if __name__ == "__main__":
    main()
