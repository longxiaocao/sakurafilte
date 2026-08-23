"""V24-F96 (v28-3) 1M 数据扩容 - apps-only 续传 (step5-step7)

前置: _gen_v28_3_1m_data.py 完成 step1-3 (950K products), _gen_v28_3_continue.py 完成 step4 (4.75M xrefs)
本脚本: 生成 4.75M apps + seed xref_oem_brand + ANALYZE

修复点:
  - machine_applications 唯一约束 uq_apps_product_brand_model: 用去重模板 + random.sample
"""
import os
import time
import psycopg2
from io import StringIO
import csv
from datetime import datetime

SPIKE_V3_CONN = "host=localhost port=5432 dbname=spike_test_v3 user=postgres password=784533"
PERF_CONN = "host=localhost port=5432 dbname=sakurafilter_perf_tests user=postgres password=784533"


def _to_pg_text(v):
    if v is None:
        return r"\N"
    if v is True:
        return "t"
    if v is False:
        return "f"
    return v


def step5_generate_apps(target_count=5_000_000):
    """生成 ~5M machine_applications (列: product_id, machine_brand, machine_model, created_at)
    修复: 唯一约束 uq_apps_product_brand_model (product_id, machine_brand, machine_model)
          同一 product_id 下不能用重复 (brand, model), 改用去重模板 + random.sample
    """
    import random
    random.seed(42)

    print("\n" + "=" * 80)
    print(f"[步骤 3c] 生成 ~{target_count:,} machine_applications")
    print("=" * 80)

    src_conn = psycopg2.connect(SPIKE_V3_CONN)
    src_cur = src_conn.cursor()
    print("  读取 spike_test_v3 machine_applications 模板...")
    src_cur.execute("""
        SELECT machine_brand, machine_model, created_at
        FROM machine_applications
        ORDER BY id
    """)
    templates = src_cur.fetchall()
    print(f"  读取 {len(templates):,} 条模板")
    src_cur.close()
    src_conn.close()

    # 去重: 按 (machine_brand, machine_model) 去重, 保留首次出现的 created_at
    seen = set()
    unique_templates = []
    for tpl in templates:
        key = (tpl[0], tpl[1])
        if key not in seen:
            seen.add(key)
            unique_templates.append(tpl)
    print(f"  去重后 {len(unique_templates):,} 个唯一 (brand, model) 组合")

    dst_conn = psycopg2.connect(PERF_CONN)
    dst_cur = dst_conn.cursor()

    dst_cur.execute("SELECT MIN(id), MAX(id), COUNT(*) FROM products")
    min_id, max_id, total_products = dst_cur.fetchone()
    print(f"  新库 products id 范围: {min_id} ~ {max_id} (共 {total_products:,})")

    apps_per_product = target_count // total_products
    print(f"  每个 product 分配 {apps_per_product} 个 app (random.sample 保证不重复)")

    buf = StringIO()
    writer = csv.writer(buf, delimiter='\t', lineterminator='\n')

    total = 0
    t0 = time.time()
    batch_size = 0

    for product_id in range(min_id, max_id + 1):
        # 随机选 apps_per_product 个不重复的 (brand, model, created_at) 组合
        selected = random.sample(unique_templates, min(apps_per_product, len(unique_templates)))
        for tpl in selected:
            row = (product_id, tpl[0], tpl[1], tpl[2])
            writer.writerow([_to_pg_text(v) for v in row])
            total += 1
            batch_size += 1

            if batch_size >= 100_000:
                buf.seek(0)
                dst_cur.copy_from(buf, "machine_applications",
                                  columns=("product_id", "machine_brand", "machine_model", "created_at"))
                buf = StringIO()
                writer = csv.writer(buf, delimiter='\t', lineterminator='\n')
                batch_size = 0
                elapsed = time.time() - t0
                rate = total / elapsed if elapsed > 0 else 0
                print(f"    已写入 {total:,} 行 ({elapsed:.1f}s, {rate:.0f} 行/s)")

    if batch_size > 0:
        buf.seek(0)
        dst_cur.copy_from(buf, "machine_applications",
                          columns=("product_id", "machine_brand", "machine_model", "created_at"))

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
    """复制 xref_oem_brand (5 行)"""
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
    # 先检查是否已有数据
    dst_cur.execute("SELECT COUNT(*) FROM xref_oem_brand")
    existing = dst_cur.fetchone()[0]
    if existing > 0:
        print(f"  xref_oem_brand 已有 {existing} 行, 跳过")
        dst_cur.close()
        dst_conn.close()
        return existing
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
    return actual


def step7_analyze():
    """ANALYZE 所有表"""
    print("\n" + "=" * 80)
    print("[步骤 4] ANALYZE 所有表")
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
    print(f"V24-F96 (v28-3) 1M 数据生成 apps-only 续传 - 时间: {datetime.now().isoformat()}")
    print("=" * 80)

    t_total = time.time()

    a_count = step5_generate_apps(5_000_000)
    step6_seed_xref_oem_brand()
    step7_analyze()

    # 最终验证
    conn = psycopg2.connect(PERF_CONN)
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM products")
    p_count = cur.fetchone()[0]
    cur.execute("SELECT COUNT(*) FROM cross_references")
    x_count = cur.fetchone()[0]
    cur.close()
    conn.close()

    print("\n" + "=" * 80)
    print(f"V24-F96 (v28-3) 数据生成完成 - 总耗时 {time.time() - t_total:.1f}s")
    print("=" * 80)
    print(f"  products: {p_count:,}")
    print(f"  cross_references: {x_count:,}")
    print(f"  machine_applications: {a_count:,}")
    print(f"\n下一步: 运行压测脚本 _perf_v28_3_1m_verify.py")


if __name__ == "__main__":
    main()
