"""
V24-F93 (v28-1) 验证 v3: 真实瓶颈定位 + 复合索引优化尝试
  v1/v2 显示: GIN trgm 索引不被 PG 优化器选用, 真实瓶颈是 Nested Loop + 2 EXISTS SubPlan

本脚本验证:
  1. 当前 baseline (已删 GIN trgm)
  2. 拆 OR 改 UNION ALL (让 PG 为每个分支选最优索引)
  3. 加 (is_discontinued, is_published) 复合部分索引 (减少 Nested Loop 外层行数)
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
    """当前 PostgresSearchProvider 的 SQL (OR + EXISTS)"""
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
      p.remark ILIKE '%{qe}%' ESCAPE '\\'
  )
"""


def build_union_all_sql(q):
    """改写: 5 字段 ILIKE 拆 UNION ALL, 每个 SELECT 走自己的索引 + LIMIT 1 去重"""
    qe = q.replace("'", "''")
    # 用 CTE 先预筛 published + not_discontinued + has xref, 然后 5 字段 UNION
    return f"""
WITH pids AS (
    SELECT p.id FROM products p
    WHERE p.is_discontinued = false
      AND p.is_published = true
      AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)
)
SELECT COUNT(DISTINCT pid) FROM (
    SELECT p.id AS pid FROM pids JOIN products p ON p.id = pids.id WHERE p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\'
    UNION
    SELECT p.id AS pid FROM pids JOIN products p ON p.id = pids.id WHERE p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\'
    UNION
    SELECT p.id AS pid FROM pids JOIN products p ON p.id = pids.id WHERE p.oem_2 ILIKE '%{qe}%' ESCAPE '\\'
    UNION
    SELECT p.id AS pid FROM pids JOIN products p ON p.id = pids.id WHERE p.mr_1 ILIKE '%{qe}%' ESCAPE '\\'
    UNION
    SELECT p.id AS pid FROM pids JOIN products p ON p.id = pids.id WHERE p.remark ILIKE '%{qe}%' ESCAPE '\\'
) t
"""


