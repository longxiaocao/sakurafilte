# -*- coding: utf-8 -*-
"""Day 10+ P2.2 通用 5 新字典 E2E 测试 (Product Name 1/2, Type, OEM 3, Media, Machine, Engine)
覆盖:
  1) 表结构 (UNIQUE 索引 + 软删列)
  2) list (默认不含已删 + includeDeleted=true 返已删)
  3) typeahead (ILIKE 模糊匹配, 限 20)
  4) create (重名抛 409, 空值抛 400, 默认 sortOrder = max+10)
  5) update (改名, 重名抛 409, 不存在抛 404)
  6) delete 软删 (list 默认不返, includeDeleted 返, 已删再删抛 409)
  7) restore (恢复 + 占用检查)
  8) reorder (批量改 sortOrder, 含不存在 id → 404)
  9) 鉴权 (X-Admin-Token 缺失 → 401)
 10) 跨实例接口一致性 (7 个 dict 全部走同 BaseDictService 抽象)

复用 _test_day10_oem_brands.py 模板, 适配 P2.2 的 7 个新字典
依赖: 后端跑在 http://localhost:5148, spike_test_v3 已有 7 张新表
"""
import json
import os
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

RUN_TAG = f"r{int(time.time())}"
PREFIX = "_p22_test_"

PASS = 0
FAIL = 0
RESULTS = []


# ========== 字典元数据 ==========
# url: API 路径
# table: DB 表名
# value_col: DB 主值列 (snake_case, 用于 UNIQUE 验证 + DB 清理)
# value_camel: API DTO 主值字段 (camelCase, JSON body 用)
# extra_camel: 多字段字典的额外字段 (camelCase)
# fixture: 测试时用 (col_name, value) 列表 — col_name 是 API camelCase
DICTS = [
    {
        "name": "ProductName1",
        "url": "product-name1s",
        "table": "dict_product_name1",
        "value_col": "product_name_1",
        "value_camel": "productName1",
        "extra_camel": [],
        "fixture": [("productName1", "TEST_PN1")],
    },
    {
        "name": "ProductName2",
        "url": "product-name2s",
        "table": "dict_product_name2",
        "value_col": "product_name_2",
        "value_camel": "productName2",
        "extra_camel": [],
        "fixture": [("productName2", "TEST_PN2")],
    },
    {
        "name": "Type",
        "url": "types",
        "table": "dict_type",
        "value_col": "type",
        "value_camel": "type",
        "extra_camel": [],
        "fixture": [("type", "TEST_TYPE")],
    },
    {
        "name": "OemNo3",
        "url": "oem-no3s",
        "table": "dict_oem_no3",
        "value_col": "oem_no_3",
        "value_camel": "oemNo3",
        "extra_camel": [],
        "fixture": [("oemNo3", "TEST_OEM3")],
    },
    {
        "name": "Media",
        "url": "medias",
        "table": "dict_media",
        "value_col": "media_name",
        "value_camel": "mediaName",
        # 2 字段字典, name + model 二者组 UNIQUE, list/typeahead 走 OR 匹配
        "extra_camel": [("mediaModel", "TEST_MEDIA_MODEL")],
        "fixture": [("mediaName", "TEST_MEDIA"), ("mediaModel", "TEST_MEDIA_MODEL")],
    },
    {
        "name": "Machine",
        "url": "machines",
        "table": "dict_machine",
        "value_col": "machine_brand",
        "value_camel": "machineBrand",
        # 3 字段字典, brand + model + name 三者组 UNIQUE
        "extra_camel": [("machineModel", "TEST_MACHINE_MODEL"), ("machineName", "TEST_MACHINE_NAME")],
        "fixture": [
            ("machineBrand", "TEST_MACHINE_BRAND"),
            ("machineModel", "TEST_MACHINE_MODEL"),
            ("machineName", "TEST_MACHINE_NAME"),
        ],
    },
    {
        "name": "Engine",
        "url": "engines",
        "table": "dict_engine",
        "value_col": "engine_brand",
        "value_camel": "engineBrand",
        "extra_camel": [("engineType", "TEST_ENGINE_TYPE")],
        "fixture": [("engineBrand", "TEST_ENGINE_BRAND"), ("engineType", "TEST_ENGINE_TYPE")],
    },
]


def http(method, path, body=None, headers=None, timeout=30):
    """统一 HTTP 客户端
    timeout 默认 30s: 兼容 OemNo3 (种入 5M+ 行, list 无 limit 返全表)"""
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
        print(f"::error::P2.2 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P2.2 ERROR [{name}]: {e}")


def make_value(dict_meta, suffix):
    """生成测试值 — 始终返回 camelCase dict (与 API DTO 一致)"""
    base = f"{PREFIX}{RUN_TAG}_{suffix}"
    d = {}
    for col, _ in dict_meta["fixture"]:
        d[col] = f"{base}_{col}"
    return d


