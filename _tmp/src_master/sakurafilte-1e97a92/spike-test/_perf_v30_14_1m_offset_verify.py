# -*- coding: utf-8 -*-
"""v30-14 1M OFFSET 深分页专项压测 (ADR #5 验证闭环)

目的:
  补 v27-3 报告 §4.5 "1M 数据扩容压测" 空缺.
  v28-3 已验证 1M 下 v28-2 CTE UNION 加速比 (6.82x) + 多 token 退化 (1.49x),
  但 OFFSET 深度专项退化比 (深档 P95 / 浅档 P95) 在 1M 数据下未验证.

设计:
  - 数据库: sakurafilter_perf_tests (950K products + 4.75M xrefs + 4.75M apps)
  - SQL: v28-2 CTE UNION (生产现状, 与 PostgresSearchProvider.SearchAsync 完全等价)
  - OFFSET 档: 0 / 10000 / 100000 / 500000 / 900000 (覆盖浅/中/深/极深 4 档)
  - 场景: baseline (无 q) + q_oil (q='oil', 高频词 25% 命中) 2 场景
    * WHY 2 场景: 50K v27-3 已验证 type/size/composite 场景 OFFSET 深度影响 ≤1.03x
      1M 专项重点是纯 OFFSET 深度退化, 2 场景足以覆盖"有/无 q_match CTE"两种 SQL 形态
    * WHY q='oil': v28-3 验证 1M 下 oil 命中 25% (236778/950209), 是高频词但非候选集爆炸 (filter 99.95%)
  - keyset 对比: 启用简化版 (last_id 取 OFFSET=500000 第 1 条 id), 量化 1M 下 keyset 潜力
  - 控制变量法: 同场景深档 P95 / 浅档 P95 = OFFSET 深度退化比 (排除 ILIKE 主导的绝对耗时)

WHY 不复用 _perf_offset_paging.py:
  - 修改原脚本会破坏 v27-3 报告可重现性
  - 1M 专项档位 (100000/500000/900000) 与 50K (200/1000/...40000) 不同
  - 场景从 5 个收敛到 2 个 (50K v27-3 已覆盖其他场景)

依赖:
  - psycopg2-binary
  - sakurafilter_perf_tests 库 (v28-3 1M 数据)

输出:
  - spike-test/_perf_v30_14_1m_offset_results.json (raw 数据)
  - spike-test/_perf_v30_14_1m_offset_report.md (人读报告 + ADR #5 决策建议)
"""
import os
import sys
import time
import json
import statistics
import psycopg2
from datetime import datetime, timezone

# ========== 路径与常量 ==========
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RESULTS_PATH = os.path.join(SCRIPT_DIR, "_perf_v30_14_1m_offset_results.json")
REPORT_PATH = os.path.join(SCRIPT_DIR, "_perf_v30_14_1m_offset_report.md")
LOG_PATH = os.path.join(SCRIPT_DIR, "_v30_14_perf.log")

CONN = os.environ.get(
    "PG_V28_3_CONNECTION_STRING",
    "host=localhost port=5432 dbname=sakurafilter_perf_tests user=postgres password=784533"
)

# ========== SQL 模板 (v28-2 CTE UNION, 与 PostgresSearchProvider.SearchAsync 等价) ==========

# 基础 WHERE (is_published + EXISTS xref), 与 BuildBaseFilter 一致
BASE_WHERE = """p.is_discontinued = false
            AND p.is_published = true
            AND EXISTS (
                SELECT 1 FROM cross_references x
                WHERE x.product_id = p.id
                  AND x.is_published = true
                  AND x.is_discontinued = false
            )"""


def _escape_like(token: str) -> str:
    """LIKE 模式转义: \\ → \\\\, % → \\%, _ → \\_ (与 C# EscapeLikePattern 一致)"""
    return token.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


