# -*- coding: utf-8 -*-
"""
V24-F98 (v29-2) 高频词分布调研 (spec 28.3 P2 候选 2 决策依据)

背景: v28-3 在 1M 数据下发现高频词 "filter" 仅 1.53x 加速 (候选集爆炸)
  - 高频词命中 90%+ products 时, CTE UNION 退化为接近 Seq Scan
  - 需调研高频词分布, 决策实施方式

调研目标:
  1. 高频词 (filter/oil/air/water 等) 在 products 各字段的命中分布
  2. type 字段的 distinct 值分布 (是否分桶良好)
  3. 高频词是否集中在 type 字段 (若是, 可改用 B-tree 等值过滤)
  4. q_match CTE 候选集大小分布 (验证 "filter" 是否真的命中 90%+)

候选方案对比:
  方案 A: 高频词识别 + 改用 type 字段等值过滤 (WHERE p.type = @q)
    - 优点: B-tree 索引, 性能远优于 ILIKE GIN trgm
    - 缺点: 仅当 q 完全等于某 type 时生效; q 是 type 子串时不行
    - 适用: q='filter' 且 type='filter' 时直接走 type 索引
  方案 B: 高频词黑名单, 命中时跳过 q_match CTE
    - 优点: 简单, 直接消除候选集爆炸
    - 缺点: 改变搜索语义 (用户搜 filter 不再过滤), 用户体验差
  方案 C: q_match CTE 添加 LIMIT 50000
    - 优点: 防 INTERSECT HashSetOp Append 退化
    - 缺点: 可能漏数据, LIMIT 后 INTERSECT 结果不确定
  方案 D: type 字段补全索引 + q 命中 type distinct 值时优先走 type 索引
    - 优点: 治本, 利用现有 B-tree 索引
    - 缺点: 需要预查 type distinct 值, 实现复杂

输出:
  - spike-test/_perf_v29_2_high_freq_survey.json (raw 调研数据)
"""
import os
import json
import psycopg2
from datetime import datetime
from collections import Counter

CONNS = {
    "50K (spike_test_v3)": "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533",
    "1M (sakurafilter_perf_tests)": "host=localhost port=5432 dbname=sakurafilter_perf_tests user=postgres password=784533",
}

# 待调研的高频词 (基于 v28-3 测试场景 + 工业滤芯常见词)
HIGH_FREQ_CANDIDATES = [
    "filter", "oil", "air", "fuel", "water", "cabin", "hydraulic",
    "diesel", "separate", "carbon", "coolant", "spin", "cartridge",
    "industrial", "marine", "turbo", "exhaust", "breather",
    "CAT", "bosch", "kubota",  # 中低频对照
]


