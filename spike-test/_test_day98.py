# -*- coding: utf-8 -*-
"""Day 9.8 ETL 历史审计 + reason_code 饼图接口 E2E 测试

覆盖:
  1) GET /api/admin/etl/history 返回 cancelled 列表
  2) GET /api/admin/etl/history/aggregate 按 reason_code 聚合
  3) 字段完整性 (reasonCode/cancelReason/cancelledAt 等)
  4) 数据格式 (pct 0-100, code 在 5 枚举 + LEGACY)
  5) 空数据兜底 (不传 status 也能返回)

Day 9.12: CI 兼容
  - 跨平台路径 (用 os.path.dirname(__file__) 构建, 不再硬编码 D:/)
  - 空数据库 SKIP (参考 _test_day95.py 模式)
"""
import json
import os
import urllib.request
import urllib.error
import sys
import psycopg2

BASE = "http://localhost:5148"
TOKEN = os.environ.get("ADMIN_TOKEN", "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C")
H_ADMIN = {"X-Admin-Token": TOKEN, "Content-Type": "application/json"}
PG_CONF = dict(host="localhost", port=5432, dbname="spike_test_v3", user="postgres", password="784533")

# Day 9.12: 跨平台基准路径
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)

PASS = 0
FAIL = 0
RESULTS = []


def http(method, path, body=None, headers=None, timeout=5):
    url = BASE + path
    h = {"Content-Type": "application/json"}
    if headers:
        h.update(headers)
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        r = urllib.request.urlopen(req, timeout=timeout)
        return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")


def db_cancelled_count():
    """返回 etl_progress_log 中 cancelled 记录数"""
    c = psycopg2.connect(**PG_CONF)
    cur = c.cursor()
    cur.execute("SELECT count(*) FROM etl_progress_log WHERE status = 'cancelled'")
    n = cur.fetchone()[0]
    c.close()
    return n


def case(name, fn):
    global PASS, FAIL
    print(f"\n--- {name} ---")
    try:
        fn()
        PASS += 1
        RESULTS.append((name, "PASS", None))
        print(f"[PASS] {name}")
    except SkipTest as e:
        RESULTS.append((name, "SKIP", str(e)))
        print(f"[SKIP] {name}: {e}")
    except AssertionError as e:
        FAIL += 1
        RESULTS.append((name, "FAIL", str(e)))
        print(f"[FAIL] {name}: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")


class SkipTest(Exception):
    """Day 9.12: 空数据库或 CI 环境不支持时跳过测试"""
    pass


# ========== Case 1: /api/admin/etl/history 返回 cancelled ==========
def test_history_cancelled():
    """验证 /api/admin/etl/history?status=cancelled 返回 cancelled 记录"""
    # Day 9.12: CI 空数据库无 cancelled 记录, SKIP (空数据库不是被测代码问题)
    if db_cancelled_count() == 0:
        raise SkipTest("CI 空数据库无 cancelled 记录, 跳过 (本地有数据时验证)")
    code, body = http("GET", "/api/admin/etl/history?status=cancelled&limit=10", headers=H_ADMIN)
    assert code == 200, f"期望 200, 实际 {code}, body={body[:200]}"
    obj = json.loads(body)
    assert "count" in obj and "items" in obj, f"字段缺失: {list(obj.keys())}"
    assert obj["count"] > 0, f"应有 cancelled 记录, 实际 0 条 (检查 etl_progress_log 是否有 cancelled 数据)"
    assert all(it["status"] == "cancelled" for it in obj["items"]), "items 中存在非 cancelled"
    print(f"  ✓ /history?status=cancelled 返回 {obj['count']} 条, 全部为 cancelled")


# ========== Case 2: /aggregate 按 reason_code 聚合 ==========
def test_aggregate_reason_code():
    """验证 /api/admin/etl/history/aggregate 按 reason_code 聚合"""
    # Day 9.12: CI 空数据库 SKIP
    if db_cancelled_count() == 0:
        raise SkipTest("CI 空数据库无 cancelled 记录, 跳过聚合测试")
    code, body = http("GET", "/api/admin/etl/history/aggregate", headers=H_ADMIN)
    assert code == 200, f"期望 200, 实际 {code}, body={body[:200]}"
    obj = json.loads(body)
    assert "total" in obj and "breakdown" in obj, f"字段缺失: {list(obj.keys())}"
    assert obj["total"] > 0, f"应有 cancelled 记录, 实际 total=0"
    # 验证所有项字段
    for b in obj["breakdown"]:
        assert "code" in b and "count" in b and "pct" in b, f"breakdown 字段缺失: {b}"
        assert b["count"] > 0, f"count 应 > 0: {b}"
        assert 0 <= b["pct"] <= 100, f"pct 应在 0-100: {b}"
    # 验证 code 在白名单
    valid_codes = {"USER_REQUEST", "TIMEOUT", "SYSTEM_SHUTDOWN", "ADMIN_OVERRIDE", "OTHER", "LEGACY"}
    for b in obj["breakdown"]:
        assert b["code"] in valid_codes, f"code 非法: {b['code']}"
    # 验证 pct 之和约等于 100
    pct_sum = sum(b["pct"] for b in obj["breakdown"])
    assert 99.0 <= pct_sum <= 101.0, f"pct 总和 {pct_sum} 偏离 100 过多"
    print(f"  ✓ /aggregate total={obj['total']}, breakdown {len(obj['breakdown'])} 段")
    for b in obj["breakdown"]:
        print(f"    {b['code']:20s} {b['count']:5d} ({b['pct']}%)")


