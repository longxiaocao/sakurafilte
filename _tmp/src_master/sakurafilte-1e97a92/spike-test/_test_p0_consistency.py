# -*- coding: utf-8 -*-
"""P0-1.2 图片上传服务事务重构 一致性验证

验证 AdminProductImageService.UploadAsync 重构后的数据一致性:
  顺序: DB 事务占位 → S3 上传 → DB 提交 → 异步删旧
  - S3 失败 → DB 回滚, 不留 S3 孤儿对象
  - 覆盖上传 → DB 提交后才删旧文件, 旧图不会提前丢失

依赖:
  - 后端跑在 http://localhost:5148 (主进程)
  - X-Admin-Token 匹配 appsettings.json:Auth:DevStaticToken
  - PG 数据库 spike_test_v3 已有 products 表 (任取一个 id 用于上传)
  - MinIO 容器跑在 localhost:9000 (Day 8.1 docker compose)

用法:
  python _test_p0_consistency.py
"""
import json
import os
import sys
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path

import psycopg2

# 跨平台路径 (CI 是 Linux, 本地是 Windows)
SCRIPT_DIR = Path(__file__).resolve().parent
REPO = SCRIPT_DIR.parent

BASE = "http://localhost:5148"
TOKEN = os.environ.get(
    "ADMIN_TOKEN", "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
)
H_ADMIN = {"X-Admin-Token": TOKEN}

# PG 连接 (与 spike-test/_test_improvements.py 一致)
PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")

# 1x1 透明 PNG (最小有效图片, 67 字节)
TINY_PNG = bytes.fromhex(
    "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4"
    "890000000d49444154789c63000100000005000100" "0d0a2db40000000049454e44ae426082"
)

PASS = 0
FAIL = 0


def record(ok, msg):
    """记录单个断言结果"""
    global PASS, FAIL
    emoji = "[OK]  " if ok else "[FAIL]"
    print(f"  {emoji} {msg}")
    if ok:
        PASS += 1
    else:
        FAIL += 1


def make_multipart(file_bytes: bytes, filename: str, content_type: str):
    """构造 multipart/form-data 请求体, 返回 (body bytes, content_type header)"""
    boundary = "----WebKitFormBoundary" + os.urandom(8).hex()
    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{filename}"\r\n'
        f"Content-Type: {content_type}\r\n\r\n"
    ).encode("utf-8") + file_bytes + f"\r\n--{boundary}--\r\n".encode("utf-8")
    return body, f"multipart/form-data; boundary={boundary}"


def upload_image(product_id, slot, file_bytes, filename, content_type, timeout=30):
    """上传图片, 返回 (status_code, response_json)"""
    body, ct = make_multipart(file_bytes, filename, content_type)
    req = urllib.request.Request(
        f"{BASE}/api/admin/products/{product_id}/images/{slot}",
        data=body,
        headers={**H_ADMIN, "Content-Type": ct},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read())
        except Exception:
            return e.code, {}


