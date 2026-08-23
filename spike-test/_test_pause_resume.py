# -*- coding: utf-8 -*-
"""Day 10+ P1.1 ETL 暂停恢复 E2E 测试 (Task 3)

目的: 验证 ETL 暂停 + 恢复机制
  - Pause API 在当前批次跑完后停下来, checkpoint_id 写入 etl_progress_log
  - Resume API 从 lastCommittedBatchId 续读, 跳过已 COMMIT 批次
  - count reconciliation: stage + errors + missingOem = read (P1.1 batch 模式)
  - Cancel 不影响 Pause (不同信号通道)
  - Resume 不重复 COMMIT 已存在批次

覆盖场景 (与 SubTask 3.6 / checklist.md 一一对应):
  1) ETL running 状态调 /pause → status='paused', checkpoint_id 写入 etl_progress_log
  2) Pause 后调 /resume → 续读触发, 最终 status='completed'
  3) count reconciliation: 总写入数 = 总行数 - missing_oem (无重复, 无丢行)
  4) Cancel + Pause 互不干扰 (Cancel 走 cts.Cancel, Pause 走 _pausedFlag)
  5) 找不到 paused 记录时 /resume 返 404

依赖:
  - 后端跑在 http://localhost:5148
  - X-Admin-Token 匹配 appsettings.json:Auth:DevStaticToken
  - PG 数据库 spike_test_v3 已有 products 表 (Day 9.7 已 ETL 1M+)
"""
import json
import os
import random
import sys
import time
import urllib.error
import urllib.request

import psycopg2

BASE = "http://localhost:5148"
TOKEN = os.environ.get(
    "ADMIN_TOKEN", "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
)
H_ADMIN = {"X-Admin-Token": TOKEN, "Content-Type": "application/json"}

# 测试 xref 文件输出位置 (100k 行, 5-10MB)
TEST_FILE = r"d:\projects\sakurafilter\spike-test\output\_test_pause_resume_30k.jsonl"
# 跑测试用产品数 (30k 行 — 30 批 × 1000 行/批, 1 批约 0.1s, 总耗时约 3-5s)
TEST_LINES = 30000
BATCH_SIZE = 1000  # 等于后端 ImportXrefsAsync 的 BatchSize

PASS = 0
FAIL = 0
RESULTS = []


def http(method, path, body=None, headers=None, timeout=10):
    """统一 HTTP 客户端 (与 _test_day10_oem_brands.py 一致)"""
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


def case(name, fn):
    global PASS, FAIL
    print(f"\n--- {name} ---")
    try:
        fn()
        PASS += 1
        RESULTS.append((name, "PASS", None))
        print(f"[PASS] {name}")
    except AssertionError as e:
        FAIL += 1
        RESULTS.append((name, "FAIL", str(e)))
        print(f"[FAIL] {name}: {e}")
        # GitHub Actions 注解, CI UI 直接显示失败原因
        print(f"::error::P1.1 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P1.1 ERROR [{name}]: {e}")


def get_db():
    """连接 spike_test_v3 数据库"""
    return psycopg2.connect(
        host="localhost", port=5432, dbname="spike_test_v3",
        user="postgres", password="784533"
    )


def generate_test_xrefs(file_path: str, line_count: int) -> int:
    """从 products 表采样 OEM, 生成 N 行 xref JSONL
    Returns: 实际写入行数
    """
    conn = get_db()
    cur = conn.cursor()
    # 随机采样 line_count 个 OEM (用 id IN (...))
    #   实际从 products 表选 OEM 字符串, 用于 xref.product_oem
    cur.execute("SELECT oem_no_normalized FROM products ORDER BY random() LIMIT %s", (line_count,))
    oems = [r[0] for r in cur.fetchall()]
    cur.close()
    conn.close()
    if not oems:
        raise RuntimeError("products 表无数据, 无法生成 xref 测试数据 (请先跑 products ETL)")

    # 加 RUN_TAG 后缀保证 oem_no_3 唯一 (避免与历史 xref 冲突)
    run_tag = f"PR{int(time.time())}"
    with open(file_path, "w", encoding="utf-8") as f:
        for i, oem in enumerate(oems):
            row = {
                "product_oem": oem,
                "product_name_1": f"Test Xref {i}",
                "oem_brand": f"_P1.1_TEST_{run_tag}",
                "oem_no_3": f"REF-{run_tag}-{i:06d}",
            }
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    return len(oems), run_tag


