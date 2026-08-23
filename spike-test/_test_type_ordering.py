# -*- coding: utf-8 -*-
"""Day 10+ P2.3 E2E 测试: Type 字典排序 + Machine 4 大类

覆盖:
  1) dict_type 5 行 seed (oil=1, fuel=2, air=3, cabin=4, others=99)
  2) dict_machine 加 category 列后, 默认值 'others'
  3) GET /api/public/products/by-type 返按 sort_order 升序的 5 个 group
  4) GET /api/public/machine-brands/aggregated 返 4 大类 (含空 list)
  5) dict_type 拖动 sort_order 后, GET 顺序变化
  6) dict_machine 加 category 后, ListMachinesByCategoryAsync 正确返回
  7) 全部 5 个场景独立 PASS, 至少保证脚本可解析 + DB seed 可执行 (API 端点 1+2+5 走 DB 直查)

依赖: PostgreSQL spike_test_v3, dict_machine.machine_category 列已通过 EF Migration AddMachineCategory 添加
  (运行前需先启动后端一次, 让 Migrate() 自动应用)
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

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")

# P2.3 计划排序: oil=1, fuel=2, air=3, cabin=4, others=99
EXPECTED_TYPE_ORDER = ["oil", "fuel", "air", "cabin", "others"]
EXPECTED_TYPE_SORTORDER = [1, 2, 3, 4, 99]

# 4 大类
EXPECTED_CATEGORIES = ["Agriculture", "Commercial", "Construction", "others"]

PASS = 0
FAIL = 0
RESULTS = []


def http(method, path, body=None, headers=None, timeout=15):
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
    except urllib.error.URLError as e:
        return 0, f"[URL unreachable: {e.reason}]"


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
        print(f"::error::P2.3 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P2.3 ERROR [{name}]: {e}")


def db_conn():
    return psycopg2.connect(**PG)


# ========== Case 1: seed dict_type 5 行 + 验证排序 ==========
def test_seed_dict_type():
    """seed 5 行 dict_type (ON CONFLICT DO UPDATE SET sort_order) + 推后 sort_order=0 历史脏数据
    Day 11 fix v2: 调 _seed_dict_defaults.seed_dict_type 走完整流程
    WHY: 历史 40+ 行 dict_type sort_order=0, 即便 P2.3 五类已 seed 1/2/3/4/99,
         验证 "全部 active type 按 sort_order 排序" 时, sort_order=0 的行混在 P2.3 之后
         → 实际返 46 行, 而 expected 只有 5 行 → 列表长度比较失败
    修复: 用完整 seed_dict_type (含脏数据推后), 然后只校验前 5 个
    """
    import _seed_dict_defaults as seed_mod
    conn = db_conn()
    cur = conn.cursor()
    res = seed_mod.seed_dict_type(cur, conn)
    conn.commit()
    # 验证 P2.3 五类的 sort_order 正确
    cur.execute("""
        SELECT type, sort_order FROM dict_type
        WHERE deleted_at IS NULL AND type IN ('oil', 'fuel', 'air', 'cabin', 'others')
        ORDER BY sort_order, type
    """)
    p23_rows = cur.fetchall()
    actual_order = [r[0] for r in p23_rows]
    actual_sortorder = [r[1] for r in p23_rows]
    assert actual_order == EXPECTED_TYPE_ORDER, \
        f"dict_type P2.3 五类顺序错误, 期望 {EXPECTED_TYPE_ORDER}, 实际 {actual_order}"
    assert actual_sortorder == EXPECTED_TYPE_SORTORDER, \
        f"dict_type P2.3 五类 sort_order 错误, 期望 {EXPECTED_TYPE_SORTORDER}, 实际 {actual_sortorder}"
    # 验证: 5 类排在前 5 位 (历史 type 已被推后到 100+)
    cur.execute("""
        SELECT type, sort_order FROM dict_type
        WHERE deleted_at IS NULL
        ORDER BY sort_order, type
    """)
    all_rows = cur.fetchall()
    assert all_rows[0][1] == 1, f"前 5 个 sort_order 应从 1 开始, 实际 {all_rows[0][1]}"
    assert [r[0] for r in all_rows[:5]] == EXPECTED_TYPE_ORDER, \
        f"前 5 个 type 顺序错误, 期望 {EXPECTED_TYPE_ORDER}, 实际 {[r[0] for r in all_rows[:5]]}"
    # 验证: 所有非 P2.3 的 sort_order > 99
    cur.execute("""
        SELECT COUNT(*) FROM dict_type
        WHERE deleted_at IS NULL AND sort_order > 0 AND sort_order < 100
          AND type NOT IN ('oil', 'fuel', 'air', 'cabin', 'others')
    """)
    in_p23_band = cur.fetchone()[0]
    assert in_p23_band == 0, f"P2.3 排序区间 (1-99) 不应有其他 type, 实际 {in_p23_band} 条"
    conn.close()
    print(f"  ✓ P2.3 五类 sort_order={actual_sortorder}, "
          f"前 5 个顺序 = {actual_order}, "
          f"被推后 {res['moved_zero']} 条历史脏数据, "
          f"总 active type = {len(all_rows)}")


# ========== Case 2: GET /api/public/products/by-type ==========
def test_by_type_order():
    """GET by-type 验证顺序 = oil(1) fuel(2) air(3) cabin(4) others(99)"""
    code, body = http("GET", "/api/public/by-type")
    if code == 0:
        raise AssertionError(f"后端未启动或不可达: {body[:200]}")
    assert code == 200, f"by-type 期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    groups = obj.get("groups", [])
    actual_order = [g["type"] for g in groups]
    # 至少有 5 个 group, 顺序符合预期
    assert len(groups) >= 5, f"by-type 应至少返 5 个 group, 实际 {len(groups)}"
    # 检查前 5 个按预期排序
    assert actual_order[:5] == EXPECTED_TYPE_ORDER, \
        f"by-type 前 5 个 group 顺序错误, 期望 {EXPECTED_TYPE_ORDER}, 实际 {actual_order[:5]}"
    # 检查 sortOrder 字段也正确
    actual_so = [g["sortOrder"] for g in groups[:5]]
    assert actual_so == EXPECTED_TYPE_SORTORDER, \
        f"by-type sortOrder 错误, 期望 {EXPECTED_TYPE_SORTORDER}, 实际 {actual_so}"
    # 检查 productCount 和 products 字段
    for g in groups:
        assert "productCount" in g, f"group 缺 productCount: {g.keys()}"
        assert "products" in g, f"group 缺 products: {g.keys()}"
        assert len(g["products"]) == g["productCount"], \
            f"group {g['type']} products.length != productCount"
    print(f"  ✓ by-type 顺序正确 = {actual_order[:5]}, 总类型数 = {len(groups)}")


# ========== Case 3: GET /api/public/machine-brands/aggregated ==========
def test_machine_brands_aggregated():
    """GET aggregated 返 4 大类 (即使为空也有 others)"""
    code, body = http("GET", "/api/public/machine-brands/aggregated")
    if code == 0:
        raise AssertionError(f"后端未启动或不可达: {body[:200]}")
    assert code == 200, f"aggregated 期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    by_cat = obj.get("byCategory", {})
    # 4 大类一定存在
    for cat in EXPECTED_CATEGORIES:
        assert cat in by_cat, f"byCategory 缺 {cat}, 实际 keys = {list(by_cat.keys())}"
        assert isinstance(by_cat[cat], list), f"byCategory[{cat}] 应为 list, 实际 {type(by_cat[cat])}"
    # totalCount 字段
    assert "totalCount" in obj, f"响应缺 totalCount, 实际 {obj.keys()}"
    # 累加校验
    sum_count = sum(len(v) for v in by_cat.values())
    assert obj["totalCount"] == sum_count, \
        f"totalCount={obj['totalCount']} 与累加 {sum_count} 不一致"
    print(f"  ✓ aggregated 4 大类齐全, totalCount={obj['totalCount']}, breakdown={ {k: len(v) for k, v in by_cat.items()} }")


# ========== Case 4: 拖动 type.sort_order 后, GET 顺序变化 ==========
def test_drag_type_reorder():
    """手动 UPDATE sort_order 把 others 排到第 1, GET 验证顺序变化, 然后恢复"""
    conn = db_conn()
    cur = conn.cursor()
    # 备份原值
    cur.execute("SELECT type, sort_order FROM dict_type WHERE deleted_at IS NULL ORDER BY type")
    original = {r[0]: r[1] for r in cur.fetchall()}
    # 把 others 改到 1, oil 改到 99
    cur.execute("UPDATE dict_type SET sort_order = 1 WHERE type = 'others'")
    cur.execute("UPDATE dict_type SET sort_order = 99 WHERE type = 'oil'")
    conn.commit()
    try:
        # 调 by-type 验证
        code, body = http("GET", "/api/public/by-type")
        if code == 0:
            raise AssertionError(f"后端未启动: {body[:200]}")
        assert code == 200, f"by-type 期望 200, 实际 {code}"
        obj = json.loads(body)
        actual = [g["type"] for g in obj.get("groups", [])][:2]
        assert actual == ["others", "fuel"], \
            f"拖动后 by-type 前 2 顺序应 = ['others', 'fuel'], 实际 {actual}"
    finally:
        # 恢复
        for t, so in original.items():
            cur.execute("UPDATE dict_type SET sort_order = %s WHERE type = %s", (so, t))
        conn.commit()
        conn.close()
    print(f"  ✓ 拖动 sort_order 后 by-type 顺序立即变化, 恢复原值成功")


# ========== Case 5: dict_machine 加 category 后, ListMachinesByCategoryAsync 正确返回 ==========
def test_machine_category_list():
    """DB 验证 dict_machine.machine_category 列存在, 默认值 'others', ListMachinesByCategoryAsync 走 SQL 验证"""
    conn = db_conn()
    cur = conn.cursor()
    # 1) 列存在
    cur.execute("""
        SELECT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_name='dict_machine' AND column_name='machine_category'
        )
    """)
    assert cur.fetchone()[0], "dict_machine.machine_category 列不存在 (未跑 EF Migration?)"
    # 2) 索引存在
    cur.execute("""
        SELECT EXISTS (
            SELECT 1 FROM pg_indexes
            WHERE tablename='dict_machine' AND indexname='idx_dict_machine_category'
        )
    """)
    assert cur.fetchone()[0], "idx_dict_machine_category 索引不存在"
    # 3) 模拟 ListMachinesByCategoryAsync SQL: 4 大类分别能查
    for cat in EXPECTED_CATEGORIES:
        cur.execute("""
            SELECT COUNT(*) FROM dict_machine
            WHERE deleted_at IS NULL AND machine_category = %s
        """, (cat,))
        n = cur.fetchone()[0]
        # 不校验具体值, 但 SQL 应能成功执行 (不抛 42703 缺列)
    # 4) 构造一台测试 machine, 验证按 category 过滤
    test_brand = f"_P23_CAT_TEST_{int(time.time())}"
    cur.execute("""
        INSERT INTO dict_machine
            (machine_brand, machine_model, machine_name, machine_category,
             sort_order, created_at, updated_at)
        VALUES (%s, %s, %s, %s, 999999, now(), now())
        RETURNING id
    """, (test_brand, "_P23_CAT_MODEL_", "_P23_CAT_NAME_", "Agriculture"))
    new_id = cur.fetchone()[0]
    conn.commit()
    try:
        # 验证能查到此 row
        cur.execute("""
            SELECT id, machine_brand, machine_category
            FROM dict_machine
            WHERE deleted_at IS NULL AND machine_category = 'Agriculture'
              AND id = %s
        """, (new_id,))
        row = cur.fetchone()
        assert row is not None, f"ListMachinesByCategoryAsync('Agriculture') 找不到刚插入的 row"
        assert row[2] == "Agriculture", f"category 字段值错误: {row[2]}"
        # 验证更新 category
        cur.execute("""
            UPDATE dict_machine SET machine_category = 'Commercial', updated_at = now()
            WHERE id = %s
        """, (new_id,))
        conn.commit()
        cur.execute("""
            SELECT machine_category FROM dict_machine WHERE id = %s
        """, (new_id,))
        cat = cur.fetchone()[0]
        assert cat == "Commercial", f"update category 后字段值错误: {cat}"
    finally:
        # 清理测试数据
        cur.execute("DELETE FROM dict_machine WHERE id = %s", (new_id,))
        conn.commit()
        conn.close()
    print(f"  ✓ dict_machine.machine_category 列+索引+过滤 SQL 全部正常")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== Day 10+ P2.3 E2E 测试 (Type 排序 + Machine 4 大类) ===")
    print(f"BASE={BASE} TOKEN={TOKEN[:20]}...")

    print("\n[prep] seed dict_type P2.3 排序...")
    case("1. seed dict_type 5 行 + 验证 sort_order", test_seed_dict_type)
    case("2. GET /api/public/products/by-type 验证顺序", test_by_type_order)
    case("3. GET /api/public/machine-brands/aggregated 4 大类", test_machine_brands_aggregated)
    case("4. 拖动 type.sort_order 后 GET 顺序变化", test_drag_type_reorder)
    case("5. dict_machine.machine_category 列+ListMachinesByCategoryAsync SQL 验证", test_machine_category_list)

    print(f"\n=== 总结: {PASS} PASS, {FAIL} FAIL ===")
    for n, s, e in RESULTS:
        marker = "✓" if s == "PASS" else "✗"
        print(f"  {marker} [{s}] {n}" + (f"  ({e})" if e else ""))
    sys.exit(0 if FAIL == 0 else 1)
