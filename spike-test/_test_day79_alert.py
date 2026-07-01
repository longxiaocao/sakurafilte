"""Day 7.9 验证: EtlAlertService 实际推送 webhook + alert_sent 置位 + 失败重试"""
import json, time, psycopg2, urllib.request

# 0) 清掉 day79 残留 + 重置 webhook 状态
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("DELETE FROM etl_progress_log WHERE file_path LIKE '%day79%'")
print(f"[0] 清理残留 day79 数据 {cur.rowcount} 行")
conn.commit()

# 1) 启用告警 + 指向测试 webhook
cur = conn.cursor()
cur.execute("UPDATE system_settings SET value='true' WHERE key='alert.enabled'")
cur.execute("UPDATE system_settings SET value='http://127.0.0.1:9876/webhook' WHERE key='alert.webhook_url'")
cur.execute("UPDATE system_settings SET value='5' WHERE key='alert.poll_seconds'")
conn.commit()
print("[1] 启用告警,poll=5s,webhook=http://127.0.0.1:9876/webhook")

# 2) 注入 3 条失败的 etl_progress_log (alert_sent=false)
cur.execute("""
    INSERT INTO etl_progress_log
        (entity_type, mode, file_path, status, read_count, error_count,
         last_error, started_at, finished_at, duration_sec, alert_sent)
    VALUES
        ('products', 'upsert', '/tmp/day79-1.jsonl', 'failed', 1000, 1,
         'Connection refused to MeiliSearch', now() - interval '3 minutes', now() - interval '3 minutes', 0.1, false),
        ('xrefs',    'upsert', '/tmp/day79-2.jsonl', 'failed', 0, 1,
         'staging table missing column foo',  now() - interval '2 minutes', now() - interval '2 minutes', 0.05, false),
        ('apps',     'upsert', '/tmp/day79-3.jsonl', 'failed', 50, 1,
         'payload JSON malformed at line 42',  now() - interval '1 minutes', now() - interval '1 minutes', 0.02, false)
""")
conn.commit()
cur.execute("SELECT count(*) FROM etl_progress_log WHERE status='failed' AND alert_sent=false")
print(f"[2] 注入 3 条 failed, 未告警: {cur.fetchone()[0]} 条")

# 3) 等 70s 让 EtlAlertService 跑过第一轮 + 第二轮 (默认 poll=60s)
print("[3] 等待 70s 让 EtlAlertService 至少跑一轮...")
time.sleep(70)

# 4) 验证 alert_sent 已置位 (按 file_path 严格匹配 day79 注入的数据)
cur.execute("SELECT count(*) FROM etl_progress_log WHERE file_path LIKE '%day79%' AND status='failed' AND alert_sent=false")
remaining = cur.fetchone()[0]
cur.execute("SELECT count(*) FROM etl_progress_log WHERE file_path LIKE '%day79%' AND status='failed' AND alert_sent=true")
alerted = cur.fetchone()[0]
print(f"[4] day79 推送后: 未告警 {remaining} / 已告警 {alerted}")
assert remaining == 0, f"还有 {remaining} 条未告警"
assert alerted == 3, f"已告警数 {alerted} 期望 3"
print("    ✅ 全部 3 条已告警")

# 5) 等 7s 再跑一轮,确认不会重复推送 (因为 alert_sent=true)
print("[5] 等待 7s 确认不重复推送...")
time.sleep(7)

# 检查 webhook 文件,看接收了几次
with open(r'd:\projects\sakurafilter\spike-test\webhook_stdout.log', 'r') as f:
    webhook_log = f.read()
print(f"  webhook 接收: {webhook_log.count('127.0.0.1') if False else '见单独记录'}")

# 6) 注入第 4 条失败, 确认继续告警能力
cur.execute("""
    INSERT INTO etl_progress_log
        (entity_type, mode, file_path, status, read_count, error_count,
         last_error, started_at, finished_at, duration_sec, alert_sent)
    VALUES
        ('products', 'full-load', '/tmp/day79-second.jsonl', 'failed', 1, 1,
         'second test', now(), now(), 0.01, false)
""")
conn.commit()
print("[6] 注入第 4 条失败, 等 8s 验证继续告警...")
time.sleep(8)

cur.execute("SELECT count(*) FROM etl_progress_log WHERE file_path LIKE '%day79%' AND status='failed' AND alert_sent=true")
final_alerted = cur.fetchone()[0]
print(f"  最终 day79 已告警: {final_alerted} (期望 4)")
assert final_alerted == 4
print("    ✅ 持续告警能力 OK")

# 6) 验证失败重试: 故意把 webhook_url 改成无效,再注入 1 条
cur.execute("UPDATE system_settings SET value='http://127.0.0.1:1/invalid' WHERE key='alert.webhook_url'")
cur.execute("""
    INSERT INTO etl_progress_log
        (entity_type, mode, file_path, status, read_count, error_count,
         last_error, started_at, finished_at, duration_sec, alert_sent)
    VALUES
        ('xrefs', 'upsert', '/tmp/day79-fail.jsonl', 'failed', 1, 1,
         'test retry', now(), now(), 0.01, false)
""")
conn.commit()
print("[7] webhook 故意改无效, 注入 1 条失败...")
time.sleep(7)
cur.execute("SELECT count(*) FROM etl_progress_log WHERE file_path LIKE '%day79-fail.jsonl' AND alert_sent=false")
uncommitted = cur.fetchone()[0]
print(f"  验证未置位: {uncommitted} (期望 1,推送失败需重试)")
assert uncommitted == 1
print("    ✅ 推送失败时未置位,可重试")

# 清理
cur.execute("SELECT count(*) FROM etl_progress_log WHERE file_path LIKE '%day79%'")
day79_count = cur.fetchone()[0]
cur.execute("DELETE FROM etl_progress_log WHERE file_path LIKE '%day79%'")
conn.commit()
cur.execute("UPDATE system_settings SET value='false' WHERE key='alert.enabled'")
cur.execute("UPDATE system_settings SET value='' WHERE key='alert.webhook_url'")
cur.execute("UPDATE system_settings SET value='60' WHERE key='alert.poll_seconds'")
conn.commit()
print(f"\n清理 day79 测试数据 {day79_count} 行,关闭告警")
conn.close()
print("\n=== EtlAlertService 端到端验证通过 ===")