# ========== Case 3: 字段完整性 ==========
def test_history_field_completeness():
    """验证 /history 返回的字段完整 (含 reasonCode/cancelReason/cancelledAt/durationSec)"""
    # Day 9.12: CI 空数据库 SKIP
    if db_cancelled_count() == 0:
        raise SkipTest("CI 空数据库无 cancelled 记录, 跳过字段完整性测试")
    code, body = http("GET", "/api/admin/etl/history?status=cancelled&limit=5", headers=H_ADMIN)
    obj = json.loads(body)
    assert obj["count"] >= 1
    required_fields = [
        "id", "entityType", "mode", "status",
        "reasonCode", "cancelReason", "cancelledAt",
        "readCount", "insertedCount", "updatedCount",
        "skippedCount", "skippedMissingOem", "skippedNullField", "skippedDuplicate",
        "errorCount", "indexedCount", "indexPendingCount",
        "lastError", "startedAt", "finishedAt", "durationSec"
    ]
    for it in obj["items"]:
        missing = [f for f in required_fields if f not in it]
        assert not missing, f"记录 {it['id']} 缺字段 {missing}"
    print(f"  ✓ 所有记录字段完整 ({len(required_fields)} 字段, 抽查 {len(obj['items'])} 条)")


# ========== Case 4: 空数据兜底 (status 过滤) ==========
def test_history_status_filter():
    """验证 status 过滤生效 (取 completed 应无 cancelled 记录)"""
    code, body = http("GET", "/api/admin/etl/history?status=completed&limit=10", headers=H_ADMIN)
    obj = json.loads(body)
    assert obj["count"] >= 0  # 允许 0 (无 completed 时)
    assert all(it["status"] == "completed" for it in obj["items"]), "items 中存在非 completed"
    print(f"  ✓ /history?status=completed 返回 {obj['count']} 条, 全部为 completed")


