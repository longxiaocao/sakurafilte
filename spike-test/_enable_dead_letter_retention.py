"""P0-1: 启用 dead_letter retention + 显示当前配置"""
import psycopg2

conn = psycopg2.connect(host='localhost', port=5432, database='spike_test_v3',
                        user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT key, value, description FROM system_settings WHERE key LIKE 'dead_letter%' ORDER BY key")
print("=== Before ===")
for k, v, d in cur.fetchall():
    print(f"  {k:40s} = {v:6s}  ({d[:60]})")

# 1) 启用 retention (默认 7 天)
cur.execute("UPDATE system_settings SET value='true', updated_at=NOW() WHERE key='dead_letter.retention_enabled' AND value<>'true'")
print(f"\n[1] dead_letter.retention_enabled: 启用更新影响 {cur.rowcount} 行")

# 2) 保留天数 = 3 (默认 7, 死信现在 1.8M 积压, 收紧到 3 天快速瘦身)
cur.execute("UPDATE system_settings SET value='3', updated_at=NOW() WHERE key='dead_letter.retention_days' AND value<>'3'")
print(f"[2] dead_letter.retention_days=3: 更新影响 {cur.rowcount} 行")

# 3) 立即运行一次, 不等 24h 调度
cur.execute("SELECT value FROM system_settings WHERE key='dead_letter.retention_enabled'")
enabled = cur.fetchone()[0]
print(f"\n当前 retention_enabled = {enabled}")

# 4) 看当前死信规模
cur.execute("""
    SELECT
        status,
        COUNT(*) AS cnt,
        MIN(moved_at) AS oldest,
        MAX(moved_at) AS newest
    FROM search_index_dead_letter
    GROUP BY status
""")
print("\n=== 死信分布 ===")
total = 0
for st, cnt, oldest, newest in cur.fetchall():
    total += cnt
    print(f"  status={st:12s}  count={cnt:>10,}  oldest={oldest}  newest={newest}")
print(f"  TOTAL: {total:,} 条")

conn.commit()
cur.close(); conn.close()
print("\n[OK] 配置已更新, 重启后端或等 24h 后自动清理")
