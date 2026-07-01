"""只跑性能测试,数据已就绪"""
import psycopg2
import time
import statistics

PG = dict(host="localhost", port=5432, dbname="spike_test_v3", user="postgres", password="784533")

def now():
    return time.perf_counter()

def time_query(cursor, sql, params=None, n_runs=5):
    times = []
    rows = 0
    for _ in range(n_runs):
        t0 = now()
        if params:
            cursor.execute(sql, params)
        else:
            cursor.execute(sql)
        rows = cursor.rowcount
        cursor.fetchall()
        times.append((now() - t0) * 1000)
    times.sort()
    return {
        "p50_ms": round(statistics.median(times), 2),
        "p95_ms": round(times[int(len(times) * 0.95)], 2),
        "p99_ms": round(times[int(len(times) * 0.99)], 2),
        "min_ms": round(min(times), 2),
        "max_ms": round(max(times), 2),
        "rows": rows,
    }

print("=" * 80)
print("Day 2 性能测试 (数据已就绪)")
print("=" * 80)

conn = psycopg2.connect(**PG)
conn.autocommit = True
cur = conn.cursor()

# DB 信息
cur.execute("SELECT pg_size_pretty(pg_database_size('spike_test_v3'))")
db_size = cur.fetchone()[0]
cur.execute("SELECT pg_size_pretty(pg_total_relation_size('products'))")
prod_size = cur.fetchone()[0]
cur.execute("SELECT pg_size_pretty(pg_total_relation_size('cross_references'))")
xref_size = cur.fetchone()[0]
cur.execute("SELECT pg_size_pretty(pg_total_relation_size('machine_applications'))")
app_size = cur.fetchone()[0]
cur.execute("SELECT COUNT(*) FROM products")
n_prod = cur.fetchone()[0]
cur.execute("SELECT COUNT(*) FROM cross_references")
n_xref = cur.fetchone()[0]
cur.execute("SELECT COUNT(*) FROM machine_applications")
n_app = cur.fetchone()[0]

print(f"\nDB 总大小: {db_size}")
print(f"  products: {prod_size} ({n_prod:,} 行)")
print(f"  cross_references: {xref_size} ({n_xref:,} 行)")
print(f"  machine_applications: {app_size} ({n_app:,} 行)")

# ANALYZE
cur.execute("ANALYZE")

# 测试用例
tests = {
    "1. 主键查询": "SELECT * FROM products WHERE id = 500000",
    "2. 精确 OEM (normalized)": "SELECT * FROM products WHERE oem_no_normalized = 'SA42359'",
    "3. 精确 OEM (display)": "SELECT * FROM products WHERE oem_no_display = 'SA 42359'",
    "4. 模糊 ILIKE 'SA%' 前缀": "SELECT id, oem_no_display FROM products WHERE oem_no_normalized ILIKE 'SA%' LIMIT 20",
    "5. 模糊 ILIKE '20%' 2字符前缀": "SELECT id, oem_no_display FROM products WHERE oem_no_normalized ILIKE '20%' LIMIT 20",
    "6. 模糊 ILIKE 'A%' 1字符前缀(最坏)": "SELECT id, oem_no_display FROM products WHERE oem_no_normalized ILIKE 'A%' LIMIT 20",
    "7. 模糊 ILIKE '%42%' 包含": "SELECT id, oem_no_display FROM products WHERE oem_no_normalized ILIKE '%42%' LIMIT 20",
    "8. ±5mm D1": "SELECT id, oem_no_display FROM products WHERE d1_mm BETWEEN 173 AND 183 LIMIT 20",
    "9. ±5mm D1 + Type": "SELECT id FROM products WHERE d1_mm BETWEEN 173 AND 183 AND type = 'AIR FILTER' LIMIT 20",
    "10. ±10mm D1+D2+H1 三维": "SELECT id FROM products WHERE d1_mm BETWEEN 168 AND 188 AND d2_mm BETWEEN 130 AND 150 AND h1_mm BETWEEN 48 AND 68 LIMIT 20",
    "11. 模糊+±5mm+Type 组合": "SELECT id FROM products WHERE oem_no_normalized ILIKE 'SA%' AND d1_mm BETWEEN 173 AND 183 AND type = 'AIR FILTER' LIMIT 20",
    "12. 交叉引用精确 brand+no": "SELECT p.id FROM products p JOIN cross_references x ON x.product_id = p.id WHERE x.oem_brand = 'BOSCH' AND x.oem_no_3 = '1234' LIMIT 20",
    "13. 交叉引用 brand 模糊 ILIKE 'BO%'": "SELECT p.id FROM products p JOIN cross_references x ON x.product_id = p.id WHERE x.oem_brand ILIKE 'BO%' LIMIT 20",
    "14. 机型 brand+model 精确": "SELECT p.id FROM products p JOIN machine_applications m ON m.product_id = p.id WHERE m.machine_brand = 'BMW' AND m.machine_model = 'B320' LIMIT 20",
    "15. 机型 brand ILIKE 'BM%'": "SELECT p.id FROM products p JOIN machine_applications m ON m.product_id = p.id WHERE m.machine_brand ILIKE 'BM%' LIMIT 20",
    "16. 全表 COUNT": "SELECT COUNT(*) FROM products",
    "17. 按 type 分组": "SELECT type, COUNT(*) FROM products GROUP BY type ORDER BY 2 DESC",
    "18. 软删除过滤": "SELECT * FROM products WHERE NOT is_discontinued AND type = 'AIR FILTER' LIMIT 20",
    "19. 综合复杂查询(用户最常见)": "SELECT id, oem_no_display, d1_mm, d2_mm FROM products WHERE oem_no_normalized ILIKE '20%' AND d1_mm BETWEEN 170 AND 190 AND d2_mm BETWEEN 130 AND 150 AND type = 'OIL FILTER' AND NOT is_discontinued LIMIT 20",
    "20. LIKE 优化检查 (text_pattern_ops)": "SELECT id FROM products WHERE oem_no_normalized LIKE 'SA%' LIMIT 20",
}

