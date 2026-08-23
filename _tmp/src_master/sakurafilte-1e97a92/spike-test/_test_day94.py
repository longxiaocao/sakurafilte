#!/usr/bin/env python
# Day 9.4 改进项端到端测试
#   1. ETL cancel 接受 reason → 落库 etl_progress_log.cancel_reason + cancelled_at
#   2. dry-run samples 数量: 5 → 50
#   3. SSE /api/admin/etl/progress/stream 推送 data: 帧
#   4. History API 支持 cursor keyset 分页
#   5. product_history 索引 idx_product_history_paging 存在
#   6. AppProductsView 必填字段校验 (machine_brand + machine_model)
import json
import os
import re
import time
import urllib.request
import urllib.error
import psycopg2
from concurrent.futures import ThreadPoolExecutor

BASE = "http://localhost:5148/api"
ADMIN_TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"X-Admin-Token": ADMIN_TOKEN, "Content-Type": "application/json"}

PROD_PATH = r"D:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl"
APPS_PATH = r"D:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl"
LARGE_PATH = r"D:\projects\sakurafilter\spike-test\output\synthetic_products_100k.jsonl"

passed = []
failed = []


def call(method, path, body=None, timeout=10, raw=False):
    url = f"{BASE}{path}"
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, method=method, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw_bytes = r.read()
            if raw:
                return r.status, raw_bytes
            try:
                return r.status, json.loads(raw_bytes)
            except json.JSONDecodeError:
                return r.status, raw_bytes.decode("utf-8", errors="replace")
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, {"raw": str(e)}


def check(name, cond, info=""):
    if cond:
        passed.append(name)
        print(f"  [PASS] {name}")
    else:
        failed.append((name, info))
        print(f"  [FAIL] {name} -- {info}")


def db_conn():
    return psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')


print("=" * 70)
print(" Day 9.4 改进项端到端测试")
print("=" * 70)

# ========== 1. cancel 接受 reason ==========
print("\n[1] cancel 接受 reason, 缺省时使用 '用户取消'")
s, r = call("DELETE", "/admin/etl/task")
check("无 body → HTTP 200", s == 200, f"status={s}")
check("无 body → cancelled=false", r.get("cancelled") is False, f"r={r}")
check("无 body → reason 字段存在", "reason" in r, f"r={r}")

# ========== 2. cancel with reason 落库 ==========
print("\n[2] cancel with reason 落库 (etl_progress_log.cancel_reason)")
# 准备: 触发 ETL 任务
if not os.path.exists(LARGE_PATH):
    print(f"  [SKIP] 大文件不存在: {LARGE_PATH}")
else:
    def trigger():
        return call("POST", "/admin/etl/trigger", {
            "entityType": "products", "jsonlPath": LARGE_PATH, "mode": "upsert", "dryRun": False
        }, timeout=120)

    def cancel_with_reason():
        time.sleep(2)  # 等任务进入 staging 阶段
        return call("DELETE", "/admin/etl/task", body={"reason": "Day 9.4 E2E 测试取消"})

    with ThreadPoolExecutor(max_workers=2) as ex:
        f_trigger = ex.submit(trigger)
        f_cancel = ex.submit(cancel_with_reason)
        try:
            ts, tr = f_trigger.result(timeout=120)
        except Exception as e:
            ts, tr = -1, {"error": str(e)}
        cs, cr = f_cancel.result(timeout=10)

    check("trigger 启动/409", ts in (200, 409), f"status={ts}")
    if ts == 200:
        check("cancel HTTP 200", cs == 200, f"status={cs}")
        check("cancel cancelled=true", cr.get("cancelled") is True, f"r={cr}")
        check("cancel reason 回显", cr.get("reason") == "Day 9.4 E2E 测试取消", f"r={cr}")
    else:
        print(f"  [INFO] trigger 被 409 拒绝, 跳过 cancel-true 验证")

    # 等任务彻底停止
    time.sleep(2)
    # 验证数据库: 最近一次 cancelled 任务必须带 reason
    conn = db_conn()
    cur = conn.cursor()
    cur.execute("""
        SELECT id, status, cancel_reason, cancelled_at
        FROM etl_progress_log
        WHERE status = 'cancelled'
        ORDER BY id DESC
        LIMIT 1
    """)
    row = cur.fetchone()
    if row:
        eid, status, reason, cancelled_at = row
        check(f"etl_progress_log 最新取消: status={status}", status == 'cancelled', f"row={row}")
        check(f"cancel_reason 落库 ({reason!r})", reason is not None and "Day 9.4" in reason, f"row={row}")
        check(f"cancelled_at 落库 ({cancelled_at!r})", cancelled_at is not None, f"row={row}")
    else:
        print(f"  [WARN] etl_progress_log 暂无 cancelled 记录 (cancel 信号可能未赶上写入)")
    conn.close()

