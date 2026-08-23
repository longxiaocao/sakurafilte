"""最小化测试: Npgsql 8 + EnableLegacyTimestampBehavior 下 DateTime 序列化真实行为"""
import psycopg2
from datetime import datetime, timezone, timedelta

PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
conn = psycopg2.connect(**PG); conn.autocommit = True
cur = conn.cursor()

# 准备一张测试表
cur.execute("DROP TABLE IF EXISTS _tstz_test")
cur.execute("CREATE TABLE _tstz_test (id serial primary key, t timestamptz)")

# 三种 DateTime 写入测试
local_tz = timezone(timedelta(hours=8))  # CST

# Case 1: DateTime {Kind=Utc} 21:22:17 UTC
utc_dt = datetime(2026, 6, 30, 21, 22, 17, 390055, tzinfo=timezone.utc)
print(f'Case 1 Kind=Utc value={utc_dt} 写入...')
cur.execute("INSERT INTO _tstz_test (t) VALUES (%s)", (utc_dt,))

# Case 2: DateTime {Kind=Local, CST} 05:22:17 CST
local_dt = datetime(2026, 7, 1, 5, 22, 17, 390055, tzinfo=local_tz)
print(f'Case 2 Kind=Local value={local_dt} 写入...')
cur.execute("INSERT INTO _tstz_test (t) VALUES (%s)", (local_dt,))

# Case 3: DateTime {Kind=Unspecified} 21:22:17 无时区
unspec_dt = datetime(2026, 6, 30, 21, 22, 17, 390055)
print(f'Case 3 Kind=Unspecified value={unspec_dt} 写入...')
try:
    cur.execute("INSERT INTO _tstz_test (t) VALUES (%s)", (unspec_dt,))
except Exception as e:
    print(f'  写入失败: {e}')

# Case 4: DateTime {Kind=Utc} 13:22:17 UTC (模拟 DateTime.UtcNow 在 UTC+8 机器上的输出)
utc_dt2 = datetime(2026, 6, 30, 13, 22, 17, 390055, tzinfo=timezone.utc)
print(f'Case 4 Kind=Utc value={utc_dt2} 写入...')
cur.execute("INSERT INTO _tstz_test (t) VALUES (%s)", (utc_dt2,))

# 查 DB 中存的值
cur.execute("SELECT id, t, t AT TIME ZONE 'UTC' AS utc, EXTRACT(EPOCH FROM t) AS epoch FROM _tstz_test ORDER BY id")
print('\n=== DB 中实际存储值 ===')
for row in cur.fetchall():
    print(f'  id={row[0]} stored_local={row[1]} stored_utc={row[2]} epoch={row[3]}')

# 清理
cur.execute("DROP TABLE _tstz_test")
print('\n清理完成')
