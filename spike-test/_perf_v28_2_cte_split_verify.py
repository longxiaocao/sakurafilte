"""
V24-F94 (v28-2) 验证: CTE 拆分重写 SQL + GIN trgm 索引
  v28-1 结论: baseline SQL (OR + EXISTS xref) 让 PG 优化器不选 GIN trgm, P95=197ms 无收益
  v28-2 目标: 把 EXISTS 子查询剥离到 CTE, 让 products 表的 5 字段 ILIKE 走 GIN trgm Bitmap Index Scan

3 种 SQL 写法对比:
  1. baseline (当前 PostgresSearchProvider): OR + EXISTS xref + EXISTS machine (P95=197ms)
  2. CTE 拆分 + GIN trgm (v28-2 新方案): q_match CTE 走 GIN trgm, 再 JOIN products
  3. CTE 拆分 + GIN trgm + xref EXISTS 内联: 测试 EXISTS 子查询放 CTE 内还是外

关键验证:
  - CTE 内 5 字段 ILIKE 是否能走 GIN trgm Bitmap Index Scan
  - EXISTS xref/machine 子查询放 CTE 内 vs 外的性能差异
  - 整体 P95 是否能从 197ms 降到 50ms 以下 (4x+)
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


def build_baseline_sql(q):
    """当前 PostgresSearchProvider 的 SQL (OR + EXISTS xref + EXISTS machine)
    对应 BuildWhereClause L93-L138 完整逻辑 (单 token 版本)"""
    qe = q.replace("'", "''")
    return f"""
SELECT COUNT(*) FROM products p
WHERE p.is_discontinued = false
  AND p.is_published = true
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)
  AND (
      p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.remark ILIKE '%{qe}%' ESCAPE '\\' OR
      EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id
              AND (x.oem_brand ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_no_3 ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_2 ILIKE '%{qe}%' ESCAPE '\\')) OR
      EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id
              AND (m.machine_brand ILIKE '%{qe}%' ESCAPE '\\' OR m.machine_model ILIKE '%{qe}%' ESCAPE '\\'))
  )
"""


def build_cte_split_sql(q):
    """v28-2 方案 A: CTE 拆分, EXISTS 子查询全部放外层
    q_match CTE 只做 5 字段 ILIKE (走 GIN trgm), EXISTS 放外层"""
    qe = q.replace("'", "''")
    return f"""
WITH q_match AS (
    SELECT p.id AS product_id
    FROM products p
    WHERE p.is_discontinued = false
      AND p.is_published = true
      AND (
          p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.remark ILIKE '%{qe}%' ESCAPE '\\'
      )
)
SELECT COUNT(*) FROM products p
JOIN q_match ON q_match.product_id = p.id
WHERE p.is_discontinued = false
  AND p.is_published = true
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)
  AND (
      EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id
              AND (x.oem_brand ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_no_3 ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_2 ILIKE '%{qe}%' ESCAPE '\\')) OR
      EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id
              AND (m.machine_brand ILIKE '%{qe}%' ESCAPE '\\' OR m.machine_model ILIKE '%{qe}%' ESCAPE '\\'))
      -- 5 字段已在 q_match 内匹配, 外层不必再检查 (JOIN q_match 即等价)
  )
"""


def build_cte_split_inline_sql(q):
    """v28-2 方案 B: CTE 拆分, EXISTS 子查询全部内联到 CTE 内
    测试 EXISTS 放 CTE 内是否影响 GIN trgm 选择"""
    qe = q.replace("'", "''")
    return f"""