def _sql_literal(text: str) -> str:
    """生成 SQL 字符串字面量 (单引号转义)"""
    return "'" + text.replace("'", "''") + "'"


def build_q_match_cte(tokens: list) -> tuple:
    """构造 q_match CTE 前缀 (与 PostgresSearchProvider.BuildQMatchCte 一致)
    返回: (cte_prefix_sql, q_params_dict)
    多 token: q_match_0, q_match_1, ... 最终 q_match = INTERSECT
    单 token: 直接 q_match
    """
    if not tokens:
        return ("", {})

    cte_parts = []
    q_params = {}
    for i, token in enumerate(tokens):
        escaped = _escape_like(token)
        param_name = f"q{i}"
        q_params[param_name] = escaped

        cte_name = f"q_match_{i}" if len(tokens) > 1 else "q_match"
        # WHY %%: psycopg2 pyformat 要求 SQL 里裸 % 必须转义为 %%
        #   占位符 %(name)s 不需要转义, 但 ILIKE '%' 里的 % 必须写成 %%
        #   psycopg2 执行时会把 %% 还原为 % 传给 PG
        like_pattern = f"'%%' || %(" + param_name + ")s || '%%'"
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

    # 多 token: INTERSECT
    if len(tokens) > 1:
        intersect_parts = [f"SELECT product_id FROM q_match_{i}" for i in range(len(tokens))]
        cte_parts.append(f"q_match AS ({' INTERSECT '.join(intersect_parts)})")

    cte_prefix = "WITH " + ", ".join(cte_parts)
    return (cte_prefix, q_params)


def build_offset_sql(q_tokens: list, offset: int, page_size: int) -> tuple:
    """OFFSET 分页 SQL (生产现状, v28-2 CTE UNION)
    返回: (sql, params)
    """
    has_q = bool(q_tokens)
    cte_prefix, q_params = build_q_match_cte(q_tokens)

    sort_cte_join = "JOIN q_match ON q_match.product_id = p.id" if has_q else ""
    cte_separator = ", " if cte_prefix else "WITH "

    sql = f"""{cte_prefix}{cte_separator}sort_cte AS (
    SELECT
        p.id AS product_id,
        COALESCE(MIN(xb.sort_order), 2147483647) AS brand_sort_order_min,
        COALESCE(MIN(x.sort_order), 2147483647) AS oem_list_sort_order_min
    FROM products p
    {sort_cte_join}
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true
        AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand
        AND xb.deleted_at IS NULL
    WHERE {BASE_WHERE}
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
LIMIT %(page_size)s OFFSET %(offset)s"""
    params = {**q_params, "page_size": page_size, "offset": offset}
    return (sql, params)


def build_keyset_sql(q_tokens: list, last_id: int, page_size: int) -> tuple:
    """keyset 简化版 SQL (WHERE p.id > :last_id, 不模拟三层排序, 仅测性能潜力)
    返回: (sql, params)
    """
    has_q = bool(q_tokens)
    cte_prefix, q_params = build_q_match_cte(q_tokens)

    if has_q:
        # 有 q: 用 q_match CTE 过滤候选, 再 WHERE p.id > last_id
        sql = f"""{cte_prefix}
SELECT
    p.id, p.mr_1, p.oem_no_display, p.type, p.updated_at
FROM products p
JOIN q_match ON q_match.product_id = p.id
WHERE p.id > %(last_id)s
  AND {BASE_WHERE}
ORDER BY p.id ASC
LIMIT %(page_size)s"""
    else:
        # 无 q: 直接 WHERE p.id > last_id
        sql = f"""SELECT
    p.id, p.mr_1, p.oem_no_display, p.type, p.updated_at
FROM products p
WHERE p.id > %(last_id)s
  AND {BASE_WHERE}
ORDER BY p.id ASC
LIMIT %(page_size)s"""
    params = {**q_params, "last_id": last_id, "page_size": page_size}
    return (sql, params)