def build_no_xref_exists_sql(q):
    """简化: 去掉 EXISTS xref 预筛 (让 PG 只关注 5 字段 ILIKE)"""
    qe = q.replace("'", "''")
    return f"""
SELECT COUNT(*) FROM products p
WHERE p.is_discontinued = false
  AND p.is_published = true
  AND (
      p.product_name_1 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.product_name_2 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.oem_2 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.mr_1 ILIKE '%{qe}%' ESCAPE '\\' OR
      p.remark ILIKE '%{qe}%' ESCAPE '\\'
  )
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
    return statistics.median(times), sorted(times)[max(0, int(len(times) * 0.95) - 1)], times


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
        "plan_lines": [r[0] for r in plan[:15]]
    }


def main():
    print("=" * 80)
    print(f"V24-F93 (v28-1) v3: 真实瓶颈定位 + 优化方向探索")
    print(f"时间: {datetime.now().isoformat()}")
    print("=" * 80)

    conn = psycopg2.connect(CONN)

    test_qs = ["oil", "filter", "CAT", "bosch", "kubota"]

    # 阶段 1: 3 种 SQL 对比
    print("\n[阶段 1] 3 种 SQL 写法对比 (10 轮 P95)")
    print(f"{'q':<10} | {'baseline OR+EXISTS':<22} | {'UNION ALL 重写':<22} | {'简化 (无 EXISTS xref)':<22}")
    print("-" * 90)

    results = []
    for q in test_qs:
        b_sql = build_baseline_sql(q)
        u_sql = build_union_all_sql(q)
        n_sql = build_no_xref_exists_sql(q)

        b_p50, b_p95, _ = measure_p95(conn, b_sql, rounds=10)
        u_p50, u_p95, _ = measure_p95(conn, u_sql, rounds=10)
        n_p50, n_p95, _ = measure_p95(conn, n_sql, rounds=10)

        print(f"{q:<10} | {b_p95:>14.0f}ms | {u_p95:>14.0f}ms | {n_p95:>14.0f}ms")
        results.append({
            "q": q,
            "baseline_p95_ms": b_p95,
            "union_all_p95_ms": u_p95,
            "no_xref_exists_p95_ms": n_p95
        })

    # 阶段 2: 看简化 SQL 的执行计划 (是否用 GIN trgm?)
    print("\n[阶段 2] 简化 SQL (无 EXISTS xref) 执行计划 (q='oil')")
    n_sql = build_no_xref_exists_sql("oil")
    n_plan = explain_query(conn, n_sql)
    for line in n_plan["plan_lines"]:
        print(line)

    # 阶段 3: 加 GIN trgm 索引后, 简化 SQL 是否能用?
    print("\n[阶段 3] 加 GIN trgm 索引, 重测简化 SQL (q='oil')")
    with conn.cursor() as cur:
        cur.execute("CREATE EXTENSION IF NOT EXISTS pg_trgm;")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_product_name_1_trgm ON products USING gin (product_name_1 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_product_name_2_trgm ON products USING gin (product_name_2 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_oem_2_trgm ON products USING gin (oem_2 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_mr_1_trgm ON products USING gin (mr_1 gin_trgm_ops);")
        cur.execute("CREATE INDEX IF NOT EXISTS ix_products_remark_trgm ON products USING gin (remark gin_trgm_ops);")
        cur.execute("ANALYZE products;")
        conn.commit()

    print("\n  简化 SQL 重测 (10 轮):")
    print(f"  {'q':<10} | {'简化无索引':<14} | {'简化+GIN trgm':<16} | {'plan 改变?':<14}")
    print("  " + "-" * 70)
    for q in test_qs:
        n_sql = build_no_xref_exists_sql(q)
        n_p50, n_p95, _ = measure_p95(conn, n_sql, rounds=10)
        n_plan = explain_query(conn, n_sql)
        plan_str = "+".join(n_plan["plan_types"])
        print(f"  {q:<10} | {n_p95:>10.0f}ms | {n_p95:>12.0f}ms | {plan_str:<14}")

    # 阶段 4: 清理 GIN trgm
    print("\n[阶段 4] 清理 GIN trgm 索引 (验证完)")
    with conn.cursor() as cur:
        for idx in ["ix_products_product_name_1_trgm", "ix_products_product_name_2_trgm",
                    "ix_products_oem_2_trgm", "ix_products_mr_1_trgm", "ix_products_remark_trgm"]:
            cur.execute(f"DROP INDEX IF EXISTS {idx};")
        conn.commit()
    print("  5 个 GIN trgm 索引已删除")

    # 汇总
    print("\n" + "=" * 80)
    print("结论")
    print("=" * 80)
    avg_b = statistics.mean(r["baseline_p95_ms"] for r in results)
    avg_u = statistics.mean(r["union_all_p95_ms"] for r in results)
    avg_n = statistics.mean(r["no_xref_exists_p95_ms"] for r in results)
    print(f"5 个 q 的平均 P95:")
    print(f"  baseline (OR + EXISTS xref): {avg_b:.0f}ms")
    print(f"  UNION ALL 重写:              {avg_u:.0f}ms ({avg_b / avg_u:.2f}x)" if avg_u > 0 else f"  UNION ALL 重写: N/A")
    print(f"  简化 (无 EXISTS xref):       {avg_n:.0f}ms ({avg_b / avg_n:.2f}x)" if avg_n > 0 else f"  简化: N/A")

    # 保存
    output_path = os.path.join(os.path.dirname(__file__), "_perf_gin_trgm_v3_results.json")
    result = {
        "timestamp": datetime.now().isoformat(),
        "results": results,
        "avg_baseline_p95_ms": avg_b,
        "avg_union_all_p95_ms": avg_u,
        "avg_no_xref_exists_p95_ms": avg_n,
    }
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(f"\n结果已保存: {output_path}")

    conn.close()


if __name__ == "__main__":
    main()