results = {}
print("\n" + "=" * 80)
print("性能测试 (5 次取 P50/P95/P99)")
print("=" * 80)

for name, sql in tests.items():
    r = time_query(cur, sql, n_runs=5)
    if r['p95_ms'] < 50:
        eval_ = "🟢 优"
    elif r['p95_ms'] < 200:
        eval_ = "✅ 达标"
    elif r['p95_ms'] < 500:
        eval_ = "⚠️ 慢"
    else:
        eval_ = "❌ 不可用"
    print(f"  [{name:40s}] P50={r['p50_ms']:7.1f}ms | P95={r['p95_ms']:7.1f}ms | P99={r['p99_ms']:7.1f}ms | {eval_}")
    results[name] = r

# 报告
print("\n写入 SPIKE-REPORT-pg.md")
report = f"""# PostgreSQL 100 万数据性能基线 (Day 2 v3 - 完整版)

## 导入性能
| 表 | 行数 | 大小 |
|---|---|---|
| products | {n_prod:,} | {prod_size} |
| cross_references | {n_xref:,} | {xref_size} |
| machine_applications | {n_app:,} | {app_size} |
| **总** | **{n_prod+n_xref+n_app:,}** | **{db_size}** |

## 查询性能 (5 次 P50/P95/P99)
| 测试 | P50 (ms) | P95 (ms) | P99 (ms) | 评价 |
|---|---|---|---|---|
"""
for name, r in results.items():
    if r['p95_ms'] < 50:
        eval_ = "🟢 优"
    elif r['p95_ms'] < 200:
        eval_ = "✅ 达标"
    elif r['p95_ms'] < 500:
        eval_ = "⚠️ 慢"
    else:
        eval_ = "❌ 不可用"
    report += f"| {name} | {r['p50_ms']} | {r['p95_ms']} | {r['p99_ms']} | {eval_} |\n"

report += f"""

## 关键发现
详见上表。重点关注:
- 测试 6 (ILIKE 'A%' 1字符前缀,最坏情况)
- 测试 11 (用户最常见组合: 模糊+±5mm+Type)
- 测试 19 (综合复杂查询)

## 建议
- 如果 P95 < 200ms,PostgreSQL 主搜可行,Meili 仅作增强
- 如果 ILIKE 慢,加 B-tree + text_pattern_ops 索引
- 如果 trgm 必要 (含 typo 容错),等 Day 3 Meili 出来后做对比
"""
with open(r"d:\projects\sakurafilter\spike-test\output\SPIKE-REPORT-pg.md", "w", encoding="utf-8") as f:
    f.write(report)
print(f"\n报告: d:\\projects\\sakurafilter\\spike-test\\output\\SPIKE-REPORT-pg.md")
print(f"\n=== Day 2 性能测试完成 ===")
cur.close()
conn.close()
