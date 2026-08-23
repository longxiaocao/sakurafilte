"""
V24-F94 (v28-2) 验证 v2: 三表 GIN trgm + CTE UNION 拆分
  v1 显示: CTE 拆分 + products 5 字段 GIN trgm P95=629ms (2.56x), 未达 4x 目标
  v2 目标: 进一步拆分 EXISTS xref/machine 到独立 CTE, 三表都加 GIN trgm

  方案 C: 三表 CTE UNION ALL
    q_match_products: products 5 字段 GIN trgm
    q_match_xref: cross_references 3 字段 GIN trgm, SELECT DISTINCT product_id
    q_match_machine: machine_applications 2 字段 GIN trgm, SELECT DISTINCT product_id
    UNION 得到候选 product_id 集合, JOIN products 基础过滤

  对比 4 种 SQL:
    1. baseline (当前): OR + EXISTS xref + EXISTS machine (P95=1611ms)
    2. CTE 拆分 v1: q_match CTE 只做 5 字段, EXISTS 仍在外层 (P95=629ms)
    3. CTE UNION v2: 三表独立 CTE UNION, EXISTS 全部剥离
    4. CTE UNION v2 + 三表 GIN trgm
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
    """当前 PostgresSearchProvider 的完整 SQL (含 EXISTS xref + EXISTS machine)"""
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


def build_cte_split_v1_sql(q):
    """v28-2 v1: CTE 拆分, 5 字段 GIN trgm + EXISTS 外层"""
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
  )
"""


def build_cte_union_v2_sql(q):
    """v28-2 v2: 三表独立 CTE UNION, EXISTS 全部剥离"""
    qe = q.replace("'", "''")
    return f"""
WITH q_match AS (
    -- products 5 字段
    SELECT p.id AS product_id
    FROM products p
    WHERE p.is_discontinued = false AND p.is_published = true
      AND (p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\'
           OR p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR p.remark ILIKE '%{qe}%' ESCAPE '\\')
    UNION
    -- cross_references 3 字段
    SELECT DISTINCT x.product_id
    FROM cross_references x
    WHERE x.is_published = true AND x.is_discontinued = false
      AND (x.oem_brand ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_no_3 ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_2 ILIKE '%{qe}%' ESCAPE '\\')
    UNION
    -- machine_applications 2 字段
    SELECT DISTINCT m.product_id
    FROM machine_applications m
    WHERE m.machine_brand ILIKE '%{qe}%' ESCAPE '\\' OR m.machine_model ILIKE '%{qe}%' ESCAPE '\\'
)
SELECT COUNT(*) FROM products p
JOIN q_match ON q_match.product_id = p.id
WHERE p.is_discontinued = false
  AND p.is_published = true
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)
"""