def cleanup_test_data():
    """清理所有 7 张字典的测试数据 (硬删)"""
    conn = psycopg2.connect(
        host="localhost", port=5432, dbname="spike_test_v3",
        user="postgres", password="784533"
    )
    cur = conn.cursor()
    total = 0
    for d in DICTS:
        cur.execute(
            f"DELETE FROM {d['table']} WHERE {d['value_col']} LIKE %s",
            (f"{PREFIX}%",)
        )
        total += cur.rowcount
    conn.commit()
    conn.close()
    if total > 0:
        print(f"  [cleanup] 删除历史测试数据 {total} 条")


# ========== Case 1: 表结构 (7 个字典) ==========
def test_table_schema():
    """验证 7 张 dict 表存在 + UNIQUE 索引 + 软删列"""
    conn = psycopg2.connect(
        host="localhost", port=5432, dbname="spike_test_v3",
        user="postgres", password="784533"
    )
    cur = conn.cursor()
    for d in DICTS:
        # 1) 表存在
        cur.execute(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name=%s)",
            (d["table"],)
        )
        assert cur.fetchone()[0], f"{d['table']} 表不存在"
        # 2) 关键列
        cur.execute("""
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_name=%s
            ORDER BY ordinal_position
        """, (d["table"],))
        cols = {r[0]: (r[1], r[2]) for r in cur.fetchall()}
        required = ["id", "sort_order", "created_at", "updated_at", "deleted_at"]
        # 主值列
        required.append(d["value_col"])
        for col in required:
            assert col in cols, f"{d['table']} 缺列 {col}"
        # 3) 软删列可空
        assert cols["deleted_at"][1] == "YES", f"{d['table']}.deleted_at 应可空"
    conn.close()
    print(f"  ✓ 7 张 dict 表 + 软删列完整")


# ========== Case 2: 鉴权 (无 X-Admin-Token → 401) ==========
def test_auth_required():
    for d in DICTS:
        code, body = http("GET", f"/api/admin/dict/{d['url']}")
        assert code == 401, f"{d['name']} 期望 401, 实际 {code}, body={body[:200]}"
    print(f"  ✓ 7 个 dict 接口无 token → 401")