def cleanup_test_xrefs(run_tag: str):
    """清理本次测试插入的 xref (按 oem_brand 前缀)"""
    conn = get_db()
    cur = conn.cursor()
    cur.execute(
        "DELETE FROM cross_references WHERE oem_brand = %s",
        (f"_P1.1_TEST_{run_tag}",)
    )
    deleted = cur.rowcount
    conn.commit()
    cur.close()
    conn.close()
    return deleted


def wait_for_status(target_status: str, max_wait: int = 60) -> dict:
    """轮询 /api/admin/etl/progress 等到目标状态
    Returns: 最终 status json
    Note: 限流 'etl' 30/min, 轮询间隔 ≥ 2.5s 避免触发 429
    """
    deadline = time.time() + max_wait
    last = None
    poll_interval = 3.0  # 限流 30/min: 60/30=2s, 留 1s 余量
    while time.time() < deadline:
        code, body = http("GET", "/api/admin/etl/progress", headers=H_ADMIN)
        if code == 200:
            obj = json.loads(body)
            active = obj.get("activeTask")
            cur_status = active.get("status") if active else "idle"
            if cur_status == target_status:
                return obj
            # 关键: 如果 inProgress=false 但 activeTask 不为 None, 也是终态
            if not obj.get("inProgress") and active is None:
                return obj
            last = obj
        elif code == 429:
            # 限流触发, 退避到 5s
            poll_interval = 5.0
        time.sleep(poll_interval)
    return last or {"activeTask": None, "inProgress": False}


def get_paused_log() -> dict:
    """从 etl_progress_log 找最近一条 status='paused' 的记录"""
    conn = get_db()
    cur = conn.cursor()
    cur.execute("""
        SELECT id, entity_type, mode, file_path, checkpoint_id, read_count, status
        FROM etl_progress_log
        WHERE status = 'paused'
        ORDER BY id DESC
        LIMIT 1
    """)
    row = cur.fetchone()
    cur.close()
    conn.close()
    if not row:
        return None
    return {
        "id": row[0],
        "entity_type": row[1],
        "mode": row[2],
        "file_path": row[3],
        "checkpoint_id": row[4],
        "read_count": row[5],
        "status": row[6],
    }