WITH q_match AS (
    SELECT DISTINCT p.id AS product_id
    FROM products p
    WHERE p.is_discontinued = false
      AND p.is_published = true
      AND (
          p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR
          p.remark ILIKE '%{qe}%' ESCAPE '\\' OR
          EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id
                  AND (x.oem_brand ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_no_3 ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_2 ILIKE '%{qe}%' ESCAPE '\\')) OR
          EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id
                  AND (m.machine_brand ILIKE '%{qe}%' ESCAPE '\\' OR m.machine_model ILIKE '%{qe}%' ESCAPE '\\'))
      )
)
SELECT COUNT(*) FROM products p
JOIN q_match ON q_match.product_id = p.id
WHERE p.is_discontinued = false
  AND p.is_published = true
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)
"""


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


def explain_query(conn, sql):
    with conn.cursor() as cur:
        cur.execute(f"EXPLAIN (ANALYZE, FORMAT TEXT) {sql}")
        plan = cur.fetchall()
    exec_time = None
    plan_types = []
    for row in plan:
        line = row[0]
        if "Execution Time" in line:
            exec_time = line.strip()
        if "Seq Scan" in line:
            plan_types.append("Seq Scan")
        elif "Bitmap Index Scan" in line:
            plan_types.append("Bitmap Index Scan")
        elif "Index Scan" in line:
            plan_types.append("Index Scan")
    return {
        "exec_time": exec_time,
        "plan_types": list(set(plan_types)) or ["Unknown"],
        "plan_lines": [r[0] for r in plan[:20]]
    }


def ensure_gin_trgm_indexes(conn):
    """创建 GIN trgm 索引 (products 5 字段)"""
    with conn.cursor() as cur:
        cur.execute("CREATE EXTENSION IF NOT EXISTS pg_trgm;")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_product_name_1_trgm ON products USING gin (product_name_1 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_product_name_2_trgm ON products USING gin (product_name_2 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_oem_2_trgm ON products USING gin (oem_2 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_mr_1_trgm ON products USING gin (mr_1 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_remark_trgm ON products USING gin (remark gin_trgm_ops);")
        cur.execute("ANALYZE products;")
        conn.commit()


def drop_gin_trgm_indexes(conn):
    """清理 GIN trgm 索引"""
    with conn.cursor() as cur:
        for idx in ["ix_products_product_name_1_trgm", "ix_products_product_name_2_trgm",
                    "ix_products_oem_2_trgm", "ix_products_mr_1_trgm", "ix_products_remark_trgm"]:
            cur.execute(f"DROP INDEX IF EXISTS {idx};")
        conn.commit()


def main():
    print("=" * 80)
    print(f"V24-F94 (v28-2) 验证: CTE 拆分重写 SQL + GIN trgm 索引")
    print(f"时间: {datetime.now().isoformat()}")
    print(f"数据库: {CONN.split('dbname=')[1].split()[0] if 'dbname=' in CONN else 'unknown'}")
    print("=" * 80)

    conn = psycopg2.connect(CONN)

    # 数据规模
    with conn.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM products;")
        total_products = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM products WHERE is_published = true AND is_discontinued = false;")
        published_products = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM cross_references;")
        total_xrefs = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM machine_applications;")
        total_machines = cur.fetchone()[0]
    print(f"\n数据规模:")
    print(f"  products: {total_products} (published+not_discontinued: {published_products})")
    print(f"  cross_references: {total_xrefs}")
    print(f"  machine_applications: {total_machines}")

    test_qs = ["oil", "filter", "CAT", "bosch", "kubota"]

    # 阶段 1: baseline (无 GIN trgm)
    print(f"\n[阶段 1] baseline (无 GIN trgm) — 10 轮 P95")
    print(f"  {'q':<10} | {'baseline':<14} | {'CTE 拆分':<14} | {'CTE 内联':<14}")
    print("  " + "-" * 70)
    results_phase1 = []
    for q in test_qs:
        b_sql = build_baseline_sql(q)
        c_sql = build_cte_split_sql(q)
        i_sql = build_cte_split_inline_sql(q)

        _, b_p95, _ = measure_p95(conn, b_sql, rounds=10)
        _, c_p95, _ = measure_p95(conn, c_sql, rounds=10)
        _, i_p95, _ = measure_p95(conn, i_sql, rounds=10)

        print(f"  {q:<10} | {b_p95:>10.0f}ms | {c_p95:>10.0f}ms | {i_p95:>10.0f}ms")
        results_phase1.append({
            "q": q,
            "baseline_p95_ms": b_p95,
            "cte_split_p95_ms": c_p95,
            "cte_inline_p95_ms": i_p95
        })

    # 阶段 2: 加 GIN trgm 索引, 重测
    print(f"\n[阶段 2] 加 GIN trgm 索引 (5 字段) — 10 轮 P95")
    ensure_gin_trgm_indexes(conn)
    print(f"  {'q':<10} | {'baseline':<14} | {'CTE 拆分':<14} | {'CTE 内联':<14}")
    print("  " + "-" * 70)
    results_phase2 = []
    for q in test_qs:
        b_sql = build_baseline_sql(q)
        c_sql = build_cte_split_sql(q)
        i_sql = build_cte_split_inline_sql(q)

        _, b_p95, _ = measure_p95(conn, b_sql, rounds=10)
        _, c_p95, _ = measure_p95(conn, c_sql, rounds=10)
        _, i_p95, _ = measure_p95(conn, i_sql, rounds=10)

        print(f"  {q:<10} | {b_p95:>10.0f}ms | {c_p95:>10.0f}ms | {i_p95:>10.0f}ms")
        results_phase2.append({
            "q": q,
            "baseline_p95_ms": b_p95,
            "cte_split_p95_ms": c_p95,
            "cte_inline_p95_ms": i_p95
        })

    # 阶段 3: 看执行计划 (q='oil' 加 GIN trgm 后)
    print(f"\n[阶段 3] 执行计划对比 (q='oil', 加 GIN trgm 后)")
    for label, sql_builder in [
        ("baseline", build_baseline_sql),
        ("CTE 拆分", build_cte_split_sql),
        ("CTE 内联", build_cte_split_inline_sql),
    ]:
        sql = sql_builder("oil")
        plan = explain_query(conn, sql)
        print(f"\n  --- {label} ---")
        print(f"  执行时间: {plan['exec_time']}")
        print(f"  索引类型: {'+'.join(plan['plan_types'])}")
        for line in plan["plan_lines"][:15]:
            print(f"  {line}")

    # 阶段 4: 清理 GIN trgm
    print(f"\n[阶段 4] 清理 GIN trgm 索引 (验证完)")
    drop_gin_trgm_indexes(conn)
    print(f"  5 个 GIN trgm 索引已删除")

    # 汇总
    print("\n" + "=" * 80)
    print("汇总")
    print("=" * 80)
    avg_b1 = statistics.mean(r["baseline_p95_ms"] for r in results_phase1)
    avg_c1 = statistics.mean(r["cte_split_p95_ms"] for r in results_phase1)
    avg_i1 = statistics.mean(r["cte_inline_p95_ms"] for r in results_phase1)
    avg_b2 = statistics.mean(r["baseline_p95_ms"] for r in results_phase2)
    avg_c2 = statistics.mean(r["cte_split_p95_ms"] for r in results_phase2)
    avg_i2 = statistics.mean(r["cte_inline_p95_ms"] for r in results_phase2)

    print(f"\n5 个 q 的平均 P95 (10 轮):")
    print(f"  阶段 1 (无 GIN trgm):")
    print(f"    baseline:           {avg_b1:>8.0f}ms")
    print(f"    CTE 拆分:           {avg_c1:>8.0f}ms ({avg_b1 / avg_c1:.2f}x)" if avg_c1 > 0 else f"    CTE 拆分: N/A")
    print(f"    CTE 内联:           {avg_i1:>8.0f}ms ({avg_b1 / avg_i1:.2f}x)" if avg_i1 > 0 else f"    CTE 内联: N/A")
    print(f"  阶段 2 (加 GIN trgm):")
    print(f"    baseline:           {avg_b2:>8.0f}ms")
    print(f"    CTE 拆分:           {avg_c2:>8.0f}ms ({avg_b2 / avg_c2:.2f}x)" if avg_c2 > 0 else f"    CTE 拆分: N/A")
    print(f"    CTE 内联:           {avg_i2:>8.0f}ms ({avg_b2 / avg_i2:.2f}x)" if avg_i2 > 0 else f"    CTE 内联: N/A")

    print(f"\n结论:")
    print(f"  v28-1 baseline P95: 197ms (5 字段 OR + EXISTS xref + EXISTS machine)")
    print(f"  v28-2 目标: P95 ≤ 50ms (4x+ 提升)")
    if avg_c2 < 50 or avg_i2 < 50:
        print(f"  ✅ 达成目标! CTE 拆分 + GIN trgm P95 = {min(avg_c2, avg_i2):.0f}ms")
    elif avg_c2 < avg_b2 or avg_i2 < avg_b2:
        print(f"  ⚠️ 部分提升: CTE 拆分 + GIN trgm P95 = {min(avg_c2, avg_i2):.0f}ms (未达 4x 目标)")
    else:
        print(f"  ❌ 无提升: CTE 拆分 + GIN trgm P95 = {min(avg_c2, avg_i2):.0f}ms")

    # 保存结果
    output_path = os.path.join(os.path.dirname(__file__), "_perf_v28_2_cte_split_results.json")
    result = {
        "timestamp": datetime.now().isoformat(),
        "test_qs": test_qs,
        "phase1_no_gin_trgm": results_phase1,
        "phase2_with_gin_trgm": results_phase2,
        "avg_p95": {
            "phase1_baseline": avg_b1,
            "phase1_cte_split": avg_c1,
            "phase1_cte_inline": avg_i1,
            "phase2_baseline": avg_b2,
            "phase2_cte_split": avg_c2,
            "phase2_cte_inline": avg_i2,
        }
    }
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(f"\n结果已保存: {output_path}")

    conn.close()


if __name__ == "__main__":
    main()