# ========== Case 5: 真实触发 + 取消 → reason_code 出现 ==========
def test_new_cancel_recorded():
    """触发一次 ETL 后取消, 验证 reason_code=USER_REQUEST 出现在 history
    Day 9.8: 用 polling 等待 status=running, 立即 cancel 避免错过时间窗
    Day 9.8 v2: ETL 字段名必须用 ETL 期望的 oem_no_normalized/oem_no_display
                否则 30K 行 2.9s 跑完 (每行 'key not present'), cancel 永远来不及
                解决方案: 用正确字段名 + 100K 行保证 5-8s 跑完
    """
    import time
    # 先 cancel 一下避免 409
    try:
        req = urllib.request.Request(
            f"{BASE}/api/admin/etl/task",
            data=json.dumps({"reason": "Day 9.8 audit test", "reasonCode": "USER_REQUEST"}).encode(),
            headers={**H_ADMIN, "Content-Type": "application/json"},
            method="DELETE"
        )
        urllib.request.urlopen(req, timeout=2)
    except Exception:
        pass
    time.sleep(1)

    # 准备一个 100K 行正确字段的 JSONL (实测 100K 行 insert-only 约 5-8s)
    # WHY 必须用正确字段: EtlImportService 读 oem_no_normalized/oem_no_display/type
    #   之前用 oem_no 全部 'key not present' 报错, 2.9s 跑完 30K, cancel 永远来不及
    # Day 9.12: 路径跨平台, 用 SCRIPT_DIR/output/ (CI Linux + Windows 都可写)
    out_dir = os.path.join(SCRIPT_DIR, "output")
    os.makedirs(out_dir, exist_ok=True)
    jsonl_path = os.path.join(out_dir, "_test_day98_audit_100k.jsonl")

    # Day 9.8 v3: 文件存在也校验内容, 防止历史错误格式文件被复用
    # WHY: 30K 文件生成时字段名错 (oem_no) 写入磁盘, 后续测试若只检查 exists 就会用错文件
    # v28-4 P0 修复: 校验集补 mr_1 (V2 主键), 旧文件无 mr_1 会触发 ETL 校验失败
    required_keys = {"oem_no_normalized", "oem_no_display", "type", "mr_1"}
    need_regen = True
    if os.path.exists(jsonl_path):
        try:
            with open(jsonl_path, "r", encoding="utf-8") as f:
                first_line = f.readline()
            sample = json.loads(first_line)
            if required_keys.issubset(sample.keys()):
                need_regen = False
                print(f"  [INFO] 复用现有 100K 文件 (字段已正确): {jsonl_path}")
            else:
                print(f"  [WARN] 现有 100K 文件字段错, 删除重建: missing keys = {required_keys - set(sample.keys())}")
                os.remove(jsonl_path)
        except Exception as e:
            print(f"  [WARN] 现有 100K 文件解析失败 ({e}), 删除重建")
            try:
                os.remove(jsonl_path)
            except Exception:
                pass
    if need_regen:
        print(f"  [INFO] 正在生成 100000 测试文件 (用正确字段名)...")
        with open(jsonl_path, "w", encoding="utf-8") as f:
            for i in range(1, 100001):
                f.write(json.dumps({
                    "oem_no_normalized": f"DAY98-AUDIT-{i:06d}",
                    "oem_no_display": f"DAY98-AUDIT-{i:06d}",
                    # v28-4 P0 修复: 补 mr_1 字段 (V2 主键, 必填)
                    #   根因: 之前测试数据缺 mr_1, ETL ImportProductsAsync L869-875 检测到 mr_1 为空
                    #         会 IncrSkippedNullField + continue 跳过该行
                    #   结果: 100K 行全部跳过 → stage=0 → L944 校验 stageCount+Errors != Read 失败
                    #   修复: 补 mr_1 字段, ETL 正常写入 staging 表, stage_count=read, 校验通过
                    "mr_1": f"MR1-DAY98-{i:06d}",
                    "type": "Hydraulic",
                    "product_name_3": f"Day 9.8 Audit {i}",
                    "media": "Synthetic",
                    "d1_mm": 30.0,
                    "h1_mm": 100.0
                }) + "\n")
        print(f"  [INFO] 文件已生成: {jsonl_path}")

    # 先记录当前最大 id (基线)
    code0, body0 = http("GET", "/api/admin/etl/history?status=cancelled&limit=1", headers=H_ADMIN)
    obj0 = json.loads(body0)
    base_max_id = obj0["items"][0]["id"] if obj0["count"] > 0 else 0
    print(f"  [INFO] 基线最大 id = {base_max_id}")

    # 触发
    trigger_req = urllib.request.Request(
        f"{BASE}/api/etl/import",
        data=json.dumps({"jsonlPath": jsonl_path, "mode": "insert-only"}).encode(),
        headers=H_ADMIN,
        method="POST"
    )
    try:
        urllib.request.urlopen(trigger_req, timeout=3)
        print(f"  [INFO] ETL 已触发 (100K 行, 预期 5-8s 跑完)")
    except urllib.error.HTTPError as e:
        if e.code == 409:
            print(f"  [INFO] 已有 ETL 在跑, 跳过本测试")
            return
        raise

    # Day 9.8 v2: polling 等待 status=running (ETL 启动到 running 约 200-500ms)
    saw_running = False
    for i in range(30):
        time.sleep(0.1)
        code_p, body_p = http("GET", "/api/etl/status", headers=H_ADMIN)
        if code_p == 200:
            p = json.loads(body_p)
            if p.get("status") == "running":
                saw_running = True
                print(f"  [INFO] ETL 进入 running 状态 ({(i+1)*100}ms 后)")
                break
    if not saw_running:
        print(f"  [WARN] polling 未见到 running, 强制 cancel")

    # 等 1.5s 让 ETL 进入稳定运行 (避免 cancel 命中 COPY 启动阶段)
    # WHY 1.5s: 启动到 running 后真正在 COPY 暂存, 此时 cancel 才会产生 cancelled 记录
    #   太早 cancel 可能在 ETL 还没进入正轨就被吞掉
    time.sleep(1.5)

    # 立即 cancel
    cancel_req = urllib.request.Request(
        f"{BASE}/api/admin/etl/task",
        data=json.dumps({"reason": "Day 9.8 audit E2E test", "reasonCode": "USER_REQUEST"}).encode(),
        headers={**H_ADMIN, "Content-Type": "application/json"},
        method="DELETE"
    )
    cancel_resp = urllib.request.urlopen(cancel_req, timeout=3)
    cancel_obj = json.loads(cancel_resp.read().decode("utf-8"))
    print(f"  [INFO] 取消响应: cancelled={cancel_obj.get('cancelled')}, normalizedCode={cancel_obj.get('normalizedCode')}")

    # 等 ETL 完结 (取消 + 落库)
    # WHY 8s: 100K 行 insert-only 完整跑完 5-8s, cancel 后会立即终止, 留 8s 兜底
    time.sleep(8)

    # 验证 history 中出现新记录 (id > baseline)
    code, body = http("GET", "/api/admin/etl/history?status=cancelled&limit=20", headers=H_ADMIN)
    obj = json.loads(body)
    if obj["count"] == 0:
        # 取消可能没成功, 检查是否有 failed 记录 (说明 ETL 跑完了)
        code2, body2 = http("GET", "/api/admin/etl/history?status=failed&limit=5", headers=H_ADMIN)
        obj2 = json.loads(body2)
        recent = [it for it in obj2["items"] if it["id"] > base_max_id]
        if recent:
            print(f"  [DEBUG] ETL 完结为 failed (未 cancelled): id={recent[0]['id']} errors={recent[0]['errorCount']} lastError={recent[0].get('lastError','')[:120]}")
        assert False, f"无 cancelled 记录, base_max_id={base_max_id}, latest cancelled.id={obj['items'][0]['id'] if obj['count']>0 else 'N/A'}"
    new_records = [it for it in obj["items"] if it["id"] > base_max_id]
    assert len(new_records) >= 1, f"应有 id>{base_max_id} 的新记录, 实际 0 (history[0].id={obj['items'][0]['id']})"
    newest = new_records[0]
    assert newest["reasonCode"] == "USER_REQUEST", f"新记录应是 USER_REQUEST, 实际 {newest['reasonCode']}"
    assert "Day 9.8 audit" in (newest["cancelReason"] or ""), f"cancelReason 应含 'Day 9.8 audit', 实际 {newest['cancelReason']}"
    print(f"  ✓ 新 cancel 记录入库: id={newest['id']} reasonCode={newest['reasonCode']} reason={newest['cancelReason']}")


