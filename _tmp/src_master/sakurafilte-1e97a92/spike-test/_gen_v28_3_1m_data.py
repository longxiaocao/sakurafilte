"""V24-F96 (v28-3) 1M 数据扩容压测 - 数据生成阶段

创建独立库 sakurafilter_perf_tests + schema + 1M products + 5M xrefs + 5M apps

策略:
  1. 用 pg_dump 从 spike_test_v3 导出 schema (含 10 个 GIN trgm 索引)
  2. 创建新库 sakurafilter_perf_tests + 应用 schema
  3. 基于 spike_test_v3 现有 50K 数据膨胀 20x:
     - products: 50K → 1M (每个产品复制 20 份, mr_1/oem_no_normalized/oem_no_display 加序号后缀保证唯一)
     - cross_references: 623K → ~5M (每条 xref 复制 8 份, 关联新 product_id)
     - machine_applications: 775K → ~5M (每条 app 复制 6.5 份, 关联新 product_id)
  4. ANALYZE 所有表

依赖:
  - pg_dump 可用 (PostgreSQL bin 路径在 PATH 中)
  - psycopg2-binary
  - spike_test_v3 库存在 (作为 schema + 数据模板)

输出:
  - 新库 sakurafilter_perf_tests (1M products + 5M xrefs + 5M apps)
"""
import os
import sys
import subprocess
import time
import psycopg2
from datetime import datetime


def _to_pg_text(v):
    """转义 Python 值为 PostgreSQL COPY TEXT 格式
    - None → \\N (PG NULL 标识符)
    - True → t, False → f (PG bool 文本表示)
    - 其他类型保持原样 (str/int/float/Decimal/datetime)
    """
    if v is None:
        return r"\N"
    if v is True:
        return "t"
    if v is False:
        return "f"
    return v

# 连接参数
ADMIN_CONN = "host=localhost port=5432 user=postgres password=784533"
SPIKE_V3_CONN = "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533"
PERF_DB = "sakurafilter_perf_tests"
PERF_CONN = f"host=localhost port=5432 dbname={PERF_DB} user=postgres password=784533"


def run(cmd, check=True):
    """执行命令并打印输出"""
    print(f">>> {cmd}")
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.stdout:
        print(result.stdout)
    if result.stderr:
        print(result.stderr, file=sys.stderr)
    if check and result.returncode != 0:
        raise RuntimeError(f"命令失败 (exit={result.returncode}): {cmd}")
    return result


def step1_create_database():
    """步骤 1: 创建独立库"""
    print("\n" + "=" * 80)
    print("[步骤 1] 创建独立库 sakurafilter_perf_tests")
    print("=" * 80)

    conn = psycopg2.connect(ADMIN_CONN)
    conn.autocommit = True
    cur = conn.cursor()

    # 检查库是否已存在
    cur.execute("SELECT 1 FROM pg_database WHERE datname = %s", (PERF_DB,))
    if cur.fetchone():
        print(f"库 {PERF_DB} 已存在, 先删除重建 (避免脏数据)")
        # 先终止所有连接
        cur.execute(f"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{PERF_DB}' AND pid <> pg_backend_pid()")
        cur.execute(f"DROP DATABASE {PERF_DB}")
        print(f"  已删除旧库 {PERF_DB}")

    cur.execute(f"CREATE DATABASE {PERF_DB}")
    print(f"  已创建新库 {PERF_DB}")

    cur.close()
    conn.close()