def build_count_sql(q_tokens: list) -> tuple:
    """COUNT 查询 (与 BuildCountSql 等价, 无 LIMIT/OFFSET/ORDER BY)"""
    has_q = bool(q_tokens)
    cte_prefix, q_params = build_q_match_cte(q_tokens)
    sort_cte_join = "JOIN q_match ON q_match.product_id = p.id" if has_q else ""
    cte_separator = ", " if cte_prefix else "WITH "

    sql = f"""{cte_prefix}{cte_separator}sort_cte AS (
    SELECT p.id AS product_id
    FROM products p
    {sort_cte_join}
    LEFT JOIN cross_references x ON x.product_id = p.id
        AND x.is_published = true
        AND x.is_discontinued = false
    LEFT JOIN xref_oem_brand xb ON xb.brand = x.oem_brand
        AND xb.deleted_at IS NULL
    WHERE {BASE_WHERE}
    GROUP BY p.id
)
SELECT COUNT(*) FROM sort_cte"""
    return (sql, q_params)


# ========== PG 连接与执行 ==========

_LOG_FP = open(LOG_PATH, "w", encoding="utf-8")


def _print(*args, **kwargs):
    """同步写日志到文件 + 控制台"""
    line = " ".join(str(a) for a in args)
    _LOG_FP.write(line + "\n")
    _LOG_FP.flush()
    sys.stdout.write(line + "\n")
    sys.stdout.flush()


def exec_sql_timed(conn, sql: str, params: dict) -> tuple:
    """执行 SQL, 返回 (耗时 ms, 返回行数)"""
    t0 = time.perf_counter()
    with conn.cursor() as cur:
        cur.execute(sql, params)
        rows = cur.fetchall()
    dt_ms = (time.perf_counter() - t0) * 1000.0
    return dt_ms, len(rows)


def exec_count(conn, sql: str, params: dict) -> tuple:
    """执行 COUNT, 返回 (耗时 ms, total)"""
    t0 = time.perf_counter()
    with conn.cursor() as cur:
        cur.execute(sql, params)
        total = cur.fetchone()[0]
    dt_ms = (time.perf_counter() - t0) * 1000.0
    return dt_ms, int(total)


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


def measure_p95(conn, sql: str, params: dict, rounds: int = 5, warmup: int = 2) -> dict:
    """测量多次, 返回统计字典"""
    # warmup (不计时, 让 PG 缓存预热)
    for _ in range(warmup):
        exec_sql_timed(conn, sql, params)

    times = []
    rows_count = 0
    for _ in range(rounds):
        dt_ms, n = exec_sql_timed(conn, sql, params)
        times.append(dt_ms)
        rows_count = n

    return {
        "rounds": rounds,
        "rows_returned": rows_count,
        "min_ms": round(min(times), 2),
        "max_ms": round(max(times), 2),
        "mean_ms": round(statistics.mean(times), 2),
        "median_ms": round(statistics.median(times), 2),
        "p95_ms": round(percentile(times, 95), 2),
        "p99_ms": round(percentile(times, 99), 2),
        "all_ms": [round(t, 2) for t in times],
    }


# ========== 压测主流程 ==========

# 压测配置
OFFSET_LEVELS = [0, 10000, 100000, 500000, 900000]
PAGE_SIZE = 20
ROUNDS = 5
WARMUP = 2
KEYSET_LAST_ID_OFFSET = 500000  # keyset 对比用 OFFSET=500000 的第 1 条 id

SCENARIOS = [
    {
        "name": "baseline",
        "desc": "无关键词过滤 (仅 is_published + EXISTS xref), 测纯 OFFSET 深度影响",
        "q_tokens": [],
    },
    {
        "name": "q_oil",
        "desc": "q='oil' (高频词, 1M 数据 25% 命中, v28-3 验证), 测有 q_match CTE 时 OFFSET 深度影响",
        "q_tokens": ["oil"],
    },
]


