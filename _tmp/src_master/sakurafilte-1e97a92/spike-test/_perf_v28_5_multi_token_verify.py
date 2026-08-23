"""
V24-F94 (v28-5) 多 token INTERSECT 边界压测
  v28-2 验证: 2 token INTERSECT P95=312ms (6.74x), 但 3+ token 多层 INTERSECT 是否让 PG 优化器放弃 GIN trgm?

本脚本验证:
  1. 1-5 token 的 q 查询 P95 对比
  2. 每种 token 数量下, PG 优化器是否仍选 GIN trgm Bitmap Index Scan
  3. 退化拐点定位 (若存在)

依赖:
  - spike_test_v3 库 (50K 现有数据)
  - v28-2 migration 已应用 (5 个新 GIN trgm 索引)
  - psycopg2-binary

输出:
  - spike-test/_perf_v28_5_multi_token_results.json (raw 数据)
"""
import os
import time
import statistics
import json
import psycopg2
from datetime import datetime

CONN = os.environ.get(
    "PG_TEST_CONNECTION_STRING",
    "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533"
)


def build_multi_token_sql(tokens):
    """构造 v28-2 CTE UNION + INTERSECT SQL (与 PostgresSearchProvider.BuildQMatchCte 一致)

    每个 token 独立 CTE (q_match_0, q_match_1, ...), 最终 q_match = INTERSECT

    NOTE: 测试脚本无 SQL 注入风险, 直接拼接字符串字面量 (避免 psycopg2 dict 参数问题)
    LIKE 元字符 (%, _, \\) 需手动转义, 与 C# 端 EscapeLikePattern 一致
    """
    cte_parts = []
    for i, token in enumerate(tokens):
        # LIKE 模式转义: \ → \\, % → \%, _ → \_ (与 C# EscapeLikePattern 一致)
        escaped = token.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")
        # SQL 字符串字面量中的单引号转义
        sql_literal = "'" + escaped.replace("'", "''") + "'"
        # 拼接 LIKE pattern ('%' || escaped || '%')
        like_pattern = f"'%' || {sql_literal} || '%'"
        cte_name = f"q_match_{i}"
        cte_parts.append(f"""{cte_name} AS (
    SELECT p.id AS product_id
    FROM products p
    WHERE p.is_discontinued = false AND p.is_published = true AND (
        p.product_name_1 ILIKE {like_pattern} ESCAPE '\\' OR
        p.product_name_2 ILIKE {like_pattern} ESCAPE '\\' OR
        p.oem_2 ILIKE {like_pattern} ESCAPE '\\' OR
        p.mr_1 ILIKE {like_pattern} ESCAPE '\\' OR
        p.remark ILIKE {like_pattern} ESCAPE '\\'
    )
    UNION
    SELECT DISTINCT x.product_id
    FROM cross_references x
    WHERE x.is_published = true AND x.is_discontinued = false AND (
        x.oem_brand ILIKE {like_pattern} ESCAPE '\\' OR
        x.oem_no_3 ILIKE {like_pattern} ESCAPE '\\' OR
        x.oem_2 ILIKE {like_pattern} ESCAPE '\\'
    )
    UNION
    SELECT DISTINCT m.product_id
    FROM machine_applications m
    WHERE m.machine_brand ILIKE {like_pattern} ESCAPE '\\' OR
          m.machine_model ILIKE {like_pattern} ESCAPE '\\'
)""")

    # INTERSECT 到 q_match
    intersect_parts = [f"SELECT product_id FROM q_match_{i}" for i in range(len(tokens))]
    cte_parts.append(f"q_match AS ({' INTERSECT '.join(intersect_parts)})")

    cte_prefix = "WITH " + ", ".join(cte_parts)

    # 完整 SQL (COUNT, 不取分页)
    sql = f"""{cte_prefix},
sort_cte AS (
    SELECT p.id AS product_id,
           COALESCE(MIN(xb.sort_order), 2147483647) AS brand_sort_order_min,
           COALESCE(MIN(x.sort_order), 2147483647) AS oem_list_sort_order_min
    FROM products p
    JOIN q_match ON q_match.product_id = p.id
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand AND xb.deleted_at IS NULL
    WHERE p.is_discontinued = false AND p.is_published = true
      AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)
    GROUP BY p.id
)
SELECT COUNT(*) FROM sort_cte"""
    return sql


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
        cur.execute("EXPLAIN (ANALYZE, FORMAT TEXT) " + sql)
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