def step2_dump_and_apply_schema():
    """步骤 2: 从 spike_test_v3 导出 schema 并应用到新库"""
    print("\n" + "=" * 80)
    print("[步骤 2] 导出 spike_test_v3 schema 并应用到 sakurafilter_perf_tests")
    print("=" * 80)

    schema_file = os.path.join(os.path.dirname(__file__), "_v28_3_schema_dump.sql")

    # pg_dump 导出 schema (仅结构, 不含数据)
    # NOTE: pg_dump --schema-only 会导出所有表 + 索引 + 约束 + 序列
    cmd = f'pg_dump --schema-only --no-owner --no-privileges "{SPIKE_V3_CONN}" > "{schema_file}"'
    run(cmd)

    # 应用 schema 到新库
    cmd = f'psql "{PERF_CONN}" -f "{schema_file}"'
    run(cmd)

    # 重置所有序列 (为后续 INSERT 做准备)
    print("重置序列...")
    conn = psycopg2.connect(PERF_CONN)
    conn.autocommit = True
    cur = conn.cursor()
    cur.execute("""
        SELECT setval(pg_get_serial_sequence(t.tablename, 'id'),
                      1, false)
        FROM (SELECT tablename FROM pg_tables WHERE schemaname = 'public') t
        WHERE EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_name = t.tablename AND column_name = 'id'
        )
    """)
    print(f"  已重置 {cur.rowcount} 个序列")
    cur.close()
    conn.close()

    print(f"  schema 文件: {schema_file}")


def step3_generate_products(target_count=1_000_000):
    """步骤 3a: 生成 1M products (基于 spike_test_v3 50K 膨胀 20x)"""
    print("\n" + "=" * 80)
    print(f"[步骤 3a] 生成 {target_count:,} products")
    print("=" * 80)

    src_conn = psycopg2.connect(SPIKE_V3_CONN)
    src_cur = src_conn.cursor()

    # 读取 spike_test_v3 全部 50K products
    print("  读取 spike_test_v3 products 模板...")
    src_cur.execute("""
        SELECT mr_1, oem_no_normalized, oem_no_display, remark,
               product_name_1, product_name_2, product_name_3, oem_2,
               type, d1_mm, d2_mm, d3_mm, h1_mm, h2_mm, h3_mm,
               d7_thread, d8_thread, media, sealing_material,
               efficiency_1, bypass_valve_lr, collapse_pressure_bar,
               temp_range, is_published, is_discontinued, updated_at
        FROM products
        ORDER BY id
    """)
    templates = src_cur.fetchall()
    print(f"  读取 {len(templates):,} 条模板")
    src_cur.close()
    src_conn.close()

    # 膨胀到 1M: 每条模板复制 N 份, mr_1/oem_no_normalized/oem_no_display 加序号后缀
    print(f"  膨胀到 {target_count:,} (每条模板复制 {target_count // len(templates)} 份)...")

    dst_conn = psycopg2.connect(PERF_CONN)
    dst_cur = dst_conn.cursor()

    # 批量 INSERT (用 COPY 加速)
    from io import StringIO
    import csv

    buf = StringIO()
    writer = csv.writer(buf, delimiter='\t', lineterminator='\n')

    copies_per_template = target_count // len(templates)
    total = 0
    t0 = time.time()

    for i, tpl in enumerate(templates):
        for j in range(copies_per_template):
            # 修改 mr_1 / oem_no_normalized / oem_no_display 加序号后缀
            # mr_1 限制 10 字符 [A-Za-z0-9] (chk_mr_1_format 约束)
            # 策略: 用 i (0-49999) + j (0-19) 组合, 转为 36 进制字符串
            seq = i * copies_per_template + j  # 0 - 999999
            # mr_1: "P" + 9 位数字 (P + 9 = 10 字符, 符合约束)
            new_mr1 = f"P{seq:09d}"
            # oem_no_normalized: 与 mr_1 一致 (用于搜索)
            new_oem_normalized = new_mr1
            # oem_no_display: 与 mr_1 一致
            new_oem_display = new_mr1

            # 写入行 (id 由 PG 分配, 不写入)
            row = (
                new_mr1, new_oem_normalized, new_oem_display, tpl[3],  # remark
                tpl[4], tpl[5], tpl[6], tpl[7],  # product_name_1/2/3, oem_2
                tpl[8],  # type
                tpl[9], tpl[10], tpl[11], tpl[12], tpl[13], tpl[14],  # d1-h3
                tpl[15], tpl[16], tpl[17], tpl[18], tpl[19], tpl[20], tpl[21], tpl[22],  # d7-temp_range
                tpl[23], tpl[24],  # is_published, is_discontinued
                tpl[25],  # updated_at
            )
            writer.writerow([_to_pg_text(v) for v in row])
            total += 1

            # 每 100K 行 flush 一次
            if total % 100_000 == 0:
                buf.seek(0)
                dst_cur.copy_from(buf, "products",
                                  columns=("mr_1", "oem_no_normalized", "oem_no_display", "remark",
                                           "product_name_1", "product_name_2", "product_name_3", "oem_2",
                                           "type",
                                           "d1_mm", "d2_mm", "d3_mm", "h1_mm", "h2_mm", "h3_mm",
                                           "d7_thread", "d8_thread", "media", "sealing_material",
                                           "efficiency_1", "bypass_valve_lr", "collapse_pressure_bar",
                                           "temp_range", "is_published", "is_discontinued", "updated_at"))
                buf = StringIO()
                writer = csv.writer(buf, delimiter='\t', lineterminator='\n')
                elapsed = time.time() - t0
                rate = total / elapsed if elapsed > 0 else 0
                print(f"    已写入 {total:,} 行 ({elapsed:.1f}s, {rate:.0f} 行/s)")

    # flush 剩余
    if total % 100_000 != 0:
        buf.seek(0)
        dst_cur.copy_from(buf, "products",
                          columns=("mr_1", "oem_no_normalized", "oem_no_display", "remark",
                                   "product_name_1", "product_name_2", "product_name_3", "oem_2",
                                   "type",
                                   "d1_mm", "d2_mm", "d3_mm", "h1_mm", "h2_mm", "h3_mm",
                                   "d7_thread", "d8_thread", "media", "sealing_material",
                                   "efficiency_1", "bypass_valve_lr", "collapse_pressure_bar",
                                   "temp_range", "is_published", "is_discontinued", "updated_at"))

    dst_conn.commit()
    elapsed = time.time() - t0
    print(f"  完成: {total:,} 行, 耗时 {elapsed:.1f}s")

    # 验证
    dst_cur.execute("SELECT COUNT(*) FROM products")
    actual = dst_cur.fetchone()[0]
    print(f"  验证: products 表共 {actual:,} 行")

    dst_cur.close()
    dst_conn.close()
    return actual