def run_scenario(conn, scenario: dict) -> dict:
    """跑单个 scenario 的所有 OFFSET 深度档位 + keyset 对比"""
    name = scenario["name"]
    desc = scenario["desc"]
    q_tokens = scenario["q_tokens"]

    _print(f"\n{'='*90}")
    _print(f"压测场景: {name} — {desc}")
    _print(f"q_tokens: {q_tokens or '(无)'}")
    _print(f"{'='*90}")

    # COUNT 拿 total
    count_sql, count_params = build_count_sql(q_tokens)
    count_ms, total = exec_count(conn, count_sql, count_params)
    _print(f"\n[COUNT] total={total:,} (耗时 {count_ms:.1f}ms)")

    result = {
        "scenario": name,
        "desc": desc,
        "q_tokens": q_tokens,
        "total": total,
        "count_ms": round(count_ms, 2),
        "offset_results": [],
        "keyset_results": [],
    }

    # OFFSET 深度循环
    for offset in OFFSET_LEVELS:
        if offset >= total:
            _print(f"  OFFSET={offset:>7} → 跳过 (offset >= total {total})")
            result["offset_results"].append({
                "offset": offset, "skipped": True, "reason": f"offset >= total ({total})"
            })
            continue

        sql, params = build_offset_sql(q_tokens, offset, PAGE_SIZE)
        stat = measure_p95(conn, sql, params, ROUNDS, WARMUP)
        stat["offset"] = offset
        stat["page_size"] = PAGE_SIZE
        result["offset_results"].append(stat)
        _print(f"  OFFSET={offset:>7} → median={stat['median_ms']:>9.1f}ms  "
              f"p95={stat['p95_ms']:>9.1f}ms  p99={stat['p99_ms']:>9.1f}ms  "
              f"rows={stat['rows_returned']}")

    # keyset 对比 (last_id 取 OFFSET=KEYSET_LAST_ID_OFFSET 第 1 条 id)
    if total > KEYSET_LAST_ID_OFFSET:
        # 简化: 直接 SELECT id FROM products ORDER BY id OFFSET ... LIMIT 1
        # 注意: 这个 id 是全局第 N 条, 不考虑 is_published 过滤, 但 keyset 用 p.id > last_id 时
        # 后续 WHERE 会过滤, 所以 last_id 取全局 OFFSET 即可
        last_id_sql = f"SELECT id FROM products ORDER BY id OFFSET {KEYSET_LAST_ID_OFFSET} LIMIT 1"
        with conn.cursor() as cur:
            cur.execute(last_id_sql)
            row = cur.fetchone()
        if row is None:
            _print(f"  [keyset] 跳过 (找不到 OFFSET={KEYSET_LAST_ID_OFFSET} 的 id)")
        else:
            last_id = row[0]
            _print(f"\n[keyset 对比] last_id={last_id} (OFFSET={KEYSET_LAST_ID_OFFSET})")
            sql, params = build_keyset_sql(q_tokens, last_id, PAGE_SIZE)
            stat = measure_p95(conn, sql, params, ROUNDS, WARMUP)
            stat["last_id"] = last_id
            stat["page_size"] = PAGE_SIZE
            result["keyset_results"].append(stat)
            _print(f"  keyset last_id={last_id} → median={stat['median_ms']:>9.1f}ms  "
                  f"p95={stat['p95_ms']:>9.1f}ms  p99={stat['p99_ms']:>9.1f}ms  "
                  f"rows={stat['rows_returned']}")

    return result


