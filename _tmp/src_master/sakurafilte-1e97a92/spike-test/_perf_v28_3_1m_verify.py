# -*- coding: utf-8 -*-
"""
V24-F96 (v28-3) 1M 数据扩容压测验证
  目标:
    1. 1M 数据下 v28-2 CTE UNION 加速比是否保持 6.0x (vs baseline OR+2EXISTS)
    2. 多 token 1-5 INTERSECT 退化是否放大 (vs 50K 数据 v28-5 结果)
    3. PG 优化器在 1M 数据下是否仍选 GIN trgm Bitmap Index Scan

  v28-2 在 50K 数据下: baseline P95=1827ms → v28-2 P95=305ms (6.0x)
  v28-5 在 50K 数据下: 1 token P95=231ms, 5 token P95=610ms (2.64x, 趋于平缓)

  本脚本在 sakurafilter_perf_tests 库 (1M 数据) 验证:
    - 场景 A: baseline vs v28-2 单 token 对比 (5 个 q 场景)
    - 场景 B: v28-2 多 token 1-5 退化曲线 (vs v28-5 50K 结果)
    - 场景 C: EXPLAIN ANALYZE 检查 GIN trgm 使用情况

依赖:
  - sakurafilter_perf_tests 库 (950K products + 4.75M xrefs + 4.75M apps)
  - 10 个 GIN trgm 索引 (从 spike_test_v3 schema dump)
  - psycopg2-binary

输出:
  - spike-test/_perf_v28_3_1m_results.json (raw 数据)
"""
import os
import time
import statistics
import json
import psycopg2
from datetime import datetime

# 连接到 v28-3 1M 数据库
CONN = os.environ.get(
    "PG_V28_3_CONNECTION_STRING",
    "host=localhost port=5432 dbname=sakurafilter_perf_tests user=postgres password=784533"
)


def _escape_like(token):
    """LIKE 模式转义: \\ → \\\\, % → \\%, _ → \\_ (与 C# EscapeLikePattern 一致)"""
    return token.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


def _sql_literal(text):
    """生成 SQL 字符串字面量 (单引号转义)"""
    return "'" + text.replace("'", "''") + "'"


def build_baseline_sql(q_token):
    """构造 v28-2 之前的 baseline SQL (单条 OR + 2 EXISTS 子查询)
    WHY 这是 v28-1 验证时让 PG 优化器不选 GIN trgm 的写法
    多 token 时用 AND 拼接多个 EXISTS 子句 (语义对齐 INTERSECT)
    单 token 时只有一个 q, 简化为基础对比
    """
    escaped = _escape_like(q_token)
    literal = _sql_literal(escaped)
    like_pattern = f"'%' || {literal} || '%'"

    # baseline: WHERE 子句直接 OR + 2 EXISTS
    sql = f"""WITH sort_cte AS (
    SELECT p.id AS product_id
    FROM products p
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand AND xb.deleted_at IS NULL
    WHERE p.is_discontinued = false AND p.is_published = true
      AND EXISTS (
          SELECT 1 FROM cross_references x
          WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false
      )
      AND (
          p.product_name_1 ILIKE {like_pattern} ESCAPE '\\' OR
          p.product_name_2 ILIKE {like_pattern} ESCAPE '\\' OR
          p.oem_2 ILIKE {like_pattern} ESCAPE '\\' OR
          p.mr_1 ILIKE {like_pattern} ESCAPE '\\' OR
          p.remark ILIKE {like_pattern} ESCAPE '\\' OR
          EXISTS (
              SELECT 1 FROM cross_references x
              WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false AND (
                  x.oem_brand ILIKE {like_pattern} ESCAPE '\\' OR
                  x.oem_no_3 ILIKE {like_pattern} ESCAPE '\\' OR
                  x.oem_2 ILIKE {like_pattern} ESCAPE '\\'
              )
          ) OR
          EXISTS (
              SELECT 1 FROM machine_applications m
              WHERE m.product_id = p.id AND (
                  m.machine_brand ILIKE {like_pattern} ESCAPE '\\' OR
                  m.machine_model ILIKE {like_pattern} ESCAPE '\\'
              )
          )
      )
    GROUP BY p.id
)
SELECT COUNT(*) FROM sort_cte"""
    return sql


def build_v28_2_cte_union_sql(tokens):
    """构造 v28-2 CTE UNION + INTERSECT SQL (与 PostgresSearchProvider.BuildQMatchCte 一致)
    每个 token 独立 CTE (q_match_0, q_match_1, ...), 最终 q_match = INTERSECT
    """
    cte_parts = []
    for i, token in enumerate(tokens):
        escaped = _escape_like(token)
        literal = _sql_literal(escaped)
        like_pattern = f"'%' || {literal} || '%'"
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


