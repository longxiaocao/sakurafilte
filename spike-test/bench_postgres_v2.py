"""
Day 2: PostgreSQL 100 万数据性能基线 v2
优化: 内存 dict lookup 代替逐行 SQL + COPY 批量导入
"""
import psycopg2
import psycopg2.extras
import json
import time
import statistics
import io
from pathlib import Path

PG = dict(host="localhost", port=5432, dbname="postgres", user="postgres", password="784533")
OUT_DIR = Path(r"d:\projects\sakurafilter\spike-test\output")
PROD = OUT_DIR / "synthetic_products_1000k.jsonl"
XREF = OUT_DIR / "synthetic_xrefs_1000k.jsonl"
APP = OUT_DIR / "synthetic_apps_1000k.jsonl"

results = {}

def now():
    return time.perf_counter()

def time_query(cursor, sql, params=None, n_runs=10):
    times = []
    rows = 0
    for _ in range(n_runs):
        t0 = now()
        cursor.execute(sql, params or ())
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

def print_result(name, r):
    print(f"  [{name:35s}] P50={r['p50_ms']:7.1f}ms | P95={r['p95_ms']:7.1f}ms | P99={r['p99_ms']:7.1f}ms | min={r['min_ms']:7.1f}ms")

print("=" * 80)
print("Day 2 v2: PostgreSQL 100 万数据性能基线")
print("=" * 80)

conn = psycopg2.connect(**PG)
conn.autocommit = True
cur = conn.cursor()

# 1) 建库
cur.execute("SELECT 1 FROM pg_database WHERE datname='spike_test'")
if not cur.fetchone():
    cur.execute("CREATE DATABASE spike_test")
    print("[1] 创建 spike_test")
else:
    cur.execute("DROP DATABASE spike_test")
    cur.execute("CREATE DATABASE spike_test")
    print("[1] 重建 spike_test")

conn.close()
PG["dbname"] = "spike_test"
conn = psycopg2.connect(**PG)
conn.autocommit = True
cur = conn.cursor()

