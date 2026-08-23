"""简易 webhook 接收器,记录所有 POST 请求"""
import http.server
import json
import threading
import time

received = []

class Handler(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get('Content-Length', 0))
        body = self.rfile.read(length).decode('utf-8')
        received.append({'time': time.time(), 'body': body})
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(b'{"errcode":0,"errmsg":"ok"}')
    def log_message(self, *a, **kw): pass

server = http.server.HTTPServer(('127.0.0.1', 9876), Handler)
t = threading.Thread(target=server.serve_forever, daemon=True)
t.start()
print("Webhook server listening on http://127.0.0.1:9876")

# 主线程持续运行,被外部 Kill
import signal
def stop(*_): server.shutdown(); print("Stopped")
signal.signal(signal.SIGTERM, stop)
while True: time.sleep(60)
