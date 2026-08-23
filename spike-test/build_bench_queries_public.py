#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
从 PG 百万数据采样生成 public 压测查询集 (bench_queries_public.json)。
- 仅 public Meili 路径: oem_fuzzy / oem_exact / type_filter / size_h1_5mm / size_h1_10mm / fulltext
- 排除 machine_brand / machine_model (admin PG 路径, 非 public)
- 全部使用真实采样值 -> 100% 命中, 避免硬编码旧契约 54% 0 命中假象
用法: python build_bench_queries_public.py
"""
import subprocess
import json
import sys

PG_CONTAINER = "sakurafilter-perf-postgres-1"
DB = "sakurafilter_perf"


def psql(sql: str):
    r = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", "postgres", "-d", DB,
         "-tA", "-c", sql],
        capture_output=True, text=True, timeout=120)
    if r.returncode != 0:
        print(f"[psql error] {r.stderr[:300]}", file=sys.stderr)
        return []
    return [ln.strip() for ln in r.stdout.splitlines() if ln.strip()]


def main():
    queries = []

    # 1) oem_exact (6): 真实 OEM 3 精确号 (必命中)
    oems = psql(
        "SELECT oem_no_3 FROM cross_references "
        "WHERE is_discontinued=false AND oem_no_3 IS NOT NULL AND oem_no_3<>'' "
        "GROUP BY oem_no_3 ORDER BY random() LIMIT 6")
    for o in oems:
        queries.append(["oem_exact", o, None, None, None, None, 5])

    # 2) oem_fuzzy (6): 真实 OEM 3 前 2 位前缀 (必命中, typo 容忍)
    prefixes = psql(
        "SELECT p FROM (SELECT DISTINCT left(oem_no_3, 2) AS p FROM cross_references "
        "WHERE is_discontinued=false AND oem_no_3 IS NOT NULL "
        "AND length(oem_no_3) >= 4) t ORDER BY random() LIMIT 6")
    for p in prefixes:
        queries.append(["oem_fuzzy", p, None, None, None, None, 5])

    # 3) type_filter (6): 高频 type (精确过滤, 必命中)
    types = psql(
        "SELECT type FROM products WHERE type IS NOT NULL AND type<>'' "
        "GROUP BY type ORDER BY count(*) DESC LIMIT 6")
    for t in types:
        queries.append(["type_filter", None, t, None, None, None, 5])

    # 4) size_h1_5mm (6): 真实 h1 值 ±5
    h1s = psql(
        "SELECT h1_mm FROM products WHERE h1_mm IS NOT NULL "
        "ORDER BY random() LIMIT 6")
    for h in h1s:
        try:
            queries.append(["size_h1_5mm", None, None, None, None,
                            int(float(h)), 5])
        except ValueError:
            pass

    # 5) size_h1_10mm (6): 真实 h1 值 ±10
    h1s2 = psql(
        "SELECT h1_mm FROM products WHERE h1_mm IS NOT NULL "
        "ORDER BY random() LIMIT 6")
    for h in h1s2:
        try:
            queries.append(["size_h1_10mm", None, None, None, None,
                            int(float(h)), 10])
        except ValueError:
            pass

    # 6) fulltext (7): product_name_1 首词 (必命中)
    names = psql(
        "SELECT product_name_1 FROM products TABLESAMPLE SYSTEM (0.1) "
        "WHERE product_name_1 IS NOT NULL AND product_name_1<>'' LIMIT 7")
    for n in names:
        word = n.split()[0] if n.split() else n
        queries.append(["fulltext", word, None, None, None, None, 5])

    typeahead = ["B", "C", "D", "F", "H", "K", "M"]

    payload = {"queries": queries, "typeahead": typeahead}
    out = "bench_queries_public.json"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False, indent=1)

    kinds = {}
    for q in queries:
        kinds[q[0]] = kinds.get(q[0], 0) + 1
    print(f"生成 {out}: {len(queries)} 条查询, 分布 {kinds}")
    print(f"typeahead: {typeahead}")


if __name__ == "__main__":
    main()
