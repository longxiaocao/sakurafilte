"""Day 8.3 EXISTS 合并 vs 拆分 对比: PG 层 EXPLAIN ANALYZE 直接量化

对比:
  A) Day 8.2.2 合并版 (生产代码): 2 个 EXISTS
     - xref 1 个 EXISTS (OemBrand + Oem3 短路 OR)
     - machine_application 1 个 EXISTS (5 字段短路 OR)

  B) 未合并版 (Day 8.2.1 之前): 6 个独立 EXISTS
     - xref 2 个: OemBrand + Oem3
     - machine_application 5 个: MB/MM/MN/EB/ET

  C) JOIN+GROUP BY 终极优化版: 改写 EXISTS 为 JOIN
     性能上限参考, 看 PG planner 能不能进一步提升

数据规模: 101,568 products + 1.34M xref + 1.66M apps
"""
import time
import subprocess
import os

os.environ['PGPASSWORD'] = '784533'
PG = ['psql', '-h', 'localhost', '-U', 'postgres', '-d', 'spike_test_v3', '-X', '-q']


def run_sql(sql, repeats=5):
    """跑 SQL N 轮取中位数"""
    times = []
    for _ in range(repeats):
        t0 = time.perf_counter()
        r = subprocess.run(PG + ['-c', sql], capture_output=True, text=True, timeout=120, encoding='utf-8', errors='replace')
        dt = (time.perf_counter() - t0) * 1000
        assert r.returncode == 0, f'SQL 失败: {r.stderr[:300]}'
        times.append(dt)
    times.sort()
    return {
        'median': round(times[len(times) // 2], 1),
        'min': round(min(times), 1),
        'max': round(max(times), 1),
    }


def fmt(name, t):
    return f'  {name:<35}  med={t["median"]:>7.1f}ms  min={t["min"]:>7.1f}ms  max={t["max"]:>7.1f}ms'


# 通用过滤: type + 尺寸, 让数据量适中
# 数据 101,568 → 期望约 1-3K 条命中
PRED = """
    type = 'OIL FILTER'
    AND d1_mm BETWEEN 50 AND 200
"""

print('=' * 90)
print('EXISTS 合并 vs 拆分 PG 层性能对比')
print(f'数据: 101,568 products + 1.34M xref + 1.66M apps')
print('=' * 90)

# ========== 1) 仅机器应用 5 字段: 5 拆 vs 1 合 ==========
print('\n[1] 机器应用 5 字段: 5 个 EXISTS vs 1 个合并 EXISTS')
print('-' * 90)

# A) 5 个独立 EXISTS (拆分版)
sql_5_exists = f"""
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT COUNT(*) FROM products p
WHERE {PRED}
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.machine_brand = 'KOMATSU')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.machine_model = 'PC200')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.model_name = 'EXCAVATOR')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.engine_brand = 'CATERPILLAR')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.engine_type = 'C6.4');
"""
t_5 = run_sql(sql_5_exists)
print(fmt('5 个独立 EXISTS (拆分版)', t_5))

# B) 1 个合并 EXISTS (Day 8.2.2 合并版)
sql_1_exists = f"""
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT COUNT(*) FROM products p
WHERE {PRED}
  AND EXISTS (
    SELECT 1 FROM machine_applications m WHERE m.product_id = p.id
      AND m.machine_brand = 'KOMATSU'
      AND m.machine_model = 'PC200'
      AND m.model_name = 'EXCAVATOR'
      AND m.engine_brand = 'CATERPILLAR'
      AND m.engine_type = 'C6.4'
  );
"""
t_1 = run_sql(sql_1_exists)
print(fmt('1 个合并 EXISTS (生产版)', t_1))

speedup_5to1 = t_5['median'] / max(t_1['median'], 0.1)
print(f'  → 提升 {speedup_5to1:.1f}x')

# C) JOIN+GROUP BY (终极)
sql_join = f"""
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT COUNT(*) FROM (
    SELECT p.id FROM products p
    JOIN machine_applications m ON m.product_id = p.id
    WHERE {PRED}
      AND m.machine_brand = 'KOMATSU'
      AND m.machine_model = 'PC200'
      AND m.model_name = 'EXCAVATOR'
      AND m.engine_brand = 'CATERPILLAR'
      AND m.engine_type = 'C6.4'
    GROUP BY p.id
) sub;
"""
t_join = run_sql(sql_join)
print(fmt('JOIN+GROUP BY (理论上限)', t_join))
print(f'  → 相比拆分 EXISTS 提升 {t_5["median"] / max(t_join["median"], 0.1):.1f}x')

# ========== 2) xref 2 字段: 2 拆 vs 1 合 ==========
print('\n[2] xref 2 字段: 2 个 EXISTS vs 1 个合并 EXISTS')
print('-' * 90)

sql_2_xref = f"""
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT COUNT(*) FROM products p
WHERE {PRED}
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.oem_brand = 'MANN')
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.oem_no_3 = 'OEM3-001');
"""
t_2x = run_sql(sql_2_xref)
print(fmt('2 个独立 EXISTS (拆分版)', t_2x))

sql_1_xref = f"""
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT COUNT(*) FROM products p
WHERE {PRED}
  AND EXISTS (
    SELECT 1 FROM cross_references x WHERE x.product_id = p.id
      AND x.oem_brand = 'MANN'
      AND x.oem_no_3 = 'DAY82-XREF-001'
  );
"""
t_1x = run_sql(sql_1_xref)
print(fmt('1 个合并 EXISTS (生产版)', t_1x))
print(f'  → 提升 {t_2x["median"] / max(t_1x["median"], 0.1):.1f}x')

# ========== 3) 混合: xref(2) + machine(5) = 7 拆 vs 2 合 ==========
print('\n[3] 混合 7 字段: 7 个 EXISTS vs 2 个合并 EXISTS')
print('-' * 90)

sql_7 = f"""
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT COUNT(*) FROM products p
WHERE {PRED}
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.oem_brand = 'MANN')
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.oem_no_3 = 'DAY82-XREF-001')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.machine_brand = 'KOMATSU')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.machine_model = 'PC200')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.model_name = 'EXCAVATOR')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.engine_brand = 'CATERPILLAR')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id AND m.engine_type = 'C6.4');
"""
t_7 = run_sql(sql_7)
print(fmt('7 个独立 EXISTS (拆分版)', t_7))

sql_2 = f"""
EXPLAIN (ANALYZE, BUFFERS, TIMING)
SELECT COUNT(*) FROM products p
WHERE {PRED}
  AND EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id
      AND x.oem_brand = 'MANN' AND x.oem_no_3 = 'DAY82-XREF-001')
  AND EXISTS (SELECT 1 FROM machine_applications m WHERE m.product_id = p.id
      AND m.machine_brand = 'KOMATSU' AND m.machine_model = 'PC200'
      AND m.model_name = 'EXCAVATOR' AND m.engine_brand = 'CATERPILLAR'
      AND m.engine_type = 'C6.4');
"""
t_2m = run_sql(sql_2)
print(fmt('2 个合并 EXISTS (生产版)', t_2m))
print(f'  → 提升 {t_7["median"] / max(t_2m["median"], 0.1):.1f}x')

# ========== 4) 抓取执行计划对比 ==========
print('\n[4] 执行计划对比 (看 PG planner 怎么改写 EXISTS)')
print('-' * 90)

print('\n  [拆分 7 个 EXISTS] 节选:')
r = subprocess.run(PG + ['-c', sql_7], capture_output=True, text=True, encoding='utf-8', errors='replace')
lines = [l for l in (r.stdout or '').split('\n') if any(k in l for k in ('Hash', 'Index', 'Nested', 'Sub', 'Semi', 'Append', 'Scan'))]
for l in lines[:12]:
    print(f'    {l.strip()}')

print('\n  [合并 2 个 EXISTS] 节选:')
r = subprocess.run(PG + ['-c', sql_2], capture_output=True, text=True, encoding='utf-8', errors='replace')
lines = [l for l in (r.stdout or '').split('\n') if any(k in l for k in ('Hash', 'Index', 'Nested', 'Sub', 'Semi', 'Append', 'Scan'))]
for l in lines[:12]:
    print(f'    {l.strip()}')

print('\n' + '=' * 90)
print('对比完成')
print('=' * 90)