# 2) 建表
print("\n[2] 建表 ...")
ddl = """
DROP TABLE IF EXISTS products CASCADE;
DROP TABLE IF EXISTS cross_references CASCADE;
DROP TABLE IF EXISTS machine_applications CASCADE;
DROP TABLE IF EXISTS product_history CASCADE;

CREATE TABLE products (
  id BIGSERIAL,
  oem_no_normalized VARCHAR(50) NOT NULL,
  oem_no_display VARCHAR(50) NOT NULL,
  remark TEXT,
  product_name_3 VARCHAR(100),
  type VARCHAR(50) NOT NULL,
  d1_mm NUMERIC(8,2), d2_mm NUMERIC(8,2), d3_mm NUMERIC(8,2),
  h1_mm NUMERIC(8,2), h2_mm NUMERIC(8,2), h3_mm NUMERIC(8,2),
  d7_thread VARCHAR(50), d8_thread VARCHAR(50),
  media VARCHAR(100),
  sealing_material VARCHAR(100),
  efficiency_1 VARCHAR(100),
  bypass_valve_lr NUMERIC,
  collapse_pressure_bar NUMERIC,
  temp_range VARCHAR(50),
  qty_per_carton INT,
  weight_kgs NUMERIC(8,3),
  carton_length_mm NUMERIC(8,2),
  carton_width_mm NUMERIC(8,2),
  carton_height_mm NUMERIC(8,2),
  image_key VARCHAR(500),
  image_status VARCHAR(20) DEFAULT 'pending',
  is_discontinued BOOLEAN DEFAULT FALSE,
  discontinued_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ DEFAULT NOW(),
  PRIMARY KEY (id, type)
) PARTITION BY LIST (type);

CREATE TABLE products_air_filter PARTITION OF products FOR VALUES IN ('AIR FILTER');
CREATE TABLE products_oil_filter PARTITION OF products FOR VALUES IN ('OIL FILTER');
CREATE TABLE products_fuel_filter PARTITION OF products FOR VALUES IN ('FUEL FILTER');
CREATE TABLE products_cabin_air_filter PARTITION OF products FOR VALUES IN ('CABIN AIR FILTER');
CREATE TABLE products_hydraulic_filter PARTITION OF products FOR VALUES IN ('HYDRAULIC FILTER');
CREATE TABLE products_petrol_filter PARTITION OF products FOR VALUES IN ('PETROL FILTER');
CREATE TABLE products_air_oil_separator PARTITION OF products FOR VALUES IN ('AIR/OIL SEPARATOR');
CREATE TABLE products_activated_carbon_filter PARTITION OF products FOR VALUES IN ('ACTIVATED CARBON FILTER');
CREATE TABLE products_water_separator PARTITION OF products FOR VALUES IN ('WATER SEPARATOR');
CREATE TABLE products_spin_on_filter PARTITION OF products FOR VALUES IN ('SPIN-ON FILTER');
CREATE TABLE products_industrial_filter PARTITION OF products FOR VALUES IN ('INDUSTRIAL FILTER');
CREATE TABLE products_oil_separator PARTITION OF products FOR VALUES IN ('OIL SEPARATOR');
CREATE TABLE products_coolant_filter PARTITION OF products FOR VALUES IN ('COOLANT FILTER');
CREATE TABLE products_air_dryer PARTITION OF products FOR VALUES IN ('AIR DRYER');
CREATE TABLE products_cartridge_filter PARTITION OF products FOR VALUES IN ('CARTRIDGE FILTER');
CREATE TABLE products_diesel_filter PARTITION OF products FOR VALUES IN ('DIESEL FILTER');
CREATE TABLE products_marine_filter PARTITION OF products FOR VALUES IN ('MARINE FILTER');
CREATE TABLE products_turbo_filter PARTITION OF products FOR VALUES IN ('TURBO FILTER');
CREATE TABLE products_exhaust_filter PARTITION OF products FOR VALUES IN ('EXHAUST FILTER');
CREATE TABLE products_breather_filter PARTITION OF products FOR VALUES IN ('BREATHER FILTER');
CREATE TABLE products_power_steering_filter PARTITION OF products FOR VALUES IN ('POWER STEERING FILTER');
CREATE TABLE products_transmission_filter PARTITION OF products FOR VALUES IN ('TRANSMISSION FILTER');
CREATE TABLE products_urea_filter PARTITION OF products FOR VALUES IN ('UREA FILTER');
CREATE TABLE products_others PARTITION OF products DEFAULT;

CREATE INDEX idx_products_oem_norm ON products (oem_no_normalized);
CREATE INDEX idx_products_oem_disp ON products (oem_no_display);
CREATE INDEX idx_products_d1 ON products (d1_mm);
CREATE INDEX idx_products_d2 ON products (d2_mm);
CREATE INDEX idx_products_h1 ON products (h1_mm);
CREATE INDEX idx_products_type ON products (type);
CREATE INDEX idx_products_active ON products (id) WHERE NOT is_discontinued;

CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX idx_products_oem_trgm ON products USING GIN (oem_no_normalized gin_trgm_ops);
CREATE INDEX idx_products_oem_disp_trgm ON products USING GIN (oem_no_display gin_trgm_ops);

CREATE TABLE cross_references (
  id BIGSERIAL PRIMARY KEY,
  product_id BIGINT NOT NULL,
  product_name_1 VARCHAR(100),
  oem_brand VARCHAR(100),
  oem_no_3 VARCHAR(100),
  is_discontinued BOOLEAN DEFAULT FALSE,
  created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_xref_product ON cross_references (product_id);
CREATE INDEX idx_xref_brand_no ON cross_references (oem_brand, oem_no_3);
CREATE INDEX idx_xref_brand_trgm ON cross_references USING GIN (oem_brand gin_trgm_ops);

CREATE TABLE machine_applications (
  id BIGSERIAL PRIMARY KEY,
  product_id BIGINT NOT NULL,
  machine_brand VARCHAR(200),
  machine_model VARCHAR(200),
  model_name VARCHAR(100),
  engine_brand VARCHAR(100),
  engine_type VARCHAR(100),
  engine_energy VARCHAR(50),
  production_date_start DATE,
  is_ongoing BOOLEAN DEFAULT TRUE,
  is_discontinued BOOLEAN DEFAULT FALSE,
  created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_app_product ON machine_applications (product_id);
CREATE INDEX idx_app_brand_model ON machine_applications (machine_brand, machine_model);
CREATE INDEX idx_app_brand_trgm ON machine_applications USING GIN (machine_brand gin_trgm_ops);

CREATE TABLE product_history (
  id BIGSERIAL PRIMARY KEY,
  product_id BIGINT NOT NULL,
  change_type VARCHAR(20),
  changed_fields JSONB,
  changed_by VARCHAR(100),
  changed_at TIMESTAMPTZ DEFAULT NOW()
);
"""
cur.execute(ddl)
print("    完成")

# 3) 导入 products (用 COPY)
print("\n[3] COPY 导入 products (100 万) ...")
t0 = now()
product_cols = ["oem_no_normalized", "oem_no_display", "remark", "product_name_3", "type",
                "d1_mm", "d2_mm", "d3_mm", "h1_mm", "h2_mm", "h3_mm",
                "d7_thread", "d8_thread", "media", "sealing_material", "efficiency_1",
                "bypass_valve_lr", "collapse_pressure_bar", "temp_range",
                "qty_per_carton", "weight_kgs",
                "carton_length_mm", "carton_width_mm", "carton_height_mm",
                "image_key", "image_status", "is_discontinued"]