def derive_decision(results: dict) -> str:
    """基于实测数据推导 ADR #5 决策建议 (控制变量法)"""
    advice = []

    # 1. OFFSET 深度退化分析
    advice.append("### 4.1 OFFSET 深度退化分析 (控制变量法)\n")
    advice.append("对比同一场景下深档 P95 vs 浅档 P95 (OFFSET=0), 衡量 OFFSET 深度本身的影响:\n")
    advice.append("| 场景 | 浅档 (OFFSET=0) P95 | 最深档 P95 | 深浅比 | 主要瓶颈 |")
    advice.append("|---|---:|---:|---:|---|")

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

        if shallow_p95 > 1000:
            bottleneck = "q_match CTE + ILIKE (与 OFFSET 无关)"
        elif shallow_p95 > 300:
            bottleneck = "CTE + LATERAL JOIN + EXISTS"
        else:
            bottleneck = "结果集小, 性能良好"

        advice.append(
            f"| {sc['scenario']} | {shallow_p95:.1f}ms (OFFSET={shallow['offset']}) | "
            f"{deep_p95:.1f}ms (OFFSET={deep['offset']}) | {ratio:.2f}x | {bottleneck} |"
        )

    advice.append("")
    advice.append(f"**最大深度退化比: {deep_ratio_max:.2f}x**\n")

    # 2. keyset 改造潜力
    advice.append("### 4.2 keyset 改造潜力\n")
    advice.append(f"keyset 简化版 (WHERE p.id > :last_id, last_id 来自 OFFSET={KEYSET_LAST_ID_OFFSET}) "
                  "vs OFFSET 等价深度:\n")
    advice.append("| 场景 | OFFSET P95 | keyset P95 | 性能提升 |")
    advice.append("|---|---:|---:|---:|")
    for sc in results["scenarios"]:
        if not sc.get("keyset_results"):
            continue
        # 找最接近 KEYSET_LAST_ID_OFFSET 的档位
        offset_eq = next((r for r in sc["offset_results"]
                         if not r.get("skipped") and r.get("offset") == KEYSET_LAST_ID_OFFSET), None)
        if not offset_eq:
            continue
        ks = sc["keyset_results"][0]
        uplift = offset_eq["p95_ms"] / ks["p95_ms"] if ks["p95_ms"] > 0 else float("inf")
        advice.append(
            f"| {sc['scenario']} | {offset_eq['p95_ms']:.1f}ms | "
            f"{ks['p95_ms']:.1f}ms | {uplift:.1f}x |"
        )
    advice.append("")

    # 3. 综合决策
    advice.append("### 4.3 综合决策\n")
    if deep_ratio_max <= 1.5:
        advice.append(f"- ✅ OFFSET 深度退化比 ≤ 1.5x ({deep_ratio_max:.2f}x): "
                     f"**维持 ADR #5 暂缓决策**")
        advice.append("  - 1M 数据下 OFFSET 深度本身不是主要瓶颈, 浅档/深档性能差异不显著")
        advice.append("  - v28-3 已验证 v28-2 CTE UNION 加速比 6.82x, 多 token 退化 1.49x")
        advice.append("  - 真实用户行为: 99% 在前 5 页内, 深分页罕见")
        advice.append("  - 生产环境 Meili 主路径兜底, PostgreSQL 仅 fallback")
    elif deep_ratio_max <= 3.0:
        advice.append(f"- ⚠️ OFFSET 深度退化比 {deep_ratio_max:.2f}x (1.5x ~ 3.0x): "
                     f"**继续观察, 暂缓 keyset 改造**")
        advice.append("  - 短期: 在 PostgresSearchProvider 加 max_offset 保护 (如 OFFSET > 100000 返回 400)")
        advice.append("  - 中期: 监控真实用户深分页频率, 若 > 1% 触发 keyset 改造")
    else:
        advice.append(f"- ❌ OFFSET 深度退化比 {deep_ratio_max:.2f}x (>3x): "
                     f"**建议升级 keyset 分页, 反转 ADR #5**")
        advice.append("  - 立即加 max_offset 保护")
        advice.append("  - 排期 keyset 改造 (前后端契约改造, 需 1-2 sprint)")

    advice.append("")
    advice.append("### 4.4 与 v27-3 50K 数据对比\n")
    advice.append("| 数据规模 | 场景 | 浅档 P95 | 最深档 P95 | 深浅比 |")
    advice.append("|---|---|---:|---:|---:|")
    # v27-3 50K 数据 (从 _perf_offset_report.md 提取)
    v27_3_50k = {
        "baseline": (552.7, 529.4, 0.96),
        "type_oil": (305.3, 315.9, 1.03),
        "q_filter": (1879.1, 1897.5, 1.01),
        "size_d1_100": (264.9, 267.8, 1.01),
    }
    for k, (s, d, r) in v27_3_50k.items():
        advice.append(f"| 50K (spike_test_v3) | {k} | {s:.1f}ms | {d:.1f}ms | {r:.2f}x |")
    for sc in results["scenarios"]:
        valid = [r for r in sc["offset_results"] if not r.get("skipped")]
        if len(valid) < 2:
            continue
        shallow = valid[0]
        deep = valid[-1]
        ratio = deep["p95_ms"] / shallow["p95_ms"] if shallow["p95_ms"] > 0 else 0
        advice.append(f"| 1M (sakurafilter_perf_tests) | {sc['scenario']} | "
                     f"{shallow['p95_ms']:.1f}ms | {deep['p95_ms']:.1f}ms | {ratio:.2f}x |")
    advice.append("")

    return "\n".join(advice)


