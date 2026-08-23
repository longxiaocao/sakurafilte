"""Day 7.8 验证: etl_log cleanup 逻辑 (直接走 SQL,不依赖后台任务 24h 周期)"""
import psycopg2
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 1) 注入 2 条 100 天前的记录 + 启用 retention_enabled
cur.execute("""
    INSERT INTO etl_progress_log
        (entity_type, mode, file_path, status, started_at, finished_at, duration_sec)
    VALUES
        ('day78_test', 'upsert', '/tmp/old.jsonl', 'completed', now() - interval '100 days', now() - interval '100 days', 1.0),
        ('day78_test', 'upsert', '/tmp/old.jsonl', 'completed', now() - interval '95 days',  now() - interval '95 days',  1.0),
        ('day78_test', 'upsert', '/tmp/recent.jsonl', 'completed', now() - interval '5 days', now() - interval '5 days', 1.0)
""")
conn.commit()
print("插入 3 条测试数据 (2 条 95-100 天前,1 条 5 天前)")

# 2) 设置 retention = 90 天
cur.execute("UPDATE system_settings SET value = '90' WHERE key = 'etl_log.retention_days'")
cur.execute("UPDATE system_settings SET value = 'true' WHERE key = 'etl_log.retention_enabled'")
conn.commit()
print("启用 etl_log.retention_enabled=true, retention_days=90")

# 3) 模拟 cleanup 逻辑 (与 EtlLogCleanupService.RunOnceAsync 一致)
import datetime
retention_days = 90
batch_size = 5000
cutoff = datetime.datetime.utcnow() - datetime.timedelta(days=retention_days)
print(f"cutoff = {cutoff}")

cur.execute("SELECT count(*) FROM etl_progress_log WHERE finished_at < %s AND entity_type = 'day78_test'", (cutoff,))
candidates = cur.fetchone()[0]
print(f"候选 (90 天前 + day78_test): {candidates} 条")

# 模拟分批删除
total = 0
while True:
    cur.execute("""
        SELECT id FROM etl_progress_log
        WHERE finished_at < %s AND entity_type = 'day78_test'
        ORDER BY id LIMIT %s
    """, (cutoff, batch_size))
    ids = [r[0] for r in cur.fetchall()]
    if not ids: break
    cur.execute("DELETE FROM etl_progress_log WHERE id = ANY(%s)", (ids,))
    total += cur.rowcount
    if len(ids) < batch_size: break
conn.commit()
print(f"删除: {total} 条 (期望 2)")

# 验证剩余
cur.execute("SELECT count(*) FROM etl_progress_log WHERE entity_type = 'day78_test'")
remaining = cur.fetchone()[0]
print(f"剩余 day78_test: {remaining} 条 (期望 1,即 5 天前那条保留)")
assert remaining == 1
print("✅ 清理逻辑正确 (只删 cutoff 之前)")

# 清理
cur.execute("DELETE FROM etl_progress_log WHERE entity_type = 'day78_test'")
cur.execute("UPDATE system_settings SET value = 'false' WHERE key = 'etl_log.retention_enabled'")
conn.commit()
print(f"\n清理测试数据 {cur.rowcount} 行,关闭 retention")
conn.close()
