"""
字典数据 seed 脚本 — 从 products/machine_applications 提取 distinct 值填充 7 个字典表
用法: python _seed_dicts.py
安全: ON CONFLICT DO NOTHING (不覆盖已有数据), 事务保护 (失败自动 ROLLBACK)
"""
import psycopg2
import sys

CONN = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

# 7 个字典的 seed 映射 (字典表, 字典主值列, 源表, 源列)
SEEDS = [
    ('dict_product_name1', 'product_name_1', 'products',              'product_name_1'),
    ('dict_product_name2', 'product_name_2', 'products',              'product_name_2'),
    ('dict_type',          'type',          'products',              'type'),
    ('dict_oem_no3',       'oem_no_3',      'cross_references',      'oem_no_3'),
    ('dict_media',         'media_name',    'products',              'media'),
    ('dict_machine',       'machine_brand', 'machine_applications',  'machine_brand'),
    ('dict_engine',        'engine_brand',  'machine_applications',  'engine_brand'),
]

def main():
    conn = psycopg2.connect(**CONN)
    cur = conn.cursor()

    print("===== 字典数据 seed =====")
    total_inserted = 0

    for dict_table, dict_col, src_table, src_col in SEEDS:
        # 预检查: 字典表已有大量数据则跳过 (避免 527 万行 NOT EXISTS 慢查询)
        cur.execute(f"SELECT COUNT(*) FROM {dict_table}")
        existing = cur.fetchone()[0]
        if existing > 10000:
            print(f"  {dict_table}: 已有 {existing} 行, 跳过")
            continue

        # 统计 distinct 值
        cur.execute(f"SELECT COUNT(DISTINCT {src_col}) FROM {src_table} WHERE {src_col} IS NOT NULL AND {src_col} != ''")
        distinct_count = cur.fetchone()[0]

        # INSERT DISTINCT...WHERE NOT EXISTS (兼容复合 UNIQUE 索引)
        sql = f"""
            INSERT INTO {dict_table} ({dict_col}, sort_order, created_at)
            SELECT DISTINCT src.{src_col}, 0, NOW()
            FROM {src_table} src
            WHERE src.{src_col} IS NOT NULL AND src.{src_col} != ''
              AND NOT EXISTS (
                SELECT 1 FROM {dict_table} d WHERE d.{dict_col} = src.{src_col}
              )
        """
        try:
            cur.execute(sql)
            inserted = cur.rowcount
            total_inserted += inserted
            print(f"  {dict_table}: distinct={distinct_count}, inserted={inserted}")
        except Exception as e:
            print(f"  [ERROR] {dict_table}: {e}")
            conn.rollback()
            cur.close()
            conn.close()
            sys.exit(1)

    conn.commit()
    print(f"\n总计插入: {total_inserted} 行")

    # 验证最终行数
    print("\n--- 验证 ---")
    for dict_table, dict_col, _, _ in SEEDS:
        cur.execute(f"SELECT COUNT(*) FROM {dict_table}")
        count = cur.fetchone()[0]
        print(f"  {dict_table}: {count} rows")

    cur.close()
    conn.close()
    print("\n[DONE] 字典 seed 完成")

if __name__ == '__main__':
    main()