def main():
    print("=" * 80)
    print(f"V24-F94 (v28-5) 多 token INTERSECT 边界压测")
    print(f"时间: {datetime.now().isoformat()}")
    print("=" * 80)

    conn = psycopg2.connect(CONN)

    # 测试场景: 1-5 token
    test_cases = [
        ("1 token", ["oil"]),
        ("2 token", ["oil", "filter"]),
        ("3 token", ["oil", "filter", "CAT"]),
        ("4 token", ["oil", "filter", "CAT", "bosch"]),
        ("5 token", ["oil", "filter", "CAT", "bosch", "kubota"]),
    ]

    print("\n[阶段 1] 1-5 token P95 对比 (10 轮/场景)")
    print(f"{'场景':<12} | {'token 数':<10} | {'P50 (ms)':<12} | {'P95 (ms)':<12} | {'执行计划':<30}")
    print("-" * 90)

    results = []
    for label, tokens in test_cases:
        sql = build_multi_token_sql(tokens)

        # 先 EXPLAIN
        plan_info = explain_query(conn, sql)

        # 再 P95 测量
        p50, p95, _ = measure_p95(conn, sql, rounds=10)

        plan_str = "+".join(plan_info["plan_types"])
        print(f"{label:<12} | {len(tokens):<10} | {p50:<12.0f} | {p95:<12.0f} | {plan_str:<30}")
        results.append({
            "label": label,
            "tokens": tokens,
            "token_count": len(tokens),
            "p50_ms": p50,
            "p95_ms": p95,
            "plan_types": plan_info["plan_types"],
            "exec_time": plan_info["exec_time"],
            "plan_lines": plan_info["plan_lines"]
        })

    # 阶段 2: 详细执行计划 (5 token 场景, 看 PG 是否放弃 GIN trgm)
    print("\n[阶段 2] 5 token 场景执行计划 (前 20 行)")
    last = results[-1]
    for line in last["plan_lines"]:
        print(line)

    # 阶段 3: 退化分析
    print("\n[阶段 3] 退化分析")
    p95_1 = results[0]["p95_ms"]
    print(f"基准 (1 token P95): {p95_1:.0f}ms")
    for r in results[1:]:
        ratio = r["p95_ms"] / p95_1 if p95_1 > 0 else 0
        print(f"  {r['label']}: P95={r['p95_ms']:.0f}ms (vs 1 token: {ratio:.2f}x), 计划: {'+'.join(r['plan_types'])}")

    # 汇总
    print("\n" + "=" * 80)
    print("结论")
    print("=" * 80)
    p95s = [r["p95_ms"] for r in results]
    max_p95 = max(p95s)
    min_p95 = min(p95s)
    print(f"P95 范围: {min_p95:.0f}ms ~ {max_p95:.0f}ms (差距 {max_p95/min_p95:.2f}x)" if min_p95 > 0 else "P95 范围: N/A")

    # 是否有退化拐点?
    plan_changes = [r["plan_types"] for r in results]
    unique_plans = set(tuple(sorted(p)) for p in plan_changes)
    if len(unique_plans) == 1:
        print(f"PG 优化器计划稳定: {'+'.join(results[0]['plan_types'])} (所有 token 数量下都选 GIN trgm)")
    else:
        print(f"PG 优化器计划变化: {len(unique_plans)} 种不同计划")
        for r in results:
            print(f"  {r['label']}: {'+'.join(r['plan_types'])}")

    # 保存
    output_path = os.path.join(os.path.dirname(__file__), "_perf_v28_5_multi_token_results.json")
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump({
            "timestamp": datetime.now().isoformat(),
            "results": results,
            "p95_range": {"min": min_p95, "max": max_p95},
            "unique_plans": [list(p) for p in unique_plans]
        }, f, ensure_ascii=False, indent=2)
    print(f"\n结果已保存: {output_path}")

    conn.close()


if __name__ == "__main__":
    main()
