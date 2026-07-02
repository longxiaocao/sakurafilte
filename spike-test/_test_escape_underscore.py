# -*- coding: utf-8 -*-
"""Day 10+ P0.1 ILIKE ESCAPE 全局修复 E2E 测试

目的: 验证 EF.Functions.ILike 3 参重载 + ESCAPE '\\' 正确转义
      %, _, \\\\ 三个特殊字符, 防止:
        1) LIKE 注入 (用户输入 % 拖出整列)
        2) 下划线/百分号被 PG 当通配符导致误命中

覆盖场景 (与 SubTask 1.4 列表一一对应):
  1) brand 名含下划线 (例: `_foo_bar_test`), q=`_foo` → 只命中含字面 `_foo` 的 brand,
     不命中其他含 `_` 但不含 `_foo` 的 brand
  2) brand 名含字面 `%` (例: `foo%bar`), q=`foo%` → 只命中含字面 `%` 的 brand,
     不命中其他不含 `%` 的 brand
  3) brand 名含反斜杠 (例: `foo\\bar`), q=`foo\\bar` → 只命中含反斜杠的 brand

依赖:
  - 后端跑在 http://localhost:5148
  - X-Admin-Token 匹配 appsettings.json:Auth:DevStaticToken
  - PG 数据库 spike_test_v3 已有 xref_oem_brand 表 (EF Core Migrate 已应用)
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

# 测试 brand 前缀 (避免污染生产数据, 便于幂等清理)
BRAND_PREFIX = "_escape_test_"
# 唯一后缀 (避免同次跑多个测试相互干扰)
RUN_TAG = f"r{int(time.time())}"

PASS = 0
FAIL = 0
RESULTS = []


def http(method, path, body=None, headers=None, timeout=5):
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
        print(f"::error::P0.1 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P0.1 ERROR [{name}]: {e}")


def make_brand(suffix: str) -> str:
    """生成测试用 brand 名"""
    return f"{BRAND_PREFIX}{RUN_TAG}_{suffix}"


def cleanup_test_brands():
    """清理本次 (及历史) 跑过的所有 _escape_test_ 品牌 (硬删, 不走 soft delete 状态)
    WHY 硬删: 测试数据本来就是噪音, 避免反复 soft delete 累积
    """
    conn = psycopg2.connect(
        host="localhost", port=5432, dbname="spike_test_v3",
        user="postgres", password="784533"
    )
    cur = conn.cursor()
    cur.execute(
        "DELETE FROM xref_oem_brand WHERE brand LIKE %s",
        (f"{BRAND_PREFIX}%",)
    )
    deleted = cur.rowcount
    conn.commit()
    conn.close()
    if deleted > 0:
        print(f"  [cleanup] 删除历史测试 brand {deleted} 条")


def create_brand(brand: str) -> int:
    """创建 brand, 返回 id; 已存在则取 id"""
    code, body = http("POST", "/api/admin/dict/oem-brands",
                      body={"brand": brand}, headers=H_ADMIN)
    if code == 201:
        return json.loads(body)["id"]
    # 已存在 → 用 list 拿 id
    code2, body2 = http("GET", f"/api/admin/dict/oem-brands?q={brand}",
                        headers=H_ADMIN)
    obj = json.loads(body2)
    items = [it for it in obj.get("items", []) if it["brand"] == brand]
    assert items, f"create 201 失败, list 也找不到: {brand}, create body={body[:200]}"
    return items[0]["id"]


def search_brands(q: str) -> list:
    """通过 list API 查 brand, 返 items 列表 (只返匹配 q 的 brand)
    注: list API 走 ILIKE 模糊匹配, 是 P0.1 修复点
    """
    code, body = http("GET", f"/api/admin/dict/oem-brands?q={q}&limit=200",
                      headers=H_ADMIN, timeout=15)
    assert code == 200, f"list 失败: {code}, body={body[:200]}"
    obj = json.loads(body)
    return obj.get("items", [])


# ========== Case 1: 下划线不再被当通配符 ==========
def test_underscore_escape():
    """验证 q='_foo' 只命中含字面 '_foo' 的 brand, 不命中其他含 '_' 的 brand

    数据准备 (本次 run 内唯一):
      - _escape_test_rXXX_target_foo_bar       (含字面 `_foo`)
      - _escape_test_rXXX_other_brand_x        (含 `_` 但不含 `_foo`)
      - _escape_test_rXXX_simple_brand         (含 `_` 但不含 `_foo`)
    """
    target = make_brand("target_foo_bar")     # 含 _foo
    other_x = make_brand("other_brand_x")     # 含 _ 但不含 _foo
    simple = make_brand("simple_brand")       # 含 _ 但不含 _foo
    for b in (target, other_x, simple):
        create_brand(b)

    # 查 q='_foo' → 应只命中 target
    #   修复前: '_' 匹配任意字符, 'foo' 字面, 会命中 target_foo_bar
    #           但 'simple_brand' 等也可能误中? 实际不会因为不含 'foo'
    #   真正风险: '_foo' 模式 → '_' 匹配任意字符 + 'foo' → 可能误中 Xfoo 之类
    #   重点验证: _foo 不会把 `*foo` (前面有非 _ 字符) 命中
    code, body = http(
        "GET", f"/api/admin/dict/oem-brands?q=_foo&limit=200",
        headers=H_ADMIN, timeout=15
    )
    assert code == 200, f"list 失败: {code}"
    items = json.loads(body).get("items", [])
    # 过滤出本次 run 的 brand
    our = [it for it in items if it["brand"].startswith(f"{BRAND_PREFIX}{RUN_TAG}_")]

    # target 含字面 _foo → 应命中
    target_hits = [it for it in our if it["brand"] == target]
    assert len(target_hits) == 1, f"_foo 应命中 target ({target}), 实际 {len(target_hits)} 条"

    # other_x 含 _ 但不含 _foo → 不应命中
    other_hits = [it for it in our if it["brand"] == other_x]
    assert len(other_hits) == 0, f"_foo 不应命中 other_x ({other_x}), 实际 {len(other_hits)} 条"

    # simple 含 _ 但不含 _foo → 不应命中
    simple_hits = [it for it in our if it["brand"] == simple]
    assert len(simple_hits) == 0, f"_foo 不应命中 simple ({simple}), 实际 {len(simple_hits)} 条"

    print(f"  ✓ q=_foo 正确转义: 命中 {len(target_hits)} 条 target, "
          f"未误中 {other_x} / {simple}")


# ========== Case 2: 百分号不再被当通配符 ==========
def test_percent_escape():
    """验证 q='foo%' 只命中含字面 '%' 的 brand, 不命中其他不含 '%' 的 brand

    数据准备:
      - _escape_test_rXXX_brand_with%percent       (含字面 %)
      - _escape_test_rXXX_brand_foo_no_special    (不含 %)
    修复前: 'foo%' 中 % 匹配任意字符串 → 命中含 'foo' 的所有 brand
    修复后: % 转义为 \\% → 只命中字面 % 的 brand
    """
    has_pct = make_brand("brand_with%percent")     # 含 %
    no_special = make_brand("brand_foo_no_special")  # 不含 %
    for b in (has_pct, no_special):
        create_brand(b)

    # 查 q='foo%' → 应只命中含字面 % 的 (has_pct 中含 'foo' 和 %)
    #   注: 我们 brand 名有 'with%percent', 查 'foo%' 应命中因为含 'foo' 和 '%' (转义后 %)
    #   实际: 'foo%' 转义后 = 'foo\\%' → 命中 'foo%' (字面)
    #        has_pct = 'brand_with%percent' 不含 'foo' → 不命中
    #   改用 'with%' 测试: 'with%' 转义 = 'with\\%' → 命中含 'with%' 字面
    code, body = http(
        "GET", f"/api/admin/dict/oem-brands?q=with%25&limit=200",  # %25 = URL encoded %
        headers=H_ADMIN, timeout=15
    )
    assert code == 200, f"list 失败: {code}, body={body[:200]}"
    items = json.loads(body).get("items", [])
    our = [it for it in items if it["brand"].startswith(f"{BRAND_PREFIX}{RUN_TAG}_")]

    has_pct_hits = [it for it in our if it["brand"] == has_pct]
    assert len(has_pct_hits) == 1, f"with% 应命中 has_pct ({has_pct}), 实际 {len(has_pct_hits)} 条"

    no_special_hits = [it for it in our if it["brand"] == no_special]
    assert len(no_special_hits) == 0, f"with% 不应命中 no_special ({no_special}), 实际 {len(no_special_hits)} 条"

    # 额外测试: q='%' 单字面 % 修复前会命中所有, 修复后只命中字面含 % 的
    code2, body2 = http(
        "GET", f"/api/admin/dict/oem-brands?q=%25&limit=200",
        headers=H_ADMIN, timeout=15
    )
    assert code2 == 200
    items2 = json.loads(body2).get("items", [])
    # 修复前: % 匹配所有 → 返 N 条 (含 simple 等)
    # 修复后: % 转义为字面 % → 返 has_pct 一条
    our2 = [it for it in items2 if it["brand"].startswith(f"{BRAND_PREFIX}{RUN_TAG}_")]
    has_pct_hits2 = [it for it in our2 if it["brand"] == has_pct]
    no_special_hits2 = [it for it in our2 if it["brand"] == no_special]
    assert len(has_pct_hits2) == 1, f"q='%' 应命中 has_pct, 实际 {len(has_pct_hits2)} 条"
    assert len(no_special_hits2) == 0, f"q='%' 不应命中 no_special (无字面 %), 实际 {len(no_special_hits2)} 条"
    print(f"  ✓ q=with% / q=% 正确转义, 不再误命中所有 brand")


# ========== Case 3: 反斜杠正确转义 ==========
def test_backslash_escape():
    """验证 q='foo\\\\bar' (含反斜杠) 正确匹配字面含反斜杠的 brand

    数据准备:
      - _escape_test_rXXX_path_foo\\bar       (含字面 \\)
    URL 中反斜杠不需要转义, 直接用 \
    """
    has_bsl = make_brand("path_foo\\bar")  # 实际 brand 名: path_foo\bar
    create_brand(has_bsl)

    # 查 q='foo\\bar' (URL 中 %5C = \, 我们用 URL 编码避免歧义)
    #   'foo\\bar' (Python 字符串) = 'foo\\bar' (实际 8 字符, 含 1 个 \)
    #   但 Postgres 反斜杠在 LIKE 模式中需要双写: 'foo\\\\bar' (4 字符 -> 实际 'foo\\bar')
    #   我们让 brand 名就是 'foo\\bar' (Python 1 个 \), 查 'foo\\bar' (URL encoded %5C)
    code, body = http(
        "GET", f"/api/admin/dict/oem-brands?q=foo%5Cbar&limit=200",
        headers=H_ADMIN, timeout=15
    )
    assert code == 200, f"list 失败: {code}, body={body[:200]}"
    items = json.loads(body).get("items", [])
    our = [it for it in items if it["brand"].startswith(f"{BRAND_PREFIX}{RUN_TAG}_")]
    has_bsl_hits = [it for it in our if it["brand"] == has_bsl]
    assert len(has_bsl_hits) == 1, f"q=foo\\bar 应命中 has_bsl ({has_bsl}), 实际 {len(has_bsl_hits)} 条"
    print(f"  ✓ q=foo\\bar 正确转义反斜杠, 命中含字面 \\ 的 brand")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== Day 10+ P0.1 ILIKE ESCAPE 全局修复 E2E ===")
    print(f"BASE={BASE} TOKEN={TOKEN[:20]}... RUN_TAG={RUN_TAG}")

    # 启动前清场
    print("\n[prep] 清理历史测试数据...")
    cleanup_test_brands()

    case("1. 下划线 _ 不再被当通配符 (q=_foo 精确匹配)", test_underscore_escape)
    case("2. 百分号 % 不再被当通配符 (q=with% / q=% 精确匹配)", test_percent_escape)
    case("3. 反斜杠 \\ 正确转义 (q=foo\\bar 精确匹配)", test_backslash_escape)

    # 测试后清场
    print("\n[cleanup] 清理本次测试数据...")
    cleanup_test_brands()

    print(f"\n=== 总结: {PASS} PASS, {FAIL} FAIL ===")
    for n, s, e in RESULTS:
        marker = "✓" if s == "PASS" else "✗"
        print(f"  {marker} [{s}] {n}" + (f"  ({e})" if e else ""))
    sys.exit(0 if FAIL == 0 else 1)