def survey_database(label, conn_str):
    """调研单个数据库的高频词分布"""
    print(f"\n{'=' * 80}")
    print(f"调研数据库: {label}")
    print(f"{'=' * 80}")

    conn = psycopg2.connect(conn_str)
    result = {"label": label, "tokens": {}}

    with conn.cursor() as cur:
        # 1. 数据量
        cur.execute("""SELECT
            (SELECT COUNT(*) FROM products WHERE is_discontinued = false AND is_published = true),
            (SELECT COUNT(*) FROM cross_references WHERE is_published = true AND is_discontinued = false),
            (SELECT COUNT(*) FROM machine_applications)""")
        total_products, total_xrefs, total_apps = cur.fetchone()
        result["data_volume"] = {
            "products_published_not_discontinued": total_products,
            "xrefs_published_not_discontinued": total_xrefs,
            "machine_applications": total_apps,
        }
        print(f"数据量: products={total_products:,} xrefs={total_xrefs:,} apps={total_apps:,}")

        # 2. type 字段 distinct 值分布 (前 30 个)
        cur.execute("""SELECT type, COUNT(*) AS cnt
            FROM products
            WHERE is_discontinued = false AND is_published = true AND type IS NOT NULL AND type != ''
            GROUP BY type ORDER BY cnt DESC LIMIT 30""")
        type_dist = cur.fetchall()
        result["type_distribution_top30"] = [{"type": t, "count": c, "pct": round(c / total_products * 100, 2)} for t, c in type_dist]
        print(f"\n[type 分布 Top 10]")
        print(f"{'type':<30} | {'count':<10} | {'pct':<8}")
        print("-" * 55)
        for t, c in type_dist[:10]:
            print(f"{t:<30} | {c:<10,} | {c/total_products*100:<8.2f}%")

        # 3. 每个高频词的命中分布
        print(f"\n[高频词命中分布]")
        print(f"{'token':<14} | {'命中 products':<12} | {'pct':<8} | {'type 完全匹配':<14} | {'是否高频':<10}")
        print("-" * 75)

        for token in HIGH_FREQ_CANDIDATES:
            # 3a. q_match CTE 命中产品数 (与 v28-2 SQL 一致: products 5 字段 + xref 3 字段 + machine 2 字段 UNION)
            cur.execute("""
                WITH q_match AS (
                    SELECT p.id AS product_id
                    FROM products p
                    WHERE p.is_discontinued = false AND p.is_published = true AND (
                        p.product_name_1 ILIKE %s OR
                        p.product_name_2 ILIKE %s OR
                        p.oem_2 ILIKE %s OR
                        p.mr_1 ILIKE %s OR
                        p.remark ILIKE %s
                    )
                    UNION
                    SELECT DISTINCT x.product_id
                    FROM cross_references x
                    WHERE x.is_published = true AND x.is_discontinued = false AND (
                        x.oem_brand ILIKE %s OR
                        x.oem_no_3 ILIKE %s OR
                        x.oem_2 ILIKE %s
                    )
                    UNION
                    SELECT DISTINCT m.product_id
                    FROM machine_applications m
                    WHERE m.machine_brand ILIKE %s OR
                          m.machine_model ILIKE %s
                )
                SELECT COUNT(*) FROM q_match""",
                (f"%{token}%", f"%{token}%", f"%{token}%", f"%{token}%", f"%{token}%",
                 f"%{token}%", f"%{token}%", f"%{token}%", f"%{token}%", f"%{token}%"))
            hit_count = cur.fetchone()[0]
            hit_pct = hit_count / total_products * 100 if total_products > 0 else 0

            # 3b. 是否存在 type 完全匹配 (case-insensitive)
            cur.execute("""SELECT COUNT(*) FROM products
                WHERE is_discontinued = false AND is_published = true AND LOWER(type) = LOWER(%s)""",
                (token,))
            type_exact_match_count = cur.fetchone()[0]

            # 3c. 是否高频 (命中 > 50% 视为候选集爆炸风险)
            is_high_freq = hit_pct > 50
            type_exact_match_str = f"{type_exact_match_count:,}" if type_exact_match_count > 0 else "无"

            print(f"{token:<14} | {hit_count:<12,} | {hit_pct:<8.2f}% | {type_exact_match_str:<14} | {'⚠️ 高频' if is_high_freq else '正常'}")

            result["tokens"][token] = {
                "hit_count": hit_count,
                "hit_pct": round(hit_pct, 2),
                "type_exact_match_count": type_exact_match_count,
                "is_high_freq": is_high_freq,
            }

        # 4. 关键问题: 高频词的命中来源分布 (产品字段 vs xref vs machine)
        print(f"\n[高频词命中来源分布 (前 5 个高频词)]")
        print(f"{'token':<14} | {'products 命中':<14} | {'xref 命中':<14} | {'machine 命中':<14} | {'总和':<14}")
        print("-" * 80)

        for token in HIGH_FREQ_CANDIDATES[:10]:
            # products 命中
            cur.execute("""SELECT COUNT(DISTINCT p.id) FROM products p
                WHERE p.is_discontinued = false AND p.is_published = true AND (
                    p.product_name_1 ILIKE %s OR p.product_name_2 ILIKE %s OR
                    p.oem_2 ILIKE %s OR p.mr_1 ILIKE %s OR p.remark ILIKE %s)""",
                (f"%{token}%", f"%{token}%", f"%{token}%", f"%{token}%", f"%{token}%"))
            p_hit = cur.fetchone()[0]

            # xref 命中 (distinct product_id)
            cur.execute("""SELECT COUNT(DISTINCT x.product_id) FROM cross_references x
                WHERE x.is_published = true AND x.is_discontinued = false AND (
                    x.oem_brand ILIKE %s OR x.oem_no_3 ILIKE %s OR x.oem_2 ILIKE %s)""",
                (f"%{token}%", f"%{token}%", f"%{token}%"))
            x_hit = cur.fetchone()[0]

            # machine 命中 (distinct product_id)
            cur.execute("""SELECT COUNT(DISTINCT m.product_id) FROM machine_applications m
                WHERE m.machine_brand ILIKE %s OR m.machine_model ILIKE %s""",
                (f"%{token}%", f"%{token}%"))
            m_hit = cur.fetchone()[0]

            print(f"{token:<14} | {p_hit:<14,} | {x_hit:<14,} | {m_hit:<14,} | {p_hit + x_hit + m_hit:<14,}")

            result["tokens"][token]["products_hit"] = p_hit
            result["tokens"][token]["xref_hit"] = x_hit
            result["tokens"][token]["machine_hit"] = m_hit

    conn.close()
    return result