def step4_generate_xrefs(target_count=5_000_000):
    """步骤 3b: 生成 5M cross_references"""
    print("\n" + "=" * 80)
    print(f"[步骤 3b] 生成 ~{target_count:,} cross_references")
    print("=" * 80)

    src_conn = psycopg2.connect(SPIKE_V3_CONN)
    src_cur = src_conn.cursor()

    # 读取 spike_test_v3 全部 xrefs (作为模板)
    print("  读取 spike_test_v3 cross_references 模板...")
    src_cur.execute("""
        SELECT oem_brand, oem_no_3, oem_2, sort_order, is_published, is_discontinued, updated_at
        FROM cross_references
        ORDER BY id
    """)
    templates = src_cur.fetchall()
    print(f"  读取 {len(templates):,} 条模板")
    src_cur.close()
    src_conn.close()

    dst_conn = psycopg2.connect(PERF_CONN)
    dst_cur = dst_conn.cursor()

    # 获取新库 products 的 id 范围
    dst_cur.execute("SELECT MIN(id), MAX(id), COUNT(*) FROM products")
    min_id, max_id, total_products = dst_cur.fetchone()
    print(f"  新库 products id 范围: {min_id} ~ {max_id} (共 {total_products:,})")

    # 策略: 每个新 product_id 分配 target_count / total_products 个 xref
    xrefs_per_product = target_count // total_products
    print(f"  每个 product 分配 {xrefs_per_product} 个 xref")

    from io import StringIO
    import csv

    buf = StringIO()
    writer = csv.writer(buf, delimiter='\t', lineterminator='\n')

    total = 0
    t0 = time.time()
    batch_size = 0

    # 遍历每个 product_id, 为它分配 xrefs_per_product 个 xref (从模板循环取)
    template_count = len(templates)
    for product_id in range(min_id, max_id + 1):
        for k in range(xrefs_per_product):
            tpl_idx = (product_id * xrefs_per_product + k) % template_count
            tpl = templates[tpl_idx]
            row = (product_id, tpl[0], tpl[1], tpl[2], tpl[3], tpl[4], tpl[5], tpl[6])
            writer.writerow([_to_pg_text(v) for v in row])
            total += 1
            batch_size += 1

            if batch_size >= 100_000:
                buf.seek(0)
                dst_cur.copy_from(buf, "cross_references",
                                  columns=("product_id", "oem_brand", "oem_no_3", "oem_2",
                                           "sort_order", "is_published", "is_discontinued", "updated_at"))
                buf = StringIO()
                writer = csv.writer(buf, delimiter='\t', lineterminator='\n')
                batch_size = 0
                elapsed = time.time() - t0
                rate = total / elapsed if elapsed > 0 else 0
                print(f"    已写入 {total:,} 行 ({elapsed:.1f}s, {rate:.0f} 行/s)")

    if batch_size > 0:
        buf.seek(0)
        dst_cur.copy_from(buf, "cross_references",
                          columns=("product_id", "oem_brand", "oem_no_3", "oem_2",
                                   "sort_order", "is_published", "is_discontinued", "updated_at"))

    dst_conn.commit()
    elapsed = time.time() - t0
    print(f"  完成: {total:,} 行, 耗时 {elapsed:.1f}s")

    dst_cur.execute("SELECT COUNT(*) FROM cross_references")
    actual = dst_cur.fetchone()[0]
    print(f"  验证: cross_references 表共 {actual:,} 行")

    dst_cur.close()
    dst_conn.close()
    return actual