def measure_p95(conn, sql, rounds=10, warmup=2):
    """测量 P95 (含 warmup 预热避免冷启动偏差)"""
    # warmup
    with conn.cursor() as cur:
        for _ in range(warmup):
            cur.execute(sql)
            cur.fetchone()

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
    p99 = sorted(times)[max(0, int(len(times) * 0.99) - 1)]
    return p50, p95, p99, times


def explain_query(conn, sql):
    """EXPLAIN ANALYZE 提取关键信息"""
    with conn.cursor() as cur:
        cur.execute("EXPLAIN (ANALYZE, FORMAT TEXT) " + sql)
        plan = cur.fetchall()
    exec_time = None
    plan_types = []
    for row in plan:
        line = row[0]
        if "Execution Time" in line:
            exec_time = line.strip()
        if "Seq Scan" in line and "Bitmap" not in line:
            plan_types.append("Seq Scan")
        elif "Bitmap Index Scan" in line:
            plan_types.append("Bitmap Index Scan")
        elif "Index Scan" in line and "Bitmap" not in line:
            plan_types.append("Index Scan")
        elif "GIN" in line:
            plan_types.append("GIN")
    return {
        "exec_time": exec_time,
        "plan_types": sorted(set(plan_types)) or ["Unknown"],
        "plan_lines": [r[0] for r in plan[:25]]
    }


_LOG_PATH = os.path.join(os.path.dirname(__file__), "_v28_3_perf.log")
_LOG_FP = open(_LOG_PATH, "w", encoding="utf-8")


def _print(*args, **kwargs):
    """同步写日志到文件 + 控制台 (避免 PowerShell 重定向缓冲)"""
    import sys
    line = " ".join(str(a) for a in args)
    _LOG_FP.write(line + "\n")
    _LOG_FP.flush()
    sys.stdout.write(line + "\n")
    sys.stdout.flush()