def generate_report(results: dict) -> str:
    """生成 Markdown 报告"""
    ts = datetime.now(timezone.utc).astimezone().strftime("%Y-%m-%d %H:%M:%S %z")
    lines = []
    lines.append("# v30-14 1M OFFSET 深分页专项压测报告 (ADR #5 验证闭环)\n")
    lines.append(f"> 生成时间: {ts}")
    lines.append(f"> 数据库: sakurafilter_perf_tests@localhost:5432")
    lines.append(f"> 数据规模: {results['data_volume']['products']:,} products / "
                 f"{results['data_volume']['xrefs']:,} xrefs / "
                 f"{results['data_volume']['apps']:,} apps\n")

    lines.append("## 1. 摘要\n")
    lines.append(f"- 测试目的: 验证 v28-2 CTE UNION SQL 在 1M 数据下 OFFSET 深度退化比, "
                 f"补 v27-3 报告 §4.5 空缺, 闭环 ADR #5 keyset 暂缓决策")
    lines.append(f"- OFFSET 深度档: {', '.join(str(o) for o in OFFSET_LEVELS)}")
    lines.append(f"- 重复次数: {ROUNDS} (warmup {WARMUP})")
    lines.append(f"- 场景: {', '.join(s['name'] for s in SCENARIOS)}")
    lines.append(f"- keyset 对比: 启用 (last_id 来自 OFFSET={KEYSET_LAST_ID_OFFSET})\n")

    # 数据规模
    lines.append("## 2. 数据规模\n")
    lines.append("| 表 | 行数 |")
    lines.append("|---|---|")
    lines.append(f"| products | {results['data_volume']['products']:,} |")
    lines.append(f"| cross_references | {results['data_volume']['xrefs']:,} |")
    lines.append(f"| machine_applications | {results['data_volume']['apps']:,} |")
    lines.append("")

    # 每个 scenario 的 OFFSET 深度表
    lines.append("## 3. OFFSET 深度 vs 性能曲线\n")
    for i, sc in enumerate(results["scenarios"], 1):
        lines.append(f"### 3.{i} 场景: {sc['scenario']}\n")
        lines.append(f"**描述**: {sc['desc']}")
        lines.append(f"**q_tokens**: {sc['q_tokens'] or '(无)'}")
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

        if sc.get("keyset_results"):
            lines.append(f"**keyset 对比** (last_id 来自 OFFSET={KEYSET_LAST_ID_OFFSET}):\n")
            lines.append("| mode | last_id | rows | median(ms) | p95(ms) | p99(ms) |")
            lines.append("|---|---:|---:|---:|---:|---:|")
            for k in sc["keyset_results"]:
                lines.append(f"| keyset | {k['last_id']} | {k['rows_returned']} | "
                            f"{k['median_ms']} | {k['p95_ms']} | {k['p99_ms']} |")
            lines.append("")

    # ADR #5 决策建议
    lines.append("## 4. ADR #5 决策建议\n")
    lines.append(derive_decision(results))

    lines.append("\n## 5. 文件清单\n")
    lines.append("- `_perf_v30_14_1m_offset_verify.py` — 主压测脚本")
    lines.append("- `_perf_v30_14_1m_offset_results.json` — 原始结果 (本报告的数据源)")
    lines.append("- `_perf_v30_14_1m_offset_report.md` — 本报告")
    lines.append("- `_v30_14_perf.log` — 执行日志\n")

    return "\n".join(lines)