def build_cte_union_v2_strict_sql(q):
    """v28-2 v2 strict: 三表 CTE UNION, 但 xref/machine 子 CTE 也加 published+not_discontinued 过滤
    (避免 UNION 后 JOIN products 时再过滤, 减少 CTE 大小)"""
    qe = q.replace("'", "''")
    return f"""
WITH q_match AS (
    SELECT p.id AS product_id
    FROM products p
    WHERE p.is_discontinued = false AND p.is_published = true
      AND (p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\'
           OR p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR p.remark ILIKE '%{qe}%' ESCAPE '\\')
    UNION
    SELECT x.product_id
    FROM cross_references x
    JOIN products p ON p.id = x.product_id AND p.is_discontinued = false AND p.is_published = true
    WHERE x.is_published = true AND x.is_discontinued = false
      AND (x.oem_brand ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_no_3 ILIKE '%{qe}%' ESCAPE '\\' OR x.oem_2 ILIKE '%{qe}%' ESCAPE '\\')
    UNION
    SELECT m.product_id
    FROM machine_applications m
    JOIN products p ON p.id = m.product_id AND p.is_discontinued = false AND p.is_published = true
    WHERE m.machine_brand ILIKE '%{qe}%' ESCAPE '\\' OR m.machine_model ILIKE '%{qe}%' ESCAPE '\\'
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
        "plan_lines": [r[0] for r in plan[:25]]
    }


def ensure_all_gin_trgm_indexes(conn):
    """创建三表 GIN trgm 索引"""
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


def drop_all_gin_trgm_indexes(conn):
    """清理三表 GIN trgm 索引"""
    with conn.cursor() as cur:
        for idx in [
            "ix_products_product_name_1_trgm", "ix_products_product_name_2_trgm",
            "ix_products_oem_2_trgm", "ix_products_mr_1_trgm", "ix_products_remark_trgm",
            "ix_xrefs_oem_brand_trgm", "ix_xrefs_oem_no_3_trgm", "ix_xrefs_oem_2_trgm",
            "ix_machine_apps_brand_trgm", "ix_machine_apps_model_trgm"
        ]:
            cur.execute(f"DROP INDEX IF EXISTS {idx};")
        conn.commit()


def main():
    print("=" * 80)
    print(f"V24-F94 (v28-2) 验证 v2: 三表 GIN trgm + CTE UNION 拆分")
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

    test_qs = ["oil", "filter", "CAT", "bosch", "kubota"]

    # 阶段 1: 无 GIN trgm, 4 种 SQL 对比
    print(f"\n[阶段 1] 无 GIN trgm — 10 轮 P95")
    print(f"  {'q':<10} | {'baseline':<12} | {'CTE v1':<12} | {'CTE UNION v2':<16} | {'CTE UNION strict':<18}")
    print("  " + "-" * 90)
    results_phase1 = []
    for q in test_qs:
        _, b_p95, _ = measure_p95(conn, build_baseline_sql(q), rounds=10)
        _, c1_p95, _ = measure_p95(conn, build_cte_split_v1_sql(q), rounds=10)
        _, c2_p95, _ = measure_p95(conn, build_cte_union_v2_sql(q), rounds=10)
        _, c2s_p95, _ = measure_p95(conn, build_cte_union_v2_strict_sql(q), rounds=10)

        print(f"  {q:<10} | {b_p95:>8.0f}ms | {c1_p95:>8.0f}ms | {c2_p95:>12.0f}ms | {c2s_p95:>14.0f}ms")
        results_phase1.append({
            "q": q,
            "baseline_p95_ms": b_p95,
            "cte_v1_p95_ms": c1_p95,
            "cte_union_v2_p95_ms": c2_p95,
            "cte_union_v2_strict_p95_ms": c2s_p95
        })

    # 阶段 2: 加三表 GIN trgm, 重测
    print(f"\n[阶段 2] 加三表 GIN trgm — 10 轮 P95")
    ensure_all_gin_trgm_indexes(conn)
    print(f"  {'q':<10} | {'baseline':<12} | {'CTE v1':<12} | {'CTE UNION v2':<16} | {'CTE UNION strict':<18}")
    print("  " + "-" * 90)
    results_phase2 = []
    for q in test_qs:
        _, b_p95, _ = measure_p95(conn, build_baseline_sql(q), rounds=10)
        _, c1_p95, _ = measure_p95(conn, build_cte_split_v1_sql(q), rounds=10)
        _, c2_p95, _ = measure_p95(conn, build_cte_union_v2_sql(q), rounds=10)
        _, c2s_p95, _ = measure_p95(conn, build_cte_union_v2_strict_sql(q), rounds=10)

        print(f"  {q:<10} | {b_p95:>8.0f}ms | {c1_p95:>8.0f}ms | {c2_p95:>12.0f}ms | {c2s_p95:>14.0f}ms")
        results_phase2.append({
            "q": q,
            "baseline_p95_ms": b_p95,
            "cte_v1_p95_ms": c1_p95,
            "cte_union_v2_p95_ms": c2_p95,
            "cte_union_v2_strict_p95_ms": c2s_p95
        })

    # 阶段 3: 看执行计划 (q='oil' 加 GIN trgm 后)
    print(f"\n[阶段 3] 执行计划对比 (q='oil', 加三表 GIN trgm 后)")
    for label, sql_builder in [
        ("baseline", build_baseline_sql),
        ("CTE UNION v2", build_cte_union_v2_sql),
        ("CTE UNION strict", build_cte_union_v2_strict_sql),
    ]:
        sql = sql_builder("oil")
        plan = explain_query(conn, sql)
        print(f"\n  --- {label} ---")
        print(f"  执行时间: {plan['exec_time']}")
        print(f"  索引类型: {'+'.join(plan['plan_types'])}")
        for line in plan["plan_lines"][:20]:
            print(f"  {line}")

    # 阶段 4: 清理
    print(f"\n[阶段 4] 清理三表 GIN trgm 索引 (验证完)")
    drop_all_gin_trgm_indexes(conn)
    print(f"  10 个 GIN trgm 索引已删除")

    # 汇总
    print("\n" + "=" * 80)
    print("汇总")
    print("=" * 80)
    avg_b1 = statistics.mean(r["baseline_p95_ms"] for r in results_phase1)
    avg_c1_1 = statistics.mean(r["cte_v1_p95_ms"] for r in results_phase1)
    avg_c2_1 = statistics.mean(r["cte_union_v2_p95_ms"] for r in results_phase1)
    avg_c2s_1 = statistics.mean(r["cte_union_v2_strict_p95_ms"] for r in results_phase1)
    avg_b2 = statistics.mean(r["baseline_p95_ms"] for r in results_phase2)
    avg_c1_2 = statistics.mean(r["cte_v1_p95_ms"] for r in results_phase2)
    avg_c2_2 = statistics.mean(r["cte_union_v2_p95_ms"] for r in results_phase2)
    avg_c2s_2 = statistics.mean(r["cte_union_v2_strict_p95_ms"] for r in results_phase2)

    print(f"\n5 个 q 的平均 P95 (10 轮):")
    print(f"  阶段 1 (无 GIN trgm):")
    print(f"    baseline:           {avg_b1:>8.0f}ms")
    print(f"    CTE v1:             {avg_c1_1:>8.0f}ms ({avg_b1 / avg_c1_1:.2f}x)" if avg_c1_1 > 0 else f"    CTE v1: N/A")
    print(f"    CTE UNION v2:       {avg_c2_1:>8.0f}ms ({avg_b1 / avg_c2_1:.2f}x)" if avg_c2_1 > 0 else f"    CTE UNION v2: N/A")
    print(f"    CTE UNION strict:   {avg_c2s_1:>8.0f}ms ({avg_b1 / avg_c2s_1:.2f}x)" if avg_c2s_1 > 0 else f"    CTE UNION strict: N/A")
    print(f"  阶段 2 (加三表 GIN trgm):")
    print(f"    baseline:           {avg_b2:>8.0f}ms")
    print(f"    CTE v1:             {avg_c1_2:>8.0f}ms ({avg_b2 / avg_c1_2:.2f}x)" if avg_c1_2 > 0 else f"    CTE v1: N/A")
    print(f"    CTE UNION v2:       {avg_c2_2:>8.0f}ms ({avg_b2 / avg_c2_2:.2f}x)" if avg_c2_2 > 0 else f"    CTE UNION v2: N/A")
    print(f"    CTE UNION strict:   {avg_c2s_2:>8.0f}ms ({avg_b2 / avg_c2s_2:.2f}x)" if avg_c2s_2 > 0 else f"    CTE UNION strict: N/A")

    print(f"\n结论:")
    print(f"  v28-2 目标: P95 ≤ 50ms (32x+ 提升, baseline=1611ms)")
    best_avg = min(avg_c1_2, avg_c2_2, avg_c2s_2)
    best_label = "CTE v1" if avg_c1_2 == best_avg else ("CTE UNION v2" if avg_c2_2 == best_avg else "CTE UNION strict")
    if best_avg < 50:
        print(f"  ✅ 达成目标! {best_label} + GIN trgm P95 = {best_avg:.0f}ms ({avg_b2 / best_avg:.1f}x)")
    elif best_avg < avg_b2 / 4:
        print(f"  ✅ 达 4x 目标! {best_label} + GIN trgm P95 = {best_avg:.0f}ms ({avg_b2 / best_avg:.1f}x)")
    elif best_avg < avg_b2:
        print(f"  ⚠️ 部分提升: {best_label} + GIN trgm P95 = {best_avg:.0f}ms ({avg_b2 / best_avg:.1f}x, 未达 4x 目标)")
    else:
        print(f"  ❌ 无提升: best P95 = {best_avg:.0f}ms")

    # 保存结果
    output_path = os.path.join(os.path.dirname(__file__), "_perf_v28_2_v2_results.json")
    result = {
        "timestamp": datetime.now().isoformat(),
        "test_qs": test_qs,
        "phase1_no_gin_trgm": results_phase1,
        "phase2_with_gin_trgm": results_phase2,
        "avg_p95": {
            "phase1_baseline": avg_b1,
            "phase1_cte_v1": avg_c1_1,
            "phase1_cte_union_v2": avg_c2_1,
            "phase1_cte_union_v2_strict": avg_c2s_1,
            "phase2_baseline": avg_b2,
            "phase2_cte_v1": avg_c1_2,
            "phase2_cte_union_v2": avg_c2_2,
            "phase2_cte_union_v2_strict": avg_c2s_2,
        }
    }
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(f"\n结果已保存: {output_path}")

    conn.close()


if __name__ == "__main__":
    main()