buf = io.StringIO()
def esc(v):
    if v is None: return "\\N"
    if isinstance(v, bool): return "t" if v else "f"
    s = str(v)
    return s.replace("\\", "\\\\").replace("\t", " ").replace("\n", " ").replace("\r", " ")

with open(PROD, encoding="utf-8") as f:
    for line in f:
        d = json.loads(line)
        buf.write("\t".join(esc(d.get(c)) for c in product_cols) + "\n")
buf.seek(0)
cur.copy_expert(f"COPY products ({','.join(product_cols)}) FROM STDIN WITH (FORMAT text, NULL '\\N')", buf)
buf.close()
prod_elapsed = now() - t0
print(f"    products: {prod_elapsed:.1f}s ({1_000_000/prod_elapsed:,.0f} rows/s)")

# 4) 构建内存 dict 映射 (一次性读入所有 OEM → id)
print("\n[4] 构建 OEM → product_id 内存映射 ...")
t0 = now()
cur.execute("SELECT oem_no_normalized, id FROM products")
oem_to_id = {oem: pid for oem, pid in cur}
print(f"    映射完成: {len(oem_to_id):,} 条 | {now()-t0:.1f}s")

# 5) 导入 xrefs (内存 lookup + execute_values 批量)
print("\n[5] 导入 cross_references (12.5M) ...")
t0 = now()
BATCH = 10000
batch = []
xref_count = 0
xref_cols = ["product_id", "product_name_1", "oem_brand", "oem_no_3", "is_discontinued"]

def flush_xref():
    global xref_count
    if not batch: return
    psycopg2.extras.execute_values(cur,
        f"INSERT INTO cross_references ({','.join(xref_cols)}) VALUES %s",
        batch, page_size=BATCH)
    xref_count += len(batch)
    batch.clear()

with open(XREF, encoding="utf-8") as f:
    for line in f:
        d = json.loads(line)
        pid = oem_to_id.get(d["product_oem"])
        if not pid: continue
        batch.append((pid, d["product_name_1"], d["oem_brand"], d["oem_no_3"], False))
        if len(batch) >= BATCH:
            flush_xref()
flush_xref()
xref_elapsed = now() - t0
print(f"    xrefs: {xref_elapsed:.1f}s ({xref_count/xref_elapsed:,.0f} rows/s)")

# 6) 导入 apps
print("\n[6] 导入 machine_applications (15.5M) ...")
t0 = now()
batch = []
app_count = 0
app_cols = ["product_id", "machine_brand", "machine_model", "model_name",
            "engine_brand", "engine_type", "engine_energy", "is_ongoing", "is_discontinued"]

def flush_app():
    global app_count
    if not batch: return
    psycopg2.extras.execute_values(cur,
        f"INSERT INTO machine_applications ({','.join(app_cols)}) VALUES %s",
        batch, page_size=BATCH)
    app_count += len(batch)
    batch.clear()

with open(APP, encoding="utf-8") as f:
    for line in f:
        d = json.loads(line)
        pid = oem_to_id.get(d["product_oem"])
        if not pid: continue
        batch.append((pid, d["machine_brand"], d["machine_model"], d["model_name"],
                      d.get("engine_brand") or None, d.get("engine_type") or None,
                      d.get("engine_energy") or None, True, False))
        if len(batch) >= BATCH:
            flush_app()
flush_app()
app_elapsed = now() - t0
print(f"    apps: {app_elapsed:.1f}s ({app_count/app_elapsed:,.0f} rows/s)")

# 释放映射内存
del oem_to_id

# 7) ANALYZE
print("\n[7] ANALYZE ...")
cur.execute("ANALYZE")
print("    完成")

# 8) 性能测试
print("\n" + "=" * 80)
print("性能测试 (5 次取 P50/P95/P99)")
print("=" * 80)