def step5_generate_apps(target_count=5_000_000):
    """步骤 3c: 生成 5M machine_applications"""
    print("\n" + "=" * 80)
    print(f"[步骤 3c] 生成 ~{target_count:,} machine_applications")
    print("=" * 80)

    src_conn = psycopg2.connect(SPIKE_V3_CONN)
    src_cur = src_conn.cursor()

    print("  读取 spike_test_v3 machine_applications 模板...")
    src_cur.execute("""
        SELECT machine_brand, machine_model, sort_order, updated_at
        FROM machine_applications
        ORDER BY id
    """)
    templates = src_cur.fetchall()
    print(f"  读取 {len(templates):,} 条模板")
    src_cur.close()
    src_conn.close()

    dst_conn = psycopg2.connect(PERF_CONN)
    dst_cur = dst_conn.cursor()

    dst_cur.execute("SELECT MIN(id), MAX(id), COUNT(*) FROM products")
    min_id, max_id, total_products = dst_cur.fetchone()
    print(f"  新库 products id 范围: {min_id} ~ {max_id} (共 {total_products:,})")

    apps_per_product = target_count // total_products
    print(f"  每个 product 分配 {apps_per_product} 个 app")

    from io import StringIO
    import csv

    buf = StringIO()
    writer = csv.writer(buf, delimiter='\t', lineterminator='\n')

    total = 0
    t0 = time.time()
    batch_size = 0

    template_count = len(templates)
    for product_id in range(min_id, max_id + 1):
        for k in range(apps_per_product):
            tpl_idx = (product_id * apps_per_product + k) % template_count
            tpl = templates[tpl_idx]
            row = (product_id, tpl[0], tpl[1], tpl[2], tpl[3])
            writer.writerow([_to_pg_text(v) for v in row])
            total += 1
            batch_size += 1

            if batch_size >= 100_000:
                buf.seek(0)
                dst_cur.copy_from(buf, "machine_applications",
                                  columns=("product_id", "machine_brand", "machine_model",
                                           "sort_order", "updated_at"))
                buf = StringIO()
                writer = csv.writer(buf, delimiter='\t', lineterminator='\n')
                batch_size = 0
                elapsed = time.time() - t0
                rate = total / elapsed if elapsed > 0 else 0
                print(f"    已写入 {total:,} 行 ({elapsed:.1f}s, {rate:.0f} 行/s)")

    if batch_size > 0:
        buf.seek(0)
        dst_cur.copy_from(buf, "machine_applications",
                          columns=("product_id", "machine_brand", "machine_model",
                                   "sort_order", "updated_at"))

    dst_conn.commit()
    elapsed = time.time() - t0
    print(f"  完成: {total:,} 行, 耗时 {elapsed:.1f}s")

    dst_cur.execute("SELECT COUNT(*) FROM machine_applications")
    actual = dst_cur.fetchone()[0]
    print(f"  验证: machine_applications 表共 {actual:,} 行")

    dst_cur.close()
    dst_conn.close()
    return actual


