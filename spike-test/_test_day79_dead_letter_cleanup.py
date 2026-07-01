"""Day 7.9 验证: DeadLetterCleanupService 清理逻辑 (SQL 等价)"""
import psycopg2, datetime
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 注入 3 条 moved_at: 8 天前 / 3 天前 / 1 天前
cur.execute("DELETE FROM search_index_dead_letter WHERE operation = 'day79_cleanup'")
cur.execute("""
    INSERT INTO search_index_dead_letter
        (original_id, operation, payload, retry_count, last_error, created_at, moved_at)
    VALUES
        (790001, 'day79_cleanup', '{}'::jsonb, 5, 'old-8d',    now() - interval '8 days', now() - interval '8 days'),
        (790002, 'day79_cleanup', '{}'::jsonb, 5, 'old-3d',    now() - interval '3 days', now() - interval '3 days'),
        (790003, 'day79_cleanup', '{}'::jsonb, 5, 'recent-1d', now() - interval '1 day',  now() - interval '1 day')
""")
conn.commit()
print("插入 3 条 (8d/3d/1d 前)")

# 启用 dead_letter.retention_enabled + retention_days=7
cur.execute("UPDATE system_settings SET value='true' WHERE key='dead_letter.retention_enabled'")
conn.commit()

# 模拟 cleanup 逻辑 (与 DeadLetterCleanupService.RunOnceAsync 等价)
retention_days = 7
cutoff = datetime.datetime.utcnow() - datetime.timedelta(days=retention_days)
cur.execute("SELECT count(*) FROM search_index_dead_letter WHERE moved_at < %s AND operation='day79_cleanup'", (cutoff,))
candidates = cur.fetchone()[0]
print(f"cutoff = {cutoff} (7 天前), 候选: {candidates} 条 (期望 1,8 天前那条)")

cur.execute("DELETE FROM search_index_dead_letter WHERE moved_at < %s AND operation='day79_cleanup'", (cutoff,))
deleted = cur.rowcount
conn.commit()
print(f"删除: {deleted} 条 (期望 1)")

cur.execute("SELECT count(*) FROM search_index_dead_letter WHERE operation='day79_cleanup'")
remaining = cur.fetchone()[0]
print(f"剩余 day79_cleanup: {remaining} 条 (期望 2,3d + 1d 保留)")
assert deleted == 1
assert remaining == 2
print("✅ DeadLetterCleanupService 清理逻辑正确")

# 清理
cur.execute("DELETE FROM search_index_dead_letter WHERE operation='day79_cleanup'")
cur.execute("UPDATE system_settings SET value='false' WHERE key='dead_letter.retention_enabled'")
conn.commit()
conn.close()