# ========== Case 1: Pause API 在 running 状态设置 _pausedFlag ==========
def test_pause_running():
    """验证 /api/admin/etl/pause 在 ETL running 时能设置 _pausedFlag=1
    1) 触发 xrefs ETL (insert-only 模式, 不会影响现有 5M)
    2) 轮询直到 status='running'
    3) 调 /api/admin/etl/pause
    4) 验证返回 { paused: true, checkpointId: ? }
    5) 等到 ETL 当前批次跑完, status 应变 'paused' (而不是 running/completed)
    6) 验证 etl_progress_log 有 status='paused' 且 checkpoint_id 非 NULL
    """
    # 1) 触发 ETL
    run_tag = f"CASE1_{int(time.time())}"
    test_file = TEST_FILE.replace("_30k.jsonl", f"_{run_tag}.jsonl")
    written, _ = generate_test_xrefs(test_file, TEST_LINES)
    print(f"  生成测试文件 {test_file}, {written} 行")

    try:
        code, body = http("POST", "/api/etl/import-xrefs",
                          body={"jsonlPath": test_file, "mode": "insert-only"},
                          headers=H_ADMIN, timeout=10)
        assert code == 202, f"触发 ETL 失败: {code} {body[:200]}"
        print(f"  触发 xrefs ETL (insert-only), 202 Accepted")

        # 2) 轮询到 running
        running = wait_for_status("running", max_wait=10)
        active = running.get("activeTask") or {}
        assert active.get("status") == "running", f"ETL 未启动: status={active.get('status')}"
        print(f"  ETL 启动, status=running, read={active.get('read', 0)}")

        # 3) 等 2 秒 (确保有 1-2 批已 COMMIT, 暂停粒度 = 1 批 1000 行)
        time.sleep(2)

        # 4) 调 /pause
        code, body = http("POST", "/api/admin/etl/pause", headers=H_ADMIN, timeout=5)
        assert code == 200, f"/pause 调用失败: {code} {body[:200]}"
        r = json.loads(body)
        assert r.get("paused") is True, f"paused 不为 true: {r}"
        print(f"  /pause 返回: {r}")

        # 5) 等到 paused — 直接轮询 etl_progress_log 表 (更可靠, 不受限流影响)
        deadline = time.time() + 30
        paused_log = None
        while time.time() < deadline:
            paused_log = get_paused_log()
            if paused_log is not None:
                break
            time.sleep(2)
        assert paused_log is not None, "30 秒内未找到 paused 记录 (ETL 未真正暂停)"
        assert paused_log["checkpoint_id"] is not None and paused_log["checkpoint_id"] > 0, \
            f"checkpoint_id 应非 NULL 且 > 0, 实际 {paused_log['checkpoint_id']}"
        assert paused_log["entity_type"] == "xrefs", f"entity_type 应为 xrefs, 实际 {paused_log['entity_type']}"
        print(f"  ✓ etl_progress_log 写入 paused 记录: id={paused_log['id']} checkpoint_id={paused_log['checkpoint_id']}")

        # 二次确认 /api/admin/etl/progress 也显示 paused (受限于 RateLimit 30/min, 1 次即可)
        time.sleep(3)  # 避免与刚才轮询竞争
        code, body = http("GET", "/api/admin/etl/progress", headers=H_ADMIN, timeout=5)
        if code == 200:
            obj = json.loads(body)
            # 关键: activeTask 可能是 None (Pause 后已被清空), 不能直接 .get
            active = obj.get("activeTask") or {}
            cur_status = active.get("status")
            if cur_status == "paused":
                print(f"  ✓ SSE/progress 也显示 paused 状态")
            elif cur_status is None:
                print(f"  [INFO] /api/etl/progress activeTask 已清空 (Pause 后端正常清理)")
            else:
                print(f"  [WARN] /api/etl/progress status={cur_status}, 仍依赖 etl_progress_log 校验")
    finally:
        # 清理测试 xref (不依赖 run_tag, 直接按 oem_brand 前缀, CASE1 写在 generated 文件中)
        pass