def main():
    _print("=" * 90)
    _print("v30-14 1M OFFSET 深分页专项压测 (ADR #5 验证闭环)")
    _print(f"时间: {datetime.now().isoformat()}")
    _print(f"数据库: sakurafilter_perf_tests (950K products + 4.75M xrefs + 4.75M apps)")
    _print(f"OFFSET 档: {OFFSET_LEVELS}")
    _print(f"场景: {[s['name'] for s in SCENARIOS]}")
    _print(f"重复: {ROUNDS} (warmup {WARMUP})")
    _print("=" * 90)

    conn = psycopg2.connect(CONN)

    # 数据量复核
    with conn.cursor() as cur:
        cur.execute("""SELECT
            (SELECT COUNT(*) FROM products),
            (SELECT COUNT(*) FROM cross_references),
            (SELECT COUNT(*) FROM machine_applications)""")
        row = cur.fetchone()
    n_products, n_xrefs, n_apps = row
    _print(f"\n数据量复核: products={n_products:,} xrefs={n_xrefs:,} apps={n_apps:,}")

    # 跑所有 scenario
    all_results = []
    for sc in SCENARIOS:
        r = run_scenario(conn, sc)
        all_results.append(r)

    # 汇总
    final = {
        "test_time": datetime.now(timezone.utc).astimezone().isoformat(),
        "database": "sakurafilter_perf_tests",
        "data_volume": {
            "products": n_products,
            "xrefs": n_xrefs,
            "apps": n_apps,
        },
        "config": {
            "offsets": OFFSET_LEVELS,
            "page_size": PAGE_SIZE,
            "rounds": ROUNDS,
            "warmup": WARMUP,
            "keyset_last_id_offset": KEYSET_LAST_ID_OFFSET,
            "scenarios": SCENARIOS,
        },
        "scenarios": all_results,
    }

    # 保存 raw 结果
    with open(RESULTS_PATH, "w", encoding="utf-8") as f:
        json.dump(final, f, ensure_ascii=False, indent=2)
    _print(f"\n结果已保存: {RESULTS_PATH}")

    # 生成报告
    report = generate_report(final)
    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        f.write(report)
    _print(f"报告已保存: {REPORT_PATH}")

    # 决策摘要
    _print("\n" + "=" * 90)
    _print("决策摘要")
    _print("=" * 90)
    for sc in all_results:
        valid = [r for r in sc["offset_results"] if not r.get("skipped")]
        if len(valid) < 2:
            continue
        shallow = valid[0]
        deep = valid[-1]
        ratio = deep["p95_ms"] / shallow["p95_ms"] if shallow["p95_ms"] > 0 else 0
        _print(f"{sc['scenario']}: 浅档 P95={shallow['p95_ms']:.1f}ms → "
              f"最深档 P95={deep['p95_ms']:.1f}ms (深浅比 {ratio:.2f}x)")

    conn.close()
    _print("\n压测完成.")


if __name__ == "__main__":
    main()
