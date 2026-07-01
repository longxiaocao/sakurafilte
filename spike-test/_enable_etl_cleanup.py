"""Day 7.9 启用 etl_log.retention_enabled + 验证 12 行不会被 90 天 cutoff 误删"""
import psycopg2, datetime
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()

# 启用清理
cur.execute("UPDATE system_settings SET value='true' WHERE key='etl_log.retention_enabled'")
cur.execute("SELECT value FROM system_settings WHERE key='etl_log.retention_enabled'")
enabled = cur.fetchone()[0]
conn.commit()
print(f"启用清理: etl_log.retention_enabled = {enabled}")

# 验证不会误删
cur.execute("SELECT count(*) FROM etl_progress_log")
total = cur.fetchone()[0]
print(f"etl_progress_log 现有 {total} 行")

# 计算 cutoff,看有几行会落在 cutoff 之前
retention_days = 90
cutoff = datetime.datetime.utcnow() - datetime.timedelta(days=retention_days)
cur.execute("SELECT count(*) FROM etl_progress_log WHERE finished_at < %s", (cutoff,))
to_delete = cur.fetchone()[0]

cur.execute("SELECT min(finished_at), max(finished_at) FROM etl_progress_log")
mn, mx = cur.fetchone()
print(f"cutoff = {cutoff}")
print(f"  最早 finished_at: {mn}")
print(f"  最新 finished_at: {mx}")
print(f"  会删除 (cutoff 之前): {to_delete} 行 (期望 0)")

assert to_delete == 0, f"误删风险!有 {to_delete} 行会被 90 天 cutoff 删除"
print("\n✅ 安全启用,无 90 天前数据")

# 看最早一行距今多久 (确认 12 行都是 7 天内)
cur.execute("SELECT id, entity_type, finished_at, extract(day from now() - finished_at)::int AS age_days FROM etl_progress_log ORDER BY id LIMIT 3")
print(f"\n最早 3 行:")
for r in cur.fetchall():
    print(f"  id={r[0]:3} entity={r[1]:10} finished={r[2]} age={r[3]}d")
conn.close()
