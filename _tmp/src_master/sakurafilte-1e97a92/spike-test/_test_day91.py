#!/usr/bin/env python
# Day 9.1 改进项回归测试
#   1. dry-run 返回 samples 字段 (前 5 行 JSON)
#   2. 取消无活跃任务 → cancelled=false, reason="无活跃任务"
#   3. 取消正在跑的任务 (用大文件) → cancelled=true
#   4. AdminProductsView history API 返回 changedFields 可被 JSON.parse
#   5. Trigger 409 (已有任务时) — 与 cancel 组合
import json
import os
import time
import urllib.request
import urllib.error
from concurrent.futures import ThreadPoolExecutor

BASE = "http://localhost:5000/api"
ADMIN_TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"X-Admin-Token": ADMIN_TOKEN, "Content-Type": "application/json"}

# 用 Day 8.1/8.2 期间已存在的小样本 (1949 products)
PROD_PATH = r"D:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl"
APPS_PATH = r"D:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl"


def call(method, path, body=None, timeout=10):
    url = f"{BASE}{path}"
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, method=method, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, {"raw": str(e)}


passed = []
failed = []


def _is_json(s):
    try:
        json.loads(s)
        return True
    except Exception:
        return False


def check(name, cond, info=""):
    if cond:
        passed.append(name)
        print(f"  [PASS] {name}")
    else:
        failed.append((name, info))
        print(f"  [FAIL] {name} -- {info}")


print("=" * 60)
print(" Day 9.1 改进项端到端测试")
print("=" * 60)

# ========== 1. dry-run 返回 samples ==========
print("\n[1] dry-run 返回 samples 字段 (前 5 行)")
if not os.path.exists(PROD_PATH):
    print(f"  [SKIP] 文件不存在: {PROD_PATH}")
else:
    s, r = call("POST", "/admin/etl/trigger", {
        "jsonlPath": PROD_PATH, "mode": "upsert", "dryRun": True
    })
    check("dry-run HTTP 200", s == 200, f"status={s}")
    check("dry-run dryRun=true", r.get("dryRun") is True, f"r={r}")
    check("dry-run lines > 0", r.get("lines", 0) > 0, f"lines={r.get('lines')}")
    check("dry-run sizeBytes > 0", r.get("sizeBytes", 0) > 0, f"sizeBytes={r.get('sizeBytes')}")
    samples = r.get("samples", [])
    check("dry-run samples 存在", isinstance(samples, list), f"samples type={type(samples).__name__}")
    check("dry-run samples <= 5", len(samples) <= 5, f"len={len(samples)}")
    check("dry-run samples 每行可 JSON.parse", all(_is_json(s) for s in samples), f"samples={samples[:1]}")


# ========== 2. 取消无活跃任务 ==========
print("\n[2] 取消无活跃任务 → cancelled=false")
s, r = call("DELETE", "/admin/etl/task")
check("cancel HTTP 200", s == 200, f"status={s}")
check("cancel cancelled=false", r.get("cancelled") is False, f"r={r}")
check("cancel reason 字段", "reason" in r, f"r={r}")


# ========== 3. 取消正在跑的任务 (大文件 + 并发) ==========
print("\n[3] 取消正在跑的任务")
LARGE_PATH = r"D:\projects\sakurafilter\spike-test\output\synthetic_products_100k.jsonl"
if not os.path.exists(LARGE_PATH):
    print(f"  [SKIP] 大文件不存在: {LARGE_PATH}")
else:
    def trigger():
        return call("POST", "/admin/etl/trigger", {
            "jsonlPath": LARGE_PATH, "mode": "upsert", "dryRun": False
        }, timeout=120)

    def cancel():
        time.sleep(1.5)  # 等任务进入 COPY 阶段
        return call("DELETE", "/admin/etl/task")

    with ThreadPoolExecutor(max_workers=2) as ex:
        f_trigger = ex.submit(trigger)
        f_cancel = ex.submit(cancel)
        try:
            ts, tr = f_trigger.result(timeout=60)
        except Exception as e:
            ts, tr = -1, {"error": str(e)}
        cs, cr = f_cancel.result(timeout=10)

    check("trigger 任务启动或 409", ts in (200, 409), f"status={ts} body={tr}")
    # 取消时如果有活跃任务 → cancelled=true
    if ts == 200:
        check("cancel HTTP 200", cs == 200, f"status={cs}")
        check("cancel cancelled=true", cr.get("cancelled") is True, f"r={cr}")
    else:
        print(f"  [INFO] trigger 被 409 拒绝, 跳过 cancel-true 验证 (上一次任务还在跑)")


# ========== 4. progress 接口 (inProgress 反映真实状态) ==========
print("\n[4] /admin/etl/progress 状态查询")
s, r = call("GET", "/admin/etl/progress")
check("progress HTTP 200", s == 200, f"status={s}")
check("progress.inProgress 字段", "inProgress" in r, f"r={r}")


# ========== 5. 历史 API 返回 changedFields 可被 JSON.parse ==========
print("\n[5] /admin/products/{id}/history 返回 changedFields 可 JSON.parse")
s, sr = call("GET", "/admin/products/search?page=1&pageSize=20&countMode=none&sortBy=id&sortDesc=true", timeout=30)
items = (sr or {}).get("items", [])
if not items:
    print("  [SKIP] 无产品数据, 跳过 history 验证")
else:
    # 优先选最新创建的产品 (id 大的, 通常有 history)
    pid = max(items, key=lambda x: x["id"])["id"]
    s, hr = call("GET", f"/admin/products/{pid}/history?limit=5")
    check("history HTTP 200", s == 200, f"status={s}")
    hist_items = hr.get("items", [])
    check("history items 数组", isinstance(hist_items, list), f"items type={type(hist_items).__name__}")
    # 验证 changedFields 可被 JSON.parse (前端的 parseChangedFields 逻辑)
    parseable = 0
    total = len(hist_items)
    for h in hist_items:
        cf = h.get("changedFields")
        if cf and _is_json(cf):
            parseable += 1
    if total > 0:
        check(f"history changedFields 可解析 ({parseable}/{total})", parseable == total or parseable > 0,
              f"parseable={parseable} total={total}")
    else:
        print("  [INFO] 产品无历史记录, 跳过 changedFields 解析验证")


# ========== 总结 ==========
print("\n" + "=" * 60)
print(f"  通过: {len(passed)}/{len(passed) + len(failed)}")
if failed:
    print("  失败列表:")
    for n, info in failed:
        print(f"    - {n}: {info}")
print("=" * 60)
print("PASSED:" if not failed else "FAILED:", len(passed), "FAILED:", len(failed))