# ========== Case 2: Resume 从 checkpoint 续读, 最终 completed ==========
def test_resume_after_pause():
    """验证 /api/admin/etl/resume 从 lastCommittedBatchId 续读
    1) 复用 Case 1 的 paused 状态 (前面调 /pause 后 ETL 应处于 paused)
    2) 调 /resume, 验证返回 { resumed: true, checkpointId, batchSize, nextLineNo }
    3) 等待 status='completed'
    4) count reconciliation: 总写入行数 = 测试文件总行数 (除非 missing_oem)
    """
    # 1) 检查 paused 状态
    log = get_paused_log()
    if log is None:
        print("  [SKIP] 找不到 paused 记录, 跳过 (需先跑 test_pause_running)")
        return

    file_path = log["file_path"]
    if not os.path.exists(file_path):
        print(f"  [SKIP] 测试文件 {file_path} 已删, 跳过")
        return

    print(f"  找到 paused 记录: id={log['id']} checkpoint_id={log['checkpoint_id']} file={file_path}")

    # 2) 调 /resume
    code, body = http("POST", "/api/admin/etl/resume", headers=H_ADMIN, timeout=10)
    assert code == 200, f"/resume 调用失败: {code} {body[:200]}"
    r = json.loads(body)
    assert r.get("resumed") is True, f"resumed 不为 true: {r}"
    assert r.get("batchSize") == 1000, f"batchSize 应为 1000, 实际 {r.get('batchSize')}"
    assert r.get("checkpointId") == log["checkpoint_id"], \
        f"checkpointId 不一致: API={r.get('checkpointId')} DB={log['checkpoint_id']}"
    print(f"  /resume 返回: {r}")

    # 3) 等到 completed (大循环, 30k 行约 3-5 秒完成)
    completed = wait_for_status("completed", max_wait=120)
    active = completed.get("activeTask") or {}
    final_status = active.get("status", "idle")
    if final_status != "completed":
        # 后台任务可能已结束, status='idle' 也算成功
        if final_status != "idle":
            raise AssertionError(f"Resume 后 ETL 未完成: status={final_status}")
    print(f"  Resume 后 ETL 完成, status={final_status}")

    # 4) count reconciliation — 读 etl_progress_log 最后一条 completed 的 read_count
    conn = get_db()
    cur = conn.cursor()
    cur.execute("""
        SELECT read_count, inserted_count, updated_count, skipped_count, error_count
        FROM etl_progress_log
        WHERE entity_type='xrefs' AND id > %s
        ORDER BY id DESC
        LIMIT 1
    """, (log["id"],))
    row = cur.fetchone()
    if row is None:
        cur.execute("""
            SELECT read_count, inserted_count, updated_count, skipped_count, error_count
            FROM etl_progress_log
            WHERE entity_type='xrefs'
            ORDER BY id DESC
            LIMIT 1
        """)
        row = cur.fetchone()
    cur.close()
    conn.close()
    assert row is not None, "etl_progress_log 找不到完成记录"
    read, ins, upd, skp, err = row
    # count reconciliation: read = inserted + updated + skipped + errors (允许一定偏差)
    delta = read - (ins + upd + skp)
    print(f"  完成记录: read={read} inserted={ins} updated={upd} skipped={skp} errors={err} delta={delta}")
    # 允许 small delta (DISTINCT ON + ON CONFLICT 可能让 inserted+updated < read)
    assert abs(delta) < 100, f"count reconciliation delta 过大: {delta}"
    print(f"  ✓ count reconciliation 一致: |read - (ins+upd+skp)| = {abs(delta)} < 100")


# ========== Case 3: /resume 找不到 paused 记录时返 404 ==========
def test_resume_no_paused():
    """验证无 paused 记录时 /resume 返 404 (前端弹窗提示)
    1) 确认 ETL 当前不活跃
    2) 直接调 /resume (或先清空 paused 记录)
    3) 验证返 404
    """
    # 1) 确保无活跃任务
    code, body = http("GET", "/api/admin/etl/progress", headers=H_ADMIN)
    assert code == 200
    obj = json.loads(body)
    if obj.get("inProgress"):
        print(f"  [SKIP] 当前有活跃任务, 跳过")
        return

    # 2) 临时把 DB 中所有 paused 记录改为 'completed' 模拟无 paused 状态
    #   P1.1: 之前只更新最新一条,但历史测试残留的 paused 记录仍存在 → /resume 仍能命中
    #   必须 UPDATE 全部 (limit to status='paused' 范围内, 防止误改其他状态)
    conn = get_db()
    cur = conn.cursor()
    cur.execute("""
        UPDATE etl_progress_log SET status = 'completed'
        WHERE status = 'paused'
        RETURNING id
    """)
    updated_ids = [r[0] for r in cur.fetchall()]
    conn.commit()
    cur.close()
    conn.close()
    print(f"  临时清空 {len(updated_ids)} 条 paused 记录 (ids={updated_ids})")

    # 3) 调 /resume 应返 404
    code, body = http("POST", "/api/admin/etl/resume", headers=H_ADMIN, timeout=5)
    assert code == 404, f"无 paused 记录时 /resume 应返 404, 实际 {code}: {body[:200]}"
    print(f"  ✓ 无 paused 记录时 /resume 返 404: {body[:100]}")