def step6_seed_xref_oem_brand():
    """步骤 3d: 复制 xref_oem_brand (5 行)"""
    print("\n" + "=" * 80)
    print("[步骤 3d] 复制 xref_oem_brand (5 行)")
    print("=" * 80)

    src_conn = psycopg2.connect(SPIKE_V3_CONN)
    src_cur = src_conn.cursor()
    src_cur.execute("SELECT brand, sort_order, deleted_at FROM xref_oem_brand")
    rows = src_cur.fetchall()
    src_cur.close()
    src_conn.close()

    dst_conn = psycopg2.connect(PERF_CONN)
    dst_cur = dst_conn.cursor()
    for r in rows:
        dst_cur.execute(
            "INSERT INTO xref_oem_brand (brand, sort_order, deleted_at) VALUES (%s, %s, %s)",
            (r[0], r[1], r[2])
        )
    dst_conn.commit()
    dst_cur.execute("SELECT COUNT(*) FROM xref_oem_brand")
    actual = dst_cur.fetchone()[0]
    print(f"  验证: xref_oem_brand 表共 {actual} 行")
    dst_cur.close()
    dst_conn.close()


def step7_analyze():
    """步骤 4: ANALYZE 所有表"""
    print("\n" + "=" * 80)
    print("[步骤 4] ANALYZE 所有表 (更新统计信息)")
    print("=" * 80)

    conn = psycopg2.connect(PERF_CONN)
    conn.autocommit = True
    cur = conn.cursor()

    for table in ["products", "cross_references", "machine_applications", "xref_oem_brand"]:
        t0 = time.time()
        cur.execute(f"ANALYZE {table}")
        elapsed = time.time() - t0
        print(f"  ANALYZE {table}: {elapsed:.1f}s")

    cur.close()
    conn.close()


def main():
    print("=" * 80)
    print(f"V24-F96 (v28-3) 1M 数据生成 - 时间: {datetime.now().isoformat()}")
    print("=" * 80)

    t_total = time.time()

    step1_create_database()
    step2_dump_and_apply_schema()
    p_count = step3_generate_products(1_000_000)
    x_count = step4_generate_xrefs(5_000_000)
    a_count = step5_generate_apps(5_000_000)
    step6_seed_xref_oem_brand()
    step7_analyze()

    print("\n" + "=" * 80)
    print(f"V24-F96 (v28-3) 数据生成完成 - 总耗时 {time.time() - t_total:.1f}s")
    print("=" * 80)
    print(f"  products: {p_count:,}")
    print(f"  cross_references: {x_count:,}")
    print(f"  machine_applications: {a_count:,}")
    print(f"\n下一步: 运行压测脚本 _perf_v28_3_1m_verify.py")


if __name__ == "__main__":
    main()