def main():
    _print("=" * 90)
    _print(f"V24-F96 (v28-3) 1M 数据扩容压测验证")
    _print(f"时间: {datetime.now().isoformat()}")
    _print(f"数据库: sakurafilter_perf_tests (950K products + 4.75M xrefs + 4.75M apps)")
    _print("=" * 90)

    conn = psycopg2.connect(CONN)

    # 数据量复核
    with conn.cursor() as cur:
        cur.execute("""SELECT
            (SELECT COUNT(*) FROM products) AS products,
            (SELECT COUNT(*) FROM cross_references) AS xrefs,
            (SELECT COUNT(*) FROM machine_applications) AS apps""")
        row = cur.fetchone()
        _print(f"\n数据量复核: products={row[0]:,} xrefs={row[1]:,} apps={row[2]:,}")

    # ============================================================
    # 场景 A: baseline vs v28-2 单 token 对比 (5 个 q 场景)
    # ============================================================
    _print("\n" + "=" * 90)
    _print("[场景 A] baseline (OR+2EXISTS) vs v28-2 (CTE UNION) 单 token 对比")
    _print("=" * 90)

    # 选 5 个具有代表性的 q (覆盖高频/中频/低频词)
    single_token_cases = [
        ("高频 oil", "oil"),
        ("高频 filter", "filter"),
        ("中频 CAT", "CAT"),
        ("中频 bosch", "bosch"),
        ("低频 kubota", "kubota"),
    ]

    _print(f"\n{'场景':<14} | {'q':<10} | {'方案':<10} | {'P50 (ms)':<10} | {'P95 (ms)':<10} | {'P99 (ms)':<10} | {'执行计划':<30}")
    _print("-" * 110)

    scenario_a_results = []
    for label, q in single_token_cases:
        # baseline
        baseline_sql = build_baseline_sql(q)
        baseline_plan = explain_query(conn, baseline_sql)
        b_p50, b_p95, b_p99, _ = measure_p95(conn, baseline_sql, rounds=10)
        plan_str_b = "+".join(baseline_plan["plan_types"])
        _print(f"{label:<14} | {q:<10} | {'baseline':<10} | {b_p50:<10.0f} | {b_p95:<10.0f} | {b_p99:<10.0f} | {plan_str_b:<30}")

        # v28-2 CTE UNION
        v28_2_sql = build_v28_2_cte_union_sql([q])
        v28_2_plan = explain_query(conn, v28_2_sql)
        v_p50, v_p95, v_p99, _ = measure_p95(conn, v28_2_sql, rounds=10)
        plan_str_v = "+".join(v28_2_plan["plan_types"])
        _print(f"{label:<14} | {q:<10} | {'v28-2':<10} | {v_p50:<10.0f} | {v_p95:<10.0f} | {v_p99:<10.0f} | {plan_str_v:<30}")

        speedup = b_p95 / v_p95 if v_p95 > 0 else 0
        _print(f"{'':<14} | {'':<10} | {'加速比':<10} | {'':<10} | {speedup:<10.2f}x |")
        _print("-" * 110)

        scenario_a_results.append({
            "label": label,
            "q": q,
            "baseline": {
                "p50_ms": b_p50, "p95_ms": b_p95, "p99_ms": b_p99,
                "plan_types": baseline_plan["plan_types"],
                "exec_time": baseline_plan["exec_time"]
            },
            "v28_2": {
                "p50_ms": v_p50, "p95_ms": v_p95, "p99_ms": v_p99,
                "plan_types": v28_2_plan["plan_types"],
                "exec_time": v28_2_plan["exec_time"]
            },
            "speedup_p95": speedup
        })

    # 场景 A 汇总
    avg_speedup = statistics.mean([r["speedup_p95"] for r in scenario_a_results])
    _print(f"\n[场景 A 汇总] 平均加速比: {avg_speedup:.2f}x (50K 数据 v28-2 验证为 6.0x)")

    # ============================================================
    # 场景 B: v28-2 多 token 1-5 退化曲线 (vs v28-5 50K 结果)
    # ============================================================
    _print("\n" + "=" * 90)
    _print("[场景 B] v28-2 多 token 1-5 INTERSECT 退化曲线 (vs 50K v28-5)")
    _print("=" * 90)

    # 与 v28-5 相同的 token 组合, 便于直接对比
    multi_token_cases = [
        ("1 token", ["oil"]),
        ("2 token", ["oil", "filter"]),
        ("3 token", ["oil", "filter", "CAT"]),
        ("4 token", ["oil", "filter", "CAT", "bosch"]),
        ("5 token", ["oil", "filter", "CAT", "bosch", "kubota"]),
    ]

    _print(f"\n{'场景':<12} | {'token 数':<10} | {'P50 (ms)':<10} | {'P95 (ms)':<10} | {'P99 (ms)':<10} | {'vs 1 token':<12} | {'执行计划':<30}")
    _print("-" * 110)

    scenario_b_results = []
    p95_1_token = None
    for label, tokens in multi_token_cases:
        sql = build_v28_2_cte_union_sql(tokens)
        plan_info = explain_query(conn, sql)
        p50, p95, p99, _ = measure_p95(conn, sql, rounds=10)

        if p95_1_token is None:
            p95_1_token = p95
            ratio_str = "1.00x (基准)"
        else:
            ratio = p95 / p95_1_token if p95_1_token > 0 else 0
            ratio_str = f"{ratio:.2f}x"

        plan_str = "+".join(plan_info["plan_types"])
        _print(f"{label:<12} | {len(tokens):<10} | {p50:<10.0f} | {p95:<10.0f} | {p99:<10.0f} | {ratio_str:<12} | {plan_str:<30}")
        scenario_b_results.append({
            "label": label,
            "tokens": tokens,
            "token_count": len(tokens),
            "p50_ms": p50, "p95_ms": p95, "p99_ms": p99,
            "vs_1_token": ratio_str,
            "plan_types": plan_info["plan_types"],
            "exec_time": plan_info["exec_time"],
            "plan_lines": plan_info["plan_lines"]
        })

    # 场景 B 汇总: 与 v28-5 50K 数据对比
    v28_5_50k = {1: 231, 2: 312, 3: 600, 4: 682, 5: 610}  # v28-5 在 50K 数据下的 P95
    _print(f"\n[场景 B 汇总] 1M vs 50K (v28-5) P95 对比:")
    _print(f"{'token 数':<10} | {'1M P95 (ms)':<14} | {'50K P95 (ms)':<14} | {'放大倍数':<10}")
    _print("-" * 60)
    for r in scenario_b_results:
        tc = r["token_count"]
        p95_1m = r["p95_ms"]
        p95_50k = v28_5_50k.get(tc, 0)
        amp = p95_1m / p95_50k if p95_50k > 0 else 0
        _print(f"{tc:<10} | {p95_1m:<14.0f} | {p95_50k:<14.0f} | {amp:<10.2f}x")

    # ============================================================
    # 场景 C: 5 token 场景详细执行计划 (检查 GIN trgm 是否仍被使用)
    # ============================================================
    _print("\n" + "=" * 90)
    _print("[场景 C] 5 token 场景执行计划 (前 25 行, 检查 GIN trgm 使用)")
    _print("=" * 90)
    last = scenario_b_results[-1]
    for line in last["plan_lines"]:
        _print(line)

    # ============================================================
    # 汇总 + 决策建议
    # ============================================================
    _print("\n" + "=" * 90)
    _print("最终决策建议")
    _print("=" * 90)

    # 1. v28-2 加速比是否保持?
    avg_speedup_str = f"{avg_speedup:.2f}x"
    if avg_speedup >= 4.0:
        decision_1 = f"v28-2 在 1M 数据下加速比保持良好 ({avg_speedup_str} vs 50K 6.0x), CTE UNION 方案有效"
    elif avg_speedup >= 2.0:
        decision_1 = f"v28-2 在 1M 数据下加速比有所下降 ({avg_speedup_str} vs 50K 6.0x), 但仍显著优于 baseline"
    else:
        decision_1 = f"v28-2 在 1M 数据下加速比严重退化 ({avg_speedup_str} vs 50K 6.0x), 需考虑 v27-1 q_match 候选集爆炸防御"
    _print(f"1. {decision_1}")

    # 2. 多 token 退化是否放大?
    p95_5_token = scenario_b_results[-1]["p95_ms"]
    p95_1_token_b = scenario_b_results[0]["p95_ms"]
    multi_token_ratio = p95_5_token / p95_1_token_b if p95_1_token_b > 0 else 0
    if multi_token_ratio <= 3.0:
        decision_2 = f"1M 数据多 token 退化可控 ({multi_token_ratio:.2f}x, 50K v28-5 为 2.64x), 无需限制 token 数量"
    elif multi_token_ratio <= 5.0:
        decision_2 = f"1M 数据多 token 退化略放大 ({multi_token_ratio:.2f}x, 50K v28-5 为 2.64x), 建议观察 P99 是否超阈值"
    else:
        decision_2 = f"1M 数据多 token 退化显著放大 ({multi_token_ratio:.2f}x, 50K v28-5 为 2.64x), 建议限制最大 token 数量"
    _print(f"2. {decision_2}")

    # 3. PG 优化器计划稳定性?
    all_plans = set()
    for r in scenario_b_results:
        all_plans.update(r["plan_types"])
    has_gin = "GIN" in all_plans or "Bitmap Index Scan" in all_plans
    has_seq = "Seq Scan" in all_plans
    if has_gin and not has_seq:
        decision_3 = "PG 优化器在 1M 数据下仍选 GIN trgm Bitmap Index Scan (无 Seq Scan 退化)"
    elif has_gin and has_seq:
        decision_3 = "PG 优化器部分场景退化为 Seq Scan (1M 数据下候选集过大)"
    else:
        decision_3 = "PG 优化器完全放弃 GIN trgm 索引 (严重退化)"
    _print(f"3. {decision_3}")

    # 4. 整体决策
    _print(f"\n4. 整体决策:")
    if avg_speedup >= 4.0 and multi_token_ratio <= 3.0 and has_gin and not has_seq:
        _print("   ✅ v28-2 CTE UNION 方案在 1M 数据下保持有效性, 维持当前实现, 无需启用 v27-1 防御")
    elif avg_speedup >= 2.0 and multi_token_ratio <= 5.0:
        _print("   ⚠️ v28-2 在 1M 数据下可用但需关注, 建议 P2 启用 v27-1 q_match 候选集爆炸防御")
    else:
        _print("   ❌ v28-2 在 1M 数据下退化严重, 需立即启用 v27-1 防御 + 考虑替代方案 (如 Meili 全量切换)")

    # 保存 raw 结果
    output_path = os.path.join(os.path.dirname(__file__), "_perf_v28_3_1m_results.json")
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump({
            "timestamp": datetime.now().isoformat(),
            "database": "sakurafilter_perf_tests",
            "data_volume": {"products": 950209, "xrefs": 4751045, "apps": 4751045},
            "scenario_a_baseline_vs_v28_2": scenario_a_results,
            "scenario_b_multi_token_1_5": scenario_b_results,
            "summary": {
                "avg_speedup_v28_2_vs_baseline": avg_speedup,
                "multi_token_ratio_5_vs_1": multi_token_ratio,
                "v28_5_50k_baseline_p95": v28_5_50k,
                "decisions": [decision_1, decision_2, decision_3]
            }
        }, f, ensure_ascii=False, indent=2)
    _print(f"\n结果已保存: {output_path}")

    conn.close()


if __name__ == "__main__":
    main()