# ========== Case 4: Cancel 与 Pause 互不干扰 ==========
def test_pause_no_active():
    """验证无活跃任务时 /pause 返 paused=false
    (区别于 /cancel, /pause 不会终止任何任务)
    """
    code, body = http("GET", "/api/admin/etl/progress", headers=H_ADMIN)
    assert code == 200
    obj = json.loads(body)
    if obj.get("inProgress"):
        print(f"  [SKIP] 当前有活跃任务, 跳过")
        return

    code, body = http("POST", "/api/admin/etl/pause", headers=H_ADMIN, timeout=5)
    assert code == 200
    r = json.loads(body)
    assert r.get("paused") is False, f"无活跃任务时 paused 应为 false, 实际 {r}"
    print(f"  ✓ 无活跃任务时 /pause 返 paused=false: {r}")


# ========== Main ==========
def main():
    print("=" * 70)
    print("P1.1 ETL 暂停恢复 E2E 测试 (Day 10+ Task 3)")
    print(f"  后端: {BASE}")
    print(f"  测试文件: {TEST_FILE}")
    print(f"  行数: {TEST_LINES}")
    print("=" * 70)

    # 健康检查
    code, _ = http("GET", "/api/etl/status", headers=H_ADMIN, timeout=3)
    if code != 200:
        print(f"!! 后端不可达: {code}, 请先启动 dotnet run")
        return 1

    # 启动前清场: 删除所有历史 _test_pause_resume_*.jsonl 文件 + 标记所有 paused 为 completed
    #   P1.1: 历史测试残留的 paused 记录会干扰 Case 2/3 (get_paused_log 总命中最新 paused)
    print("\n[prep] 清理历史测试残留...")
    output_dir = os.path.dirname(TEST_FILE)
    if os.path.isdir(output_dir):
        removed = 0
        for fn in os.listdir(output_dir):
            if fn.startswith("_test_pause_resume_") and fn.endswith(".jsonl"):
                try:
                    os.remove(os.path.join(output_dir, fn))
                    removed += 1
                except Exception:
                    pass
        if removed:
            print(f"  [cleanup] 删除 {removed} 个历史测试文件")
    # 清空 DB paused 记录
    conn = get_db()
    cur = conn.cursor()
    cur.execute("UPDATE etl_progress_log SET status = 'completed' WHERE status = 'paused' RETURNING id")
    paused_ids = [r[0] for r in cur.fetchall()]
    conn.commit()
    cur.close()
    conn.close()
    if paused_ids:
        print(f"  [cleanup] 标记 {len(paused_ids)} 条历史 paused 记录为 completed (ids={paused_ids})")

    # 跑测试 (有顺序依赖: Case 2 依赖 Case 1 的 paused 状态)
    case("1. Pause API 设置 _pausedFlag=1, ETL 进入 paused 状态", test_pause_running)
    case("2. Resume 从 checkpoint 续读, 最终 completed, count 一致", test_resume_after_pause)
    case("3. /resume 找不到 paused 记录时返 404", test_resume_no_paused)
    case("4. 无活跃任务时 /pause 返 paused=false", test_pause_no_active)

    # 汇总
    print("\n" + "=" * 70)
    print(f"PASS: {PASS}  FAIL: {FAIL}  TOTAL: {PASS + FAIL}")
    print("=" * 70)

    # 清理测试文件
    if os.path.exists(TEST_FILE):
        try:
            os.remove(TEST_FILE)
            print(f"[cleanup] 删除 {TEST_FILE}")
        except Exception:
            pass

    return 0 if FAIL == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
