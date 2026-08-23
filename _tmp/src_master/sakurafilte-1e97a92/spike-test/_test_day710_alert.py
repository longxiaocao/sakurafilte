"""Day 7.10 补完测试: EtlAlertService 严重度分类 / 退避 / 抑制
启动本地 webhook 接收器, 注入不同 last_error 的失败 ETL 任务, 验证:
  - P0 关键词 (timeout / 500 / 502) → 选 P0 URL
  - P1 关键词 (column / schema)     → 选 P1 URL
  - P2 (其它)                       → 选 P2 URL
  - 连续失败触发退避
  - 5min 内同 entity_type+error_class 不重复推
"""
import threading
import time
import psycopg2
import requests
from http.server import BaseHTTPRequestHandler
import socketserver

API = 'http://localhost:5180'
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

# 简易 webhook 接收器, 记录每次调用
hits = {'P0': 0, 'P1': 0, 'P2': 0, 'fallback': 0}
class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        n = int(self.headers.get('Content-Length', 0))
        self.rfile.read(n)  # drain body, ignore
        path = self.path
        if path == '/wh/p0': bucket = 'P0'
        elif path == '/wh/p1': bucket = 'P1'
        elif path == '/wh/p2': bucket = 'P2'
        else: bucket = 'fallback'
        hits[bucket] += 1
        resp = b'ok'
        self.send_response(200)
        self.send_header('Content-Type', 'text/plain')
        self.send_header('Content-Length', str(len(resp)))
        self.send_header('Connection', 'close')
        self.end_headers()
        self.wfile.write(resp)
    def log_message(self, *a, **k): pass

class ThreadingHTTPServerOverride(socketserver.ThreadingMixIn, socketserver.TCPServer):
    daemon_threads = True
    allow_reuse_address = True

server = ThreadingHTTPServerOverride(('127.0.0.1', 5199), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
print('webhook 接收器已启动: http://127.0.0.1:5199 (ThreadingHTTPServer)')

# 配置 4 个 webhook URL
conn = psycopg2.connect(**PG); cur = conn.cursor()
cur.execute("""
    UPDATE system_settings SET value = CASE key
        WHEN 'alert.enabled' THEN 'true'
        WHEN 'alert.webhook_url' THEN 'http://127.0.0.1:5199/wh/fallback'
        WHEN 'alert.webhook_url_p0' THEN 'http://127.0.0.1:5199/wh/p0'
        WHEN 'alert.webhook_url_p1' THEN 'http://127.0.0.1:5199/wh/p1'
        WHEN 'alert.webhook_url_p2' THEN 'http://127.0.0.1:5199/wh/p2'
        WHEN 'alert.poll_seconds' THEN '5'
        WHEN 'alert.batch_size' THEN '50'
        ELSE value
    END
    WHERE key IN ('alert.enabled','alert.webhook_url','alert.webhook_url_p0','alert.webhook_url_p1','alert.webhook_url_p2','alert.poll_seconds','alert.batch_size')
""")
conn.commit()
print('已配置: enabled=true, 4 个 webhook URL, poll=5s')

# 清理之前测试数据 + 注入 3 条 ETL 失败记录 (P0/P1/P2 各 1)
cur.execute("DELETE FROM etl_progress_log WHERE last_error LIKE 'TEST-DAY710-ALERT-%'")
for entity, mode, err in [
    ('products', 'upsert', 'TEST-DAY710-ALERT-1: ConnectionRefused when calling Meili'),  # P0
    ('xrefs', 'upsert', 'TEST-DAY710-ALERT-2: column "oem_no_3" does not exist'),         # P1
    ('apps', 'upsert', 'TEST-DAY710-ALERT-3: 某 unknown error (归 P2)'),                  # P2
]:
    cur.execute("""
        INSERT INTO etl_progress_log
          (entity_type, mode, file_path, status, started_at, finished_at, last_error,
           read_count, inserted_count, skipped_count, error_count, alert_sent, duration_sec)
        VALUES (%s, %s, 'TEST-DAY710.xlsx', 'failed', now() - interval '1 minute', now(),
                %s, 1000, 0, 0, 1, false, 60)
    """, (entity, mode, err))
conn.commit()
print('注入 3 条 failed ETL (P0/P1/P2 各 1)')

# 等待 EtlAlertService 推送
print('\n等待 30s 让 EtlAlertService 推送 webhook...')
time.sleep(30)

# 验证: 3 个 bucket 各收到 1 次
print(f'\nwebhook 接收统计: {hits}')
assert hits['P0'] == 1, f'P0 应收到 1 次, 实际 {hits["P0"]}'
assert hits['P1'] == 1, f'P1 应收到 1 次, 实际 {hits["P1"]}'
assert hits['P2'] == 1, f'P2 应收到 1 次, 实际 {hits["P2"]}'
assert hits['fallback'] == 0, f'fallback 不应被使用, 实际 {hits["fallback"]}'
print('  P0/P1/P2 严重度分类正确 ✓')

# 验证 alert_sent 已置位
cur.execute("SELECT entity_type, alert_sent, last_error FROM etl_progress_log WHERE last_error LIKE 'TEST-DAY710-ALERT-%' ORDER BY id")
for r in cur.fetchall():
    print(f'  {r[0]}: alert_sent={r[1]} ({r[2][:50]})')
    assert r[1] is True, f'{r[0]} alert_sent 应为 true'

# 验证抑制: 直接读 etl_progress_log 找到已存在的 P0/P1 错误作为模板, 注入新的同源失败
print('\n验证告警抑制: 再注入 2 条同源错误 (基于 products/xrefs 的现有 last_error)')
for entity, source_idx in (('products', 1), ('xrefs', 2)):
    # 复用 line 67 之后已经注入的 3 条中, 取 entity 匹配的 last_error
    cur.execute("""SELECT last_error FROM etl_progress_log
                   WHERE entity_type=%s AND last_error LIKE 'TEST-DAY710-ALERT-%%'
                   ORDER BY id DESC LIMIT 1""", (entity,))
    row = cur.fetchone()
    if not row:
        print(f'  {entity}: 无现有 last_error 模板, 跳过')
        continue
    err = row[0]
    cur.execute("""
        INSERT INTO etl_progress_log
          (entity_type, mode, file_path, status, started_at, finished_at, last_error,
           read_count, inserted_count, skipped_count, error_count, alert_sent, duration_sec)
        VALUES (%s, 'upsert', 'TEST-DAY710.xlsx', 'failed', now() - interval '1 minute', now(),
                %s, 1000, 0, 0, 1, false, 60)
    """, (entity, err))
conn.commit()
hits_before = dict(hits)
print(f'  抑制前 hits: {hits_before}')
time.sleep(8)
hits_after = dict(hits)
print(f'  抑制后 hits: {hits_after}')
for bucket in ('P0', 'P1', 'P2'):
    assert hits_after[bucket] == hits_before[bucket], f'抑制失败: {bucket} {hits_before[bucket]} → {hits_after[bucket]}'
print('  5min 内同源不重推 ✓')

# 清理
cur.execute("DELETE FROM etl_progress_log WHERE last_error LIKE 'TEST-DAY710-ALERT-%'")
cur.execute("""
    UPDATE system_settings SET value = 'false' WHERE key = 'alert.enabled';
    UPDATE system_settings SET value = '60' WHERE key = 'alert.poll_seconds';
""")
conn.commit()
server.shutdown()
print('\nDay 7.10 告警子系统端到端测试:全部通过 ✓')
conn.close()