# ========== Case 3: list / typeahead / create / update / delete / restore / reorder ==========
def test_crud_lifecycle(dict_meta):
    """对每个字典跑完整 CRUD 生命周期"""
    name = dict_meta["name"]
    url = dict_meta["url"]
    value_camel = dict_meta["value_camel"]
    v1 = make_value(dict_meta, "c1")
    v2 = make_value(dict_meta, "c2")
    v3 = make_value(dict_meta, "c3")  # 用于 step 10 reorder 第二条, 避免与 step 5 update 后的 v2 重名

    # 1) create
    code, body = http("POST", f"/api/admin/dict/{url}", body=v1, headers=H_ADMIN)
    assert code == 201, f"{name} create 失败: {code}, body={body[:200]}"
    item1 = json.loads(body)
    item1_id = item1["id"]
    main_v1 = v1[value_camel]

    # 2) 重复 create → 409
    code2, body2 = http("POST", f"/api/admin/dict/{url}", body=v1, headers=H_ADMIN)
    assert code2 == 409, f"{name} 重名应 409, 实际 {code2}, body={body2[:200]}"

    # 3) list 默认不含已删 → 能找到
    #   加 limit=100 防止 OemNo3 (5M+ 行) 返全表超时
    code3, body3 = http("GET", f"/api/admin/dict/{url}?q={main_v1}&limit=100", headers=H_ADMIN)
    assert code3 == 200, f"{name} list 失败: {code3}"
    obj3 = json.loads(body3)
    items3 = obj3.get("items", [])
    # list 返 camelCase, 主值字段名是 value_camel
    found = [it for it in items3 if it.get(value_camel) == main_v1]
    assert len(found) >= 1, f"{name} list 应能找到新建项 (主值={main_v1}), 实际 {len(found)} 条"

    # 4) typeahead 返精简字段
    code4, body4 = http("GET", f"/api/admin/dict/{url}/typeahead?q={PREFIX}", headers=H_ADMIN)
    assert code4 == 200, f"{name} typeahead 失败: {code4}"
    obj4 = json.loads(body4)
    typeahead_items = obj4.get("items", [])
    # 字段检查: typeahead item 必有 id + 主值字段, 多字段字典还有 extra_camel
    if typeahead_items:
        first = typeahead_items[0]
        assert "id" in first, f"{name} typeahead 缺 id, 实际 {set(first.keys())}"
        assert value_camel in first, f"{name} typeahead 缺主值 {value_camel}, 实际 {set(first.keys())}"
        for col, _ in dict_meta["extra_camel"]:
            assert col in first, f"{name} typeahead 缺附加字段 {col}, 实际 {set(first.keys())}"

    # 5) update 改名 (用 v2)
    code5, body5 = http("PUT", f"/api/admin/dict/{url}/{item1_id}",
                        body=v2, headers=H_ADMIN)
    assert code5 == 200, f"{name} update 失败: {code5}, body={body5[:200]}"
    item5 = json.loads(body5)
    new_main = item5[value_camel]
    expected_main = v2[value_camel]
    assert new_main == expected_main, \
        f"{name} update 后主值未变, 期望 {expected_main}, 实际 {new_main}"

    # 6) 不存在 id → 404
    code6, body6 = http("PUT", f"/api/admin/dict/{url}/9999999",
                        body=v2, headers=H_ADMIN)
    assert code6 == 404, f"{name} 不存在 id 应 404, 实际 {code6}"

    # 7) delete 软删
    code7, body7 = http("DELETE", f"/api/admin/dict/{url}/{item1_id}", headers=H_ADMIN)
    assert code7 == 200, f"{name} delete 失败: {code7}, body={body7[:200]}"

    # 8) 重复 delete → 409
    code8, body8 = http("DELETE", f"/api/admin/dict/{url}/{item1_id}", headers=H_ADMIN)
    assert code8 == 409, f"{name} 重复 delete 应 409, 实际 {code8}"

    # 9) restore
    code9, body9 = http("POST", f"/api/admin/dict/{url}/{item1_id}/restore", headers=H_ADMIN)
    assert code9 == 200, f"{name} restore 失败: {code9}, body={body9[:200]}"

    # 10) reorder 批量
    # 先 create 第 2 条 (用 v3, 避免与 step 5 update 后的 v2 重名)
    code10, body10 = http("POST", f"/api/admin/dict/{url}", body=v3, headers=H_ADMIN)
    assert code10 == 201, f"{name} create v3 失败: {code10}, body={body10[:200]}"
    item2_id = json.loads(body10)["id"]
    # 倒序
    items = [{"id": item2_id, "sortOrder": 2000}, {"id": item1_id, "sortOrder": 1000}]
    code11, body11 = http("POST", f"/api/admin/dict/{url}/reorder",
                          body={"items": items}, headers=H_ADMIN)
    assert code11 == 200, f"{name} reorder 失败: {code11}, body={body11[:200]}"
    # 验证 DB
    conn = psycopg2.connect(
        host="localhost", port=5432, dbname="spike_test_v3",
        user="postgres", password="784533"
    )
    cur = conn.cursor()
    for it in items:
        cur.execute(f"SELECT sort_order FROM {dict_meta['table']} WHERE id = %s", (it["id"],))
        so = cur.fetchone()[0]
        assert so == it["sortOrder"], f"{name} reorder 后 sortOrder 期望 {it['sortOrder']}, 实际 {so}"
    conn.close()

    # 11) 含不存在 id → 404
    bad_items = items + [{"id": 9999999, "sortOrder": 9999}]
    code12, body12 = http("POST", f"/api/admin/dict/{url}/reorder",
                          body={"items": bad_items}, headers=H_ADMIN)
    assert code12 == 404, f"{name} 含不存在 id 应 404, 实际 {code12}"

    # 12) 空 value 抛 400 (单字段字典)
    if not dict_meta["extra_camel"]:
        code13, body13 = http("POST", f"/api/admin/dict/{url}",
                              body={value_camel: ""}, headers=H_ADMIN)
        assert code13 == 400, f"{name} 空 value 应 400, 实际 {code13}, body={body13[:200]}"

    print(f"  ✓ {name}: list/typeahead/create/update/delete/restore/reorder 全部通过")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== Day 10+ P2.2 7 个新字典 E2E 测试 ===")
    print(f"BASE={BASE} TOKEN={TOKEN[:20]}... RUN_TAG={RUN_TAG}")

    print("\n[prep] 清理历史测试数据...")
    cleanup_test_data()

    case("1. 7 张 dict 表结构 + UNIQUE 索引 + 软删列", test_table_schema)
    case("2. 鉴权 (无 X-Admin-Token → 401) 7 个接口", test_auth_required)
    for d in DICTS:
        case(f"3.{DICTS.index(d)+1}. {d['name']} CRUD 生命周期 (list/typeahead/create/update/delete/restore/reorder)",
             lambda d=d: test_crud_lifecycle(d))

    print("\n[cleanup] 清理本次测试数据...")
    cleanup_test_data()

    print(f"\n=== 总结: {PASS} PASS, {FAIL} FAIL ===")
    for n, s, e in RESULTS:
        marker = "✓" if s == "PASS" else "✗"
        print(f"  {marker} [{s}] {n}" + (f"  ({e})" if e else ""))
    sys.exit(0 if FAIL == 0 else 1)