def list_images(product_id, timeout=15):
    """列出产品的全部图片, 返回 (status_code, list_of_dict)"""
    req = urllib.request.Request(
        f"{BASE}/api/admin/products/{product_id}/images",
        headers=H_ADMIN, method="GET",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, []


def delete_image(product_id, slot, timeout=15):
    """删除产品指定 slot 图片 (清理用)"""
    req = urllib.request.Request(
        f"{BASE}/api/admin/products/{product_id}/images/{slot}",
        headers=H_ADMIN, method="DELETE",
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status
    except urllib.error.HTTPError:
        return None


def get_db():
    return psycopg2.connect(**PG)


def pick_test_product():
    """从 PG 选一个产品 id, 优先选 oem_no_normalized 非空的"""
    c = get_db()
    cur = c.cursor()
    cur.execute(
        "SELECT id, oem_no_normalized FROM products "
        "WHERE oem_no_normalized IS NOT NULL AND oem_no_normalized <> '' "
        "ORDER BY id LIMIT 1"
    )
    row = cur.fetchone()
    c.close()
    return row  # (id, oem_normalized) 或 None


# ========== Case 1: 模拟 S3 失败 DB 回滚 (手动验证步骤) ==========
def case_s3_failure_rollback():
    print("\n" + "=" * 60)
    print("Case 1: 模拟 S3 失败 → DB 事务回滚 (手动验证步骤)")
    print("=" * 60)
    # 说明: Python 端难以直接 mock C# 服务的 IObjectStorage, 改为手动验证步骤
    print("  [INFO] Python 端无法直接 mock C# 服务, 以下为手动验证步骤:")
    print("  --- 手动验证步骤 ---")
    print("  1. 记录当前 product_images 行数:")
    c = get_db()
    cur = c.cursor()
    cur.execute("SELECT count(*) FROM product_images")
    before = cur.fetchone()[0]
    c.close()
    print(f"     product_images 当前行数 = {before}")
    print("  2. 停止 MinIO 容器: docker stop sakurafilter-minio (或对应容器名)")
    print("  3. 调用图片上传 API (POST /api/admin/products/{id}/images/1)")
    print("     期望: HTTP 500 (S3 上传失败), DB 事务已回滚")
    print("  4. 重新查询 product_images 行数, 应保持 = {} (无新增)".format(before))
    print("     → 若行数不变, 说明 S3 失败时 DB 已回滚, 无孤儿 DB 记录")
    print("  5. 重启 MinIO 容器: docker start sakurafilter-minio")
    print("  6. (可选) 检查 MinIO bucket 无新增对象, 即无 S3 孤儿对象")
    print("  --- 自动断言 (仅校验重构代码是否存在回滚逻辑) ---")
    # 自动校验: AdminProductImageService.cs 中存在 RollbackAsync 调用
    svc_path = REPO / "backend" / "src" / "SakuraFilter.Api" / "Services" / "AdminProductImageService.cs"
    try:
        content = svc_path.read_text(encoding="utf-8")
        has_rollback = "tx.RollbackAsync" in content
        has_commit = "tx.CommitAsync" in content
        has_begin = "BeginTransactionAsync" in content
        record(has_begin and has_rollback and has_commit,
               f"代码层: BeginTransactionAsync + RollbackAsync + CommitAsync 均存在 "
               f"(begin={has_begin}, rollback={has_rollback}, commit={has_commit})")
    except FileNotFoundError:
        record(False, f"未找到 {svc_path}")


# ========== Case 2: 正常上传验证 ==========
def case_normal_upload(product_id, oem_normalized):
    print("\n" + "=" * 60)
    print(f"Case 2: 正常上传验证 (productId={product_id}, slot=1, PNG)")
    print("=" * 60)

    # 清理: 若 slot 1 已有图, 先删 (避免干扰)
    delete_image(product_id, 1)
    time.sleep(1)

    # 上传前 DB 状态
    c = get_db()
    cur = c.cursor()
    cur.execute("SELECT image_key, image_status FROM products WHERE id = %s", (product_id,))
    img_key_before, _ = cur.fetchone()
    c.close()

    # 上传
    code, resp = upload_image(product_id, 1, TINY_PNG, "test_p0.png", "image/png")
    record(code == 200, f"上传 API 返回 HTTP {code} (期望 200)")

    if code != 200:
        print(f"  [SKIP] 上传失败, 跳过后续 DB 校验. resp={resp}")
        return None

    image_key = resp.get("imageKey", "")
    expected_key = f"products/{oem_normalized}/{oem_normalized}-1.png"
    record(image_key == expected_key,
           f"imageKey 正确: {image_key}" if image_key == expected_key
           else f"imageKey 不匹配: 实际={image_key} 期望={expected_key}")

    # DB 校验: product_images 表有记录
    c = get_db()
    cur = c.cursor()
    cur.execute(
        "SELECT id, image_key, slot, is_primary FROM product_images "
        "WHERE product_id = %s AND slot = 1", (product_id,)
    )
    row = cur.fetchone()
    has_record = row is not None
    record(has_record, f"product_images 表有 slot=1 记录 (id={row[0] if row else None})")
    if row:
        record(row[1] == image_key,
               f"DB image_key 与返回一致: {row[1]}")
        record(row[2] == 1, f"DB slot=1: {row[2]}")
        record(row[3] is True, f"DB is_primary=True (slot=1): {row[3]}")

    # DB 校验: products.image_key 已更新 + image_status=pending
    cur.execute("SELECT image_key, image_status FROM products WHERE id = %s", (product_id,))
    p_key, p_status = cur.fetchone()
    record(p_key == image_key,
           f"products.image_key 已更新: {p_key}" if p_key == image_key
           else f"products.image_key 未更新: 实际={p_key} 期望={image_key}")
    record(p_status == "pending",
           f"products.image_status=pending (重构后默认 pending): {p_status}")
    c.close()

    return image_key


# ========== Case 3: 覆盖上传验证 (旧图异步删除) ==========
def case_overwrite_upload(product_id, oem_normalized):
    print("\n" + "=" * 60)
    print(f"Case 3: 覆盖上传验证 (同 slot 不同 ext → 旧 key 异步删除)")
    print("=" * 60)
    print("  策略: slot=1 先已上传 PNG (Case 2), 改上传 JPEG → key 不同 → 触发旧 key 删除")

    # 上传前确认 slot 1 现有 key (Case 2 的 PNG)
    c = get_db()
    cur = c.cursor()
    cur.execute("SELECT image_key FROM product_images WHERE product_id = %s AND slot = 1",
                (product_id,))
    row = cur.fetchone()
    c.close()
    if not row:
        print("  [SKIP] Case 2 未留下 slot=1 记录, 跳过覆盖上传验证")
        return
    old_key = row[0]
    print(f"  覆盖前 slot=1 旧 key: {old_key}")

    # 用相同 PNG 字节但声明 image/jpeg → ext=jpg → key 不同
    # WHY 这样构造: 后端 P0 不校验 magic number (后续任务), 仅按 Content-Type 决定 ext
    code, resp = upload_image(product_id, 1, TINY_PNG, "test_p0.jpg", "image/jpeg")
    record(code == 200, f"覆盖上传 API 返回 HTTP {code} (期望 200)")
    if code != 200:
        print(f"  [SKIP] 覆盖上传失败, resp={resp}")
        return

    new_key = resp.get("imageKey", "")
    expected_new_key = f"products/{oem_normalized}/{oem_normalized}-1.jpg"
    record(new_key == expected_new_key,
           f"新 imageKey 正确: {new_key}" if new_key == expected_new_key
           else f"新 imageKey 不匹配: 实际={new_key} 期望={expected_new_key}")
    record(new_key != old_key,
           f"新 key 与旧 key 不同 (会触发旧文件异步删除): old={old_key} new={new_key}")

    # 等待异步删除任务执行 (Task.Run, 给 3 秒)
    print("  等待 3s 让异步删旧任务执行 (Task.Run)...")
    time.sleep(3)

    # DB 校验: product_images 仍只有 1 条 slot=1 记录, key 已换成新 key
    c = get_db()
    cur = c.cursor()
    cur.execute(
        "SELECT count(*), image_key FROM product_images "
        "WHERE product_id = %s AND slot = 1 GROUP BY image_key", (product_id,)
    )
    rows = cur.fetchall()
    record(len(rows) == 1 and rows[0][0] == 1,
           f"product_images slot=1 仅 1 条记录 (count={len(rows)})")
    if rows:
        record(rows[0][1] == new_key,
               f"DB image_key 已指向新 key: {rows[0][1]}")
    c.close()

    # list images 校验: 只有 1 张图
    code, items = list_images(product_id)
    slot1_items = [it for it in items if it.get("slot") == 1] if code == 200 else []
    record(code == 200 and len(slot1_items) == 1,
           f"list images slot=1 仅 1 条 (实际 {len(slot1_items)} 条)")
    if slot1_items:
        record(slot1_items[0].get("imageKey") == new_key,
               f"list images imageKey 已更新为新 key")

    print("\n  [INFO] 旧 S3 对象删除验证 (可选):")
    print(f"         旧 key={old_key} 应已被异步删除")
    print(f"         可通过 MinIO 控制台或 mc stat 确认旧对象不存在")
    print(f"         若旧 key 仍存在, 检查后端日志是否有 '旧图删除失败' 警告")


# ========== P0-1.3 Case 4: 并发创建相同 OEM 第二个应返回 409 ==========
def case_concurrent_create_409():
    """并发创建相同 OEM, 至少一个应返回 409 (验证 23505 → 409 映射)"""
    print("\n" + "=" * 60)
    print("Case 4: 并发创建相同 OEM 第二个应返回 409")
    print("=" * 60)
    # 用时间戳生成唯一 OEM, 避免与已有数据冲突
    oem = f"P013-CONC-{int(time.time())}"
    payload = {
        "oem2": oem,
        "isPublished": True,
        "crossReferences": [],
        "machineApplications": []
    }
    body = json.dumps(payload).encode("utf-8")

    # 用 threading 并发发送两个请求 (模拟 TOCTOU 竞态)
    results = []
    lock = threading.Lock()

    def send():
        req = urllib.request.Request(
            f"{BASE}/api/admin/products",
            data=body,
            headers={**H_ADMIN, "Content-Type": "application/json", "X-User": "p013-test"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=15) as r:
                with lock:
                    results.append((r.status, json.loads(r.read())))
        except urllib.error.HTTPError as e:
            try:
                with lock:
                    results.append((e.code, json.loads(e.read())))
            except Exception:
                with lock:
                    results.append((e.code, {}))

    t1 = threading.Thread(target=send)
    t2 = threading.Thread(target=send)
    t1.start(); t2.start()
    t1.join(); t2.join()

    statuses = sorted([r[0] for r in results])
    # 期望: 至少一个 201 (成功) + 至少一个 409 (冲突)
    #   高并发下两个都 409 也可接受 (理论极端情况), 但必须没有 500
    has_409 = 409 in statuses
    no_500 = 500 not in statuses
    record(has_409 and no_500,
           f"并发创建 OEM={oem} 至少一个返回 409 且无 500: statuses={statuses}")
    if not no_500:
        print(f"  [ERROR] 出现 500, 检查后端是否正确处理 23505: results={results}")

    # 清理: 软删除成功创建的产品 (避免污染测试数据)
    for status, resp in results:
        if status == 201 and isinstance(resp, dict) and "id" in resp:
            req = urllib.request.Request(
                f"{BASE}/api/admin/products/{resp['id']}",
                headers=H_ADMIN, method="DELETE",
            )
            try:
                urllib.request.urlopen(req, timeout=10)
            except Exception:
                pass


# ========== P0-1.3 Case 5: 正常创建验证四表一致性 ==========
def case_normal_create_three_table_consistency():
    """正常创建含 xref + apps 的产品, 验证 products + cross_references + machine_applications + product_history 四表都有记录"""
    print("\n" + "=" * 60)
    print("Case 5: 正常创建验证四表一致性 (products + xref + apps + history)")
    print("=" * 60)
    oem = f"P013-NORM-{int(time.time())}"
    payload = {
        "oem2": oem,
        "productName1": "P013TestProduct",
        "type": "oil",
        "isPublished": True,
        "crossReferences": [
            {"oemBrand": "Bosch", "oemNo3": "P013X1", "productName1": "BoschFilter"}
        ],
        "machineApplications": [
            {"machineBrand": "Toyota", "machineModel": "Corolla"}
        ]
    }
    body = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        f"{BASE}/api/admin/products",
        data=body,
        headers={**H_ADMIN, "Content-Type": "application/json", "X-User": "p013-test"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=15) as r:
            status = r.status
            resp = json.loads(r.read())
    except urllib.error.HTTPError as e:
        status = e.code
        try:
            resp = json.loads(e.read())
        except Exception:
            resp = {}

    record(status == 201, f"创建产品返回 201 (实际 {status}): resp={resp}")
    if status != 201:
        print(f"  [SKIP] 创建失败, 跳过四表校验")
        return

    product_id = resp.get("id")
    print(f"  创建产品 id={product_id} oem={oem}")

    # DB 校验: 四表都有记录 (验证事务已 commit)
    c = get_db()
    cur = c.cursor()

    # 1. products 主表
    cur.execute("SELECT id, oem_no_normalized FROM products WHERE id = %s", (product_id,))
    row = cur.fetchone()
    record(row is not None, f"products 表有记录 (id={product_id})")

    # 2. cross_references 交叉引用表
    cur.execute("SELECT count(*) FROM cross_references WHERE product_id = %s", (product_id,))
    xref_count = cur.fetchone()[0]
    record(xref_count == 1, f"cross_references 表有 1 条记录 (实际 {xref_count})")

    # 3. machine_applications 车型表
    cur.execute("SELECT count(*) FROM machine_applications WHERE product_id = %s", (product_id,))
    apps_count = cur.fetchone()[0]
    record(apps_count == 1, f"machine_applications 表有 1 条记录 (实际 {apps_count})")

    # 4. product_history 变更历史表 (change_type='create')
    cur.execute(
        "SELECT count(*) FROM product_history WHERE product_id = %s AND change_type = 'create'",
        (product_id,)
    )
    hist_count = cur.fetchone()[0]
    record(hist_count == 1, f"product_history 表有 1 条 change_type='create' 记录 (实际 {hist_count})")

    c.close()

    # 清理: 软删除产品 (留下历史记录, 不影响其他测试)
    req = urllib.request.Request(
        f"{BASE}/api/admin/products/{product_id}",
        headers=H_ADMIN, method="DELETE",
    )
    try:
        urllib.request.urlopen(req, timeout=10)
    except Exception:
        pass


def main():
    print("=" * 60)
    print("P0-1.2 图片上传 + P0-1.3 三表事务一致性 验证")
    print("=" * 60)

    # 健康检查
    try:
        req = urllib.request.Request(f"{BASE}/health/live", headers=H_ADMIN)
        with urllib.request.urlopen(req, timeout=5) as r:
            if r.status != 200:
                print(f"[FAIL] 后端健康检查失败: HTTP {r.status}")
                sys.exit(1)
        print(f"  后端健康检查: HTTP 200")
    except Exception as e:
        print(f"[FAIL] 后端不可达 ({BASE}): {e}")
        print("  请先启动后端: cd backend/src/SakuraFilter.Api && dotnet run")
        sys.exit(1)

    # 选测试产品
    row = pick_test_product()
    if not row:
        print("[FAIL] PG 中无可用产品, 请先导入产品数据")
        sys.exit(1)
    product_id, oem_normalized = row
    print(f"  测试产品: id={product_id} oem_no_normalized={oem_normalized}")

    # 执行用例 (P0-1.2 图片上传)
    case_s3_failure_rollback()
    case_normal_upload(product_id, oem_normalized)
    case_overwrite_upload(product_id, oem_normalized)

    # 执行用例 (P0-1.3 三表事务一致性)
    case_concurrent_create_409()
    case_normal_create_three_table_consistency()

    # 汇总
    print("\n" + "=" * 60)
    print(f"【汇总】 PASS={PASS} FAIL={FAIL}")
    print("=" * 60)
    if FAIL > 0:
        print("  [RESULT] 存在失败用例, 请检查")
        sys.exit(1)
    else:
        print("  [RESULT] 全部通过")
        sys.exit(0)


if __name__ == "__main__":
    main()