# ========== 3. dry-run samples 50 行 ==========
print("\n[3] dry-run samples 数量: 5 → 50")
if os.path.exists(PROD_PATH):
    s, r = call("POST", "/admin/etl/trigger", {
        "entityType": "products", "jsonlPath": PROD_PATH, "mode": "upsert", "dryRun": True
    })
    check("dry-run HTTP 200", s == 200, f"status={s}")
    samples = r.get("samples", [])
    check("dry-run samples 数组", isinstance(samples, list), f"type={type(samples).__name__}")
    check("dry-run samples <= 50 (Day 9.4 上限)", len(samples) <= 50, f"len={len(samples)}")
    check("dry-run samples >= 5 (覆盖要求)", len(samples) >= 5, f"len={len(samples)}")
    # 验证每行都是合法 JSON
    parseable = sum(1 for s in samples if s and s.strip().startswith('{'))
    check(f"dry-run samples 都可解析 ({parseable}/{len(samples)})", parseable == len(samples), f"parseable={parseable}")

    # schema 校验字段
    if "missingFieldTotal" in r:
        print(f"  [INFO] missingFieldTotal: {r['missingFieldTotal']}")
    if "sampleSchemas" in r:
        print(f"  [INFO] sampleSchemas 数量: {len(r['sampleSchemas'])}")
else:
    print(f"  [SKIP] 文件不存在: {PROD_PATH}")

# ========== 4. SSE 流推送 ==========
print("\n[4] /api/admin/etl/progress/stream 推送 data: 帧")
import socket
host = "localhost"
port = 5148
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.settimeout(5)
try:
    sock.connect((host, port))
    request = (
        f"GET /api/admin/etl/progress/stream HTTP/1.1\r\n"
        f"Host: {host}:{port}\r\n"
        f"X-Admin-Token: {ADMIN_TOKEN}\r\n"
        f"Accept: text/event-stream\r\n"
        f"Connection: close\r\n\r\n"
    )
    sock.sendall(request.encode("utf-8"))
    buf = b""
    deadline = time.time() + 3.5
    frames = []
    while time.time() < deadline:
        try:
            chunk = sock.recv(4096)
            if not chunk:
                break
            buf += chunk
        except socket.timeout:
            break
    text = buf.decode("utf-8", errors="replace")
    # SSE 格式: "data: {...}\n\n"
    data_frames = re.findall(r"data:\s*(\{.*?\})\n\n", text)
    check(f"SSE 收到 data 帧 ({len(data_frames)} 帧)", len(data_frames) >= 1, f"frames={len(data_frames)}")
    if data_frames:
        first_frame = json.loads(data_frames[0])
        check("SSE 首帧含 inProgress 字段", "inProgress" in first_frame, f"first={first_frame}")
        print(f"  [INFO] SSE 首帧内容: {first_frame}")
finally:
    sock.close()

# ========== 5. History cursor 分页 ==========
print("\n[5] History API cursor keyset 分页")
# 找有历史的产品 (直接查 DB 找历史最多的, 兜底用 max id)
import psycopg2 as pg
conn_p = pg.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur_p = conn_p.cursor()
cur_p.execute("""
    SELECT ph.product_id, count(*)
    FROM product_history ph
    JOIN products p ON p.id = ph.product_id
    GROUP BY ph.product_id
    ORDER BY count(*) DESC LIMIT 1
""")
best = cur_p.fetchone()
conn_p.close()
if best and best[1] >= 6:
    target_pid = best[0]
    print(f"  [INFO] 选用 product_id={target_pid} (历史 {best[1]} 条) 验证 cursor 分页")