tests = {
    "1. 主键查询 - 精确 OEM":
        ("SELECT * FROM products WHERE oem_no_normalized = 'SA42359'", None),
    "2. 模糊搜索 ILIKE 'SA%' (前缀)":
        ("SELECT id, oem_no_display, type FROM products WHERE oem_no_normalized ILIKE 'SA%' LIMIT 20", None),
    "3. 模糊搜索 trgm 'SA42'":
        ("SELECT id, oem_no_display FROM products WHERE oem_no_normalized % 'SA42' ORDER BY similarity(oem_no_normalized, 'SA42') DESC LIMIT 20", None),
    "4. 精确 OEM (display)":
        ("SELECT * FROM products WHERE oem_no_display = 'SA 42359'", None),
    "5. ±5mm 范围 D1":
        ("SELECT id, oem_no_display, d1_mm FROM products WHERE d1_mm BETWEEN 173 AND 183 LIMIT 20", None),
    "6. ±5mm D1 + Type 过滤":
        ("SELECT id, oem_no_display FROM products WHERE d1_mm BETWEEN 173 AND 183 AND type = 'AIR FILTER' LIMIT 20", None),
    "7. ±10mm D1 + D2 + H1 三维":
        ("SELECT id, oem_no_display FROM products WHERE d1_mm BETWEEN 168 AND 188 AND d2_mm BETWEEN 130 AND 150 AND h1_mm BETWEEN 48 AND 68 LIMIT 20", None),
    "8. 模糊 OEM + ±5mm + Type 组合":
        ("SELECT id, oem_no_display FROM products WHERE oem_no_normalized ILIKE 'SA%' AND d1_mm BETWEEN 173 AND 183 AND type = 'AIR FILTER' LIMIT 20", None),
    "9. 交叉引用 - brand+no 精确":
        ("SELECT p.id, p.oem_no_display FROM products p JOIN cross_references x ON x.product_id = p.id WHERE x.oem_brand = 'BOSCH' AND x.oem_no_3 = '1234' LIMIT 20", None),
    "10. 交叉引用 - brand 模糊 trgm":
        ("SELECT p.id, p.oem_no_display FROM products p JOIN cross_references x ON x.product_id = p.id WHERE x.oem_brand % 'BOSCH' LIMIT 20", None),
    "11. 机型适配 - brand+model 精确":
        ("SELECT p.id, p.oem_no_display FROM products p JOIN machine_applications m ON m.product_id = p.id WHERE m.machine_brand = 'BMW' AND m.machine_model = 'B320' LIMIT 20", None),
    "12. 机型适配 - brand 模糊":
        ("SELECT p.id, p.oem_no_display FROM products p JOIN machine_applications m ON m.product_id = p.id WHERE m.machine_brand % 'BMW' LIMIT 20", None),
    "13. 全表 COUNT":
        ("SELECT COUNT(*) FROM products", None),
    "14. 软删除过滤":
        ("SELECT * FROM products WHERE NOT is_discontinued AND type = 'AIR FILTER' LIMIT 20", None),
    "15. 按 type 分组计数":
        ("SELECT type, COUNT(*) FROM products GROUP BY type ORDER BY 2 DESC", None),
    "16. 复杂查询 (OEM 模糊 + 尺寸 + Type + 不显示下架)":
        ("SELECT id, oem_no_display, d1_mm, d2_mm FROM products WHERE oem_no_normalized ILIKE '20%' AND d1_mm BETWEEN 170 AND 190 AND d2_mm BETWEEN 130 AND 150 AND type = 'OIL FILTER' AND NOT is_discontinued LIMIT 20", None),
}

for name, (sql, params) in tests.items():
    r = time_query(cur, sql, params, n_runs=5)
    print_result(name, r)
    results[name] = r

# 9) DB 大小
cur.execute("SELECT pg_size_pretty(pg_database_size('spike_test'))")
db_size = cur.fetchone()[0]

# 10) 报告
print("\n" + "=" * 80)
print("写入 SPIKE-REPORT-pg.md")
print("=" * 80)

report = f"""# PostgreSQL 100 万条数据性能基线 (Day 2)

## 导入性能
| 表 | 行数 | 耗时 | 速度 |
|---|---|---|---|
| products | 1,000,000 | {prod_elapsed:.1f}s | {1_000_000/prod_elapsed:,.0f} rows/s |
| cross_references | {xref_count:,} | {xref_elapsed:.1f}s | {xref_count/xref_elapsed:,.0f} rows/s |
| machine_applications | {app_count:,} | {app_elapsed:.1f}s | {app_count/app_elapsed:,.0f} rows/s |
| **总** | **{xref_count+app_count+1_000_000:,}** | **{prod_elapsed+xref_elapsed+app_elapsed:.1f}s** | — |

## 数据库大小: {db_size}

## 查询性能
| 测试 | P50 (ms) | P95 (ms) | P99 (ms) | 达标 (<200ms) |
|---|---|---|---|---|
"""
for name, r in results.items():
    ok = "✅" if r['p95_ms'] < 200 else ("⚠️" if r['p95_ms'] < 500 else "❌")
    report += f"| {name} | {r['p50_ms']} | {r['p95_ms']} | {r['p99_ms']} | {ok} |\n"

report += """
## 结论
详见上表
"""
with open(OUT_DIR / "SPIKE-REPORT-pg.md", "w", encoding="utf-8") as f:
    f.write(report)
print(f"报告: {OUT_DIR}\\SPIKE-REPORT-pg.md")
print(f"\n=== Day 2 完成 ===")
cur.close()
conn.close()
