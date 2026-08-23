"""为真实存在的产品 seed 多条 history, 验证 cursor 分页"""
import psycopg2
from datetime import datetime, timedelta, timezone
import random

conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 找一个实际存在的产品 (id=111559 是 DAY91-TEST-001, 刚刚清空)
cur.execute("SELECT max(id) FROM products")
max_id = cur.fetchone()[0]
pid = max_id  # 用最新产品
print(f"为 product_id={pid} 插 20 条历史用于分页测试")

# 先清掉旧 cursor-test 数据
cur.execute("DELETE FROM product_history WHERE product_id = %s AND changed_by = 'cursor-test'", (pid,))
print(f"  已清掉 {cur.rowcount} 条旧测试数据")

base = datetime.now(timezone.utc) - timedelta(days=10)
change_types = ['update', 'create', 'restore', 'discontinue']

for i in range(20):
    cur.execute("""
        INSERT INTO product_history
            (product_id, change_type, changed_by, changed_at, changed_fields)
        VALUES (%s, %s, %s, %s, %s)
    """, (
        pid,
        random.choice(change_types),
        'cursor-test',
        base + timedelta(minutes=i * 5),
        '{"_test":"day94-cursor-paging","i":%d}' % i
    ))

conn.commit()
print(f"  插 20 条, 验证 product_id={pid} 总历史数:")

cur.execute("SELECT count(*) FROM product_history WHERE product_id = %s", (pid,))
total = cur.fetchone()[0]
print(f"  total = {total}")
print(f"  → 测试时用 target_pid = {pid}")
conn.close()