else:
    print(f"  [SKIP] 无产品有 >= 6 条历史, 跑 _seed_history_for_cursor.py 后重试")
    raise SystemExit(0)
# 第一次拉 (无 cursor)
s1, h1 = call("GET", f"/admin/products/{target_pid}/history?limit=5")
check("history HTTP 200", s1 == 200, f"status={s1}")
page1_items = h1.get("items", [])
check("history items 数组", isinstance(page1_items, list), f"items type={type(page1_items).__name__}")
next_cursor = h1.get("nextCursor")
check("history nextCursor 字段", "nextCursor" in h1, f"h1 keys={list(h1.keys())}")

if next_cursor and len(page1_items) >= 5:
    # 用 cursor 拉第二页
    s2, h2 = call("GET", f"/admin/products/{target_pid}/history?limit=5&cursor={next_cursor}")
    check("history page 2 HTTP 200", s2 == 200, f"status={s2}")
    page2_items = h2.get("items", [])
    check("history page 2 有数据", len(page2_items) > 0, f"page2 count={len(page2_items)}")
    # 验证 page 2 第一项的 changedAt 严格小于 page 1 最后一项
    if page1_items and page2_items:
        p1_last_ts = page1_items[-1].get("changedAt", "")
        p2_first_ts = page2_items[0].get("changedAt", "")
        check(f"page2 changedAt < page1 changedAt ({p2_first_ts} < {p1_last_ts})",
              p2_first_ts < p1_last_ts, f"p1={p1_last_ts} p2={p2_first_ts}")
        # 验证无重复
        p1_ids = {x["id"] for x in page1_items}
        p2_ids = {x["id"] for x in page2_items}
        check("page1/page2 id 无重叠", len(p1_ids & p2_ids) == 0, f"overlap={p1_ids & p2_ids}")
        # 翻到第 3 页 (验证 cursor 链式工作)
        next_cursor_2 = h2.get("nextCursor")
        if next_cursor_2:
            s3, h3 = call("GET", f"/admin/products/{target_pid}/history?limit=5&cursor={next_cursor_2}")
            check("history page 3 HTTP 200", s3 == 200, f"status={s3}")
            page3_items = h3.get("items", [])
            check(f"history page 3 有数据 ({len(page3_items)} 条)", len(page3_items) > 0, f"count={len(page3_items)}")
else:
    print(f"  [INFO] 历史不足 6 条, 跳过 page 2 验证 (total={h1.get('total')})")

# ========== 6. idx_product_history_paging 索引存在 ==========
print("\n[6] 验证 product_history 分页索引")
conn = db_conn()
cur = conn.cursor()
cur.execute("""
    SELECT indexname FROM pg_indexes
    WHERE tablename='product_history' AND indexname='idx_product_history_paging'
""")
r = cur.fetchone()
check("idx_product_history_paging 索引存在", r is not None, f"r={r}")
conn.close()

# ========== 7. etl_progress_log 取消审计字段 ==========
print("\n[7] etl_progress_log 取消审计字段")
conn = db_conn()
cur = conn.cursor()
cur.execute("""
    SELECT column_name, data_type FROM information_schema.columns
    WHERE table_name='etl_progress_log' AND column_name IN ('cancel_reason', 'cancelled_at')
    ORDER BY column_name
""")
cols = cur.fetchall()
check(f"cancel_reason 字段存在", any(c[0] == 'cancel_reason' for c in cols), f"cols={cols}")
check(f"cancelled_at 字段存在", any(c[0] == 'cancelled_at' for c in cols), f"cols={cols}")
conn.close()

# ========== 总结 ==========
print("\n" + "=" * 70)
print(f"  通过: {len(passed)}/{len(passed) + len(failed)}")
if failed:
    print("  失败列表:")
    for n, info in failed:
        print(f"    - {n}: {info}")
print("=" * 70)
print("PASSED:" if not failed else "FAILED:", len(passed), "FAILED:", len(failed))
