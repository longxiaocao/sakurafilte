"""Day 7.9 验证: webhook 实际接收 payload 内容"""
import json, time, psycopg2, http.server, threading

received = []
done = threading.Event()

class Handler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get('Content-Length', 0))
        body = self.rfile.read(length).decode('utf-8')
        received.append(json.loads(body))
        resp = b'{"errcode":0}'
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(resp)))
        self.send_header('Connection', 'close')  # 避免 keepalive 兼容问题
        self.end_headers()
        self.wfile.write(resp)
        if len(received) >= 1:
            done.set()
    def log_message(self, *a, **kw): pass

server = http.server.HTTPServer(('127.0.0.1', 9877), Handler)
t = threading.Thread(target=server.serve_forever, daemon=True)
t.start()
print("Test webhook server on :9877")

# 启告警指向 :9877,塞 1 条失败
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("UPDATE system_settings SET value='true' WHERE key='alert.enabled'")
cur.execute("UPDATE system_settings SET value='http://127.0.0.1:9877/wh' WHERE key='alert.webhook_url'")
cur.execute("DELETE FROM etl_progress_log WHERE file_path LIKE '%day79-payload%'")
cur.execute("""
    INSERT INTO etl_progress_log
        (entity_type, mode, file_path, status, read_count, inserted_count, error_count,
         last_error, started_at, finished_at, duration_sec, alert_sent)
    VALUES
        ('products', 'upsert', '/tmp/day79-payload.jsonl', 'failed', 2132, 1949, 1,
         'MeiliSearch returned 500 at /indexes/products/documents: schema validation failed',
         now() - interval '30 seconds', now(), 0.25, false)
""")
conn.commit()
print("塞 1 条 failed,等推送...")

# 等待 1 个 push (poll 默认 60s,需要重启 API 才生效 poll=5;改成触发 index replay)
# 简单做法: 直接 sleep 65s 等待下一轮 (poll=60)
done.wait(timeout=75)
print(f"\n收到 {len(received)} 条 webhook:")
for p in received:
    print(json.dumps(p, indent=2, ensure_ascii=False)[:1500])
    print('---')
    # 验证关键字段
    assert p['@event'] == 'etl.failed'
    assert p['etl']['entity_type'] == 'products'
    assert p['etl']['mode'] == 'upsert'
    assert 'MeiliSearch' in p['etl']['last_error']
    assert 'text' in p
    print("✅ payload 结构正确")

# 清理
cur.execute("DELETE FROM etl_progress_log WHERE file_path LIKE '%day79-payload%'")
cur.execute("UPDATE system_settings SET value='false' WHERE key='alert.enabled'")
cur.execute("UPDATE system_settings SET value='' WHERE key='alert.webhook_url'")
conn.commit()
print("\n清理完成")
server.shutdown()
conn.close()