def main():
    print(f"V24-F98 (v29-2) 高频词分布调研")
    print(f"时间: {datetime.now().isoformat()}")

    all_results = {}
    for label, conn_str in CONNS.items():
        all_results[label] = survey_database(label, conn_str)

    # 汇总 + 决策建议
    print(f"\n{'=' * 80}")
    print(f"汇总 + 决策建议")
    print(f"{'=' * 80}")

    # 1M 数据下高频词 (命中 > 50%)
    high_freq_1m = [(t, info) for t, info in all_results["1M (sakurafilter_perf_tests)"]["tokens"].items() if info["is_high_freq"]]
    print(f"\n1M 数据下高频词 (命中 > 50%, 候选集爆炸风险): {len(high_freq_1m)} 个")
    for token, info in high_freq_1m:
        print(f"  - {token}: 命中 {info['hit_count']:,} ({info['hit_pct']:.2f}%), type 完全匹配 {info['type_exact_match_count']:,}")

    # type 完全匹配可用的高频词 (方案 A 适用)
    type_matchable = [(t, info) for t, info in all_results["1M (sakurafilter_perf_tests)"]["tokens"].items() if info["type_exact_match_count"] > 0]
    print(f"\ntype 字段完全匹配可用的高频词 (方案 A 适用): {len(type_matchable)} 个")
    for token, info in type_matchable:
        print(f"  - {token}: type 完全匹配 {info['type_exact_match_count']:,} (q_match 命中 {info['hit_count']:,}, {info['hit_pct']:.2f}%)")

    # 决策建议
    print(f"\n决策建议:")
    if high_freq_1m:
        # 检查高频词是否都有 type 完全匹配
        all_matchable = all(info["type_exact_match_count"] > 0 for _, info in high_freq_1m)
        if all_matchable:
            print("  ✅ 方案 A (高频词识别 + type 字段等值过滤): 所有高频词都有对应 type 完全匹配, 可走 B-tree 索引")
            print("     实施方式: q token 命中 type distinct 值时, 该 token 的 q_match CTE 改用 WHERE p.type = @token (B-tree)")
            print("     预期收益: 高频词 filter 从 1.53x → ~14x (与低频词 kubota 相当)")
        else:
            not_matchable = [t for t, info in high_freq_1m if info["type_exact_match_count"] == 0]
            print(f"  ⚠️ 方案 A 仅部分适用: 高频词 {not_matchable} 无 type 完全匹配, 需配合方案 C (LIMIT)")
    else:
        print("  ℹ️ 无高频词 (>50% 命中), 候选 2 暂不需要实施")

    # 保存
    output_path = os.path.join(os.path.dirname(__file__), "_perf_v29_2_high_freq_survey.json")
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump({
            "timestamp": datetime.now().isoformat(),
            "results": all_results,
            "high_freq_1m_count": len(high_freq_1m),
            "type_matchable_count": len(type_matchable),
        }, f, ensure_ascii=False, indent=2)
    print(f"\n结果已保存: {output_path}")


if __name__ == "__main__":
    main()