# ========== Case 6: 前端组件存在性 + 入口可达性 ==========
def test_frontend_component_built():
    """验证前端 EtlReasonCodePie.vue 组件存在 + 引用了正确 import"""
    # Day 9.12: 跨平台路径 (CI Linux + Windows)
    pie_path = os.path.join(PROJECT_ROOT, "frontend", "src", "components", "EtlReasonCodePie.vue")
    print(f"  [debug] pie_path: {pie_path}")
    print(f"  [debug] exists: {os.path.exists(pie_path)}")
    assert os.path.exists(pie_path), f"前端组件不存在: {pie_path}"
    with open(pie_path, "r", encoding="utf-8") as f:
        content = f.read()
    assert "EtlReasonCodePie" in content
    assert "<svg" in content and "circle" in content, "组件缺少 SVG 圆环"
    assert "USER_REQUEST" in content and "TIMEOUT" in content, "组件缺少 reason_code 映射"
    # 验证 AdminEtlView 引用了它
    admin_path = os.path.join(PROJECT_ROOT, "frontend", "src", "views", "admin", "AdminEtlView.vue")
    with open(admin_path, "r", encoding="utf-8") as f:
        admin = f.read()
    assert "EtlReasonCodePie" in admin, "AdminEtlView.vue 未引用 EtlReasonCodePie"
    assert "reasonCodeAgg" in admin, "AdminEtlView.vue 未使用 reasonCodeAgg state"
    assert "historyItems" in admin, "AdminEtlView.vue 未使用 historyItems state"
    print(f"  ✓ EtlReasonCodePie.vue 存在 + AdminEtlView 引用正确")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== Day 9.8 ETL 审计 + reason_code 饼图 E2E ===")
    print(f"BASE={BASE}")
    case("1. /history?status=cancelled 返回", test_history_cancelled)
    case("2. /aggregate 按 reason_code 聚合", test_aggregate_reason_code)
    case("3. 字段完整性", test_history_field_completeness)
    case("4. status 过滤生效", test_history_status_filter)
    case("5. 新 cancel 记录入库", test_new_cancel_recorded)
    case("6. 前端组件就绪", test_frontend_component_built)

    print(f"\n=== 总结: {PASS} PASS, {FAIL} FAIL ===")
    skip_count = len([r for r in RESULTS if r[1] == "SKIP"])
    if skip_count > 0:
        print(f"  (其中 {skip_count} 个 SKIP: CI 空数据库或环境不支持)")
    for n, s, e in RESULTS:
        marker = "✓" if s == "PASS" else ("○" if s == "SKIP" else "✗")
        print(f"  {marker} [{s}] {n}" + (f"  ({e})" if e else ""))
    # Day 9.12: SKIP 不影响 exit code (CI 空数据库不算失败)
    sys.exit(0 if FAIL == 0 else 1)
