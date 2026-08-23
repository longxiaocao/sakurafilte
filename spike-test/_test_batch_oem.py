# -*- coding: utf-8 -*-
"""P3.2 (Task 10) 批量 OEM 查询 (Excel 多行粘贴) E2E 测试

覆盖:
  1) POST /api/public/search/batch-oem 返 200 + 列表 (Basic)
  2) 100 OEM < 1s 返回 (性能)
  3) 含中文/斜杠/引号/空行/重复 全部健壮处理 (边界)
  4) 未命中的 OEM 仍返回 (Hit=false)
  5) 上限 501 触发 400 (异常)
  6) 空数组触发 400 (异常)
  7) DB 中 oem_2 数据抽样 (前置条件, 真实查询前置)
"""
import json
import os
import time
import urllib.request
import urllib.error
import sys
import psycopg2

BASE = "http://localhost:5148"
PG_CONF = dict(host="localhost", port=5432, dbname="spike_test_v3", user="postgres", password="784533")

# Day 9.12: 跨平台基准路径
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)

PASS = 0
FAIL = 0
SKIP_COUNT = 0
RESULTS = []


class SkipTest(Exception):
    """空数据库或环境不支持时跳过测试"""
    pass


def http(method, path, body=None, timeout=10):
    url = BASE + path
    h = {"Content-Type": "application/json"}
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
    global PASS, FAIL, SKIP_COUNT
    print(f"\n--- {name} ---")
    try:
        fn()
        PASS += 1
        RESULTS.append((name, "PASS", None))
        print(f"[PASS] {name}")
    except SkipTest as e:
        SKIP_COUNT += 1
        RESULTS.append((name, "SKIP", str(e)))
        print(f"[SKIP] {name}: {e}")
    except AssertionError as e:
        FAIL += 1
        RESULTS.append((name, "FAIL", str(e)))
        print(f"[FAIL] {name}: {e}")
        print(f"::error::Task 10 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::Task 10 ERROR [{name}]: {e}")


def db_sample_oem2(n=20):
    """从 products 表抽样 oem_2 字段 (非 null, 真实数据)"""
    c = psycopg2.connect(**PG_CONF)
    cur = c.cursor()
    cur.execute("""
        SELECT oem_2 FROM products
        WHERE oem_2 IS NOT NULL AND oem_2 <> ''
        ORDER BY id ASC
        LIMIT %s
    """, (n,))
    rows = [r[0] for r in cur.fetchall()]
    c.close()
    return rows


def db_product_count():
    c = psycopg2.connect(**PG_CONF)
    cur = c.cursor()
    cur.execute("SELECT count(*) FROM products")
    n = cur.fetchone()[0]
    c.close()
    return n


# ========== Case 1: POST /batch-oem 返 200 + 列表 ==========
def test_basic_post():
    """基本场景: 发送 3 个真实 oem_2, 期望 200 + 列表 + 每个 OEM 一条结果"""
    sample = db_sample_oem2(3)
    if not sample:
        raise SkipTest("DB 无 oem_2 数据, 跳过 (本地有数据时验证)")
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": sample})
    assert code == 200, f"期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    # 顶层字段
    for k in ("total", "hits", "miss", "results"):
        assert k in obj, f"响应缺字段 {k}: {list(obj.keys())}"
    # 总数 = 去重后 OEM 数 (sample 来自 DB 抽样, 必然 distinct)
    assert obj["total"] == len(sample), f"total={obj['total']} 期望 {len(sample)}"
    # 长度对齐
    assert len(obj["results"]) == obj["total"], "results 长度 != total"
    # 每条都有 oem + hit
    for r in obj["results"]:
        assert "oem" in r and "hit" in r, f"result 字段缺失: {r}"
    # 至少应有命中 (抽样的 oem_2 在 DB 里)
    assert obj["hits"] >= 1, f"抽样 oem_2 至少 1 命中, 实际 {obj['hits']}"
    # 命中条目的字段
    hit_one = next((r for r in obj["results"] if r["hit"]), None)
    assert hit_one is not None
    for k in ("productId", "oemBrand", "productName1", "oem2"):
        assert k in hit_one, f"命中结果缺字段 {k}: {hit_one}"
    print(f"  ✓ 3 个 OEM, 命中 {obj['hits']} / 未命中 {obj['miss']}")


# ========== Case 2: 100 OEM < 1s 返回 ==========
def test_perf_100_oems():
    """性能: 100 个混合 OEM (50 真实 + 50 不存在) 端到端 < 1s"""
    sample = db_sample_oem2(50)
    if not sample:
        raise SkipTest("DB 无 oem_2 数据, 跳过性能测试")
    # 补 50 个不存在的随机 OEM
    oems = sample + [f"NONEXISTENT-OEM-{i:04d}" for i in range(50)]
    t0 = time.perf_counter()
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": oems}, timeout=15)
    dt = time.perf_counter() - t0
    assert code == 200, f"期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    assert obj["total"] == 100, f"total={obj['total']} 期望 100"
    assert obj["hits"] + obj["miss"] == 100, "hits + miss 必须等于 total"
    # 性能断言
    assert dt < 1.0, f"100 OEM 耗时 {dt:.3f}s 超过 1s 阈值"
    print(f"  ✓ 100 OEM 耗时 {dt*1000:.1f} ms, 命中 {obj['hits']} / 未命中 {obj['miss']}")


# ========== Case 3: 边界处理 (中文/斜杠/引号/空行/重复) ==========
def test_edge_cases():
    """健壮性: 包含 中文/斜杠/引号/空行/重复 全部健壮处理, 不抛 500"""
    # 混合输入: 真实 OEM + 中文 + 斜杠 + 引号 + 空行 + 重复
    sample = db_sample_oem2(2)
    raw = []
    if sample:
        raw.extend(sample)
    raw.extend([
        "AB/CD/123",          # 斜杠
        '"OEN-123"',          # 带引号
        "滤清器 1142",         # 中文 + 数字 + 空格
        "OEN-123",            # 不带引号
        "OEM-LINE-1",         # 纯字母数字
        "",                   # 空行
        "   ",                # 空白
        "OEN-123",            # 重复 (与 4 同值)
    ])
    if not sample:
        # DB 无数据时, 至少验证解析不抛 500
        pass
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": raw})
    assert code == 200, f"期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    # 解析: 过滤空字符串 + 空白 + 去重
    expected = set(s.strip() for s in raw if s and s.strip())
    assert obj["total"] == len(expected), (
        f"去重后总数 {obj['total']} 期望 {len(expected)}, "
        f"去重集合 {expected}"
    )
    # 每条结果 oem 都在去重集合内
    result_oems = {r["oem"] for r in obj["results"]}
    assert result_oems == expected, (
        f"结果 oem 集合 {result_oems} != 期望 {expected}"
    )
    # 中文必须保留 (不当分隔符) — 用集合成员断言
    assert "滤清器 1142" in result_oems, "中文 OEM '滤清器 1142' 必须保留"
    # 斜杠必须保留
    assert "AB/CD/123" in result_oems, "斜杠 OEM 'AB/CD/123' 必须保留"
    # 引号: 前端 trim 后会保留引号, 后端按 oem_2 字段严格匹配, 故 hit=false
    quoted = next((r for r in obj["results"] if r["oem"] == '"OEN-123"'), None)
    assert quoted is not None, "OEN-123 引号 OEM 必须保留 (hit=false 预期)"
    assert quoted["hit"] is False, "OEN-123 带引号应当 hit=false (匹配严格)"
    print(f"  ✓ 边界: {len(raw)} 输入 → {obj['total']} 去重后, 中文/斜杠/引号 全部保留")


# ========== Case 4: 未命中仍返回 (Hit=false) ==========
def test_unmatched_returns():
    """未命中: 全部不存在的 OEM 仍返回 200 + hit=false, 不抛 404"""
    fake_oems = ["NOPE-001", "NOPE-002", "NOPE-003", "X/X/X", "不存在的"]
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": fake_oems})
    assert code == 200, f"期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    assert obj["total"] == 5
    assert obj["hits"] == 0
    assert obj["miss"] == 5
    assert all(r["hit"] is False for r in obj["results"]), "全部未命中"
    # 命中为 false 时, productId 等字段应不存在或为 null
    for r in obj["results"]:
        assert r.get("productId") is None, "未命中不应有 productId"
    print(f"  ✓ 5 个不存在的 OEM 全部 hit=false, 返回齐全")


# ========== Case 5: 上限 501 → 400 ==========
def test_too_many_oems():
    """边界: 超过 500 个 OEM → 400"""
    oems = [f"OEM-{i:04d}" for i in range(501)]
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": oems}, timeout=10)
    assert code == 400, f"期望 400, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    # 错误响应含 'error' 或 'detail'
    assert any(k in obj for k in ("error", "detail", "title")), (
        f"错误响应缺字段: {obj}"
    )
    print(f"  ✓ 501 OEM → 400 ({obj.get('error', obj.get('title', '?'))})")


# ========== Case 6: 空数组 → 400 ==========
def test_empty_oems():
    """边界: 空 oems 数组 → 400"""
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": []})
    assert code == 400, f"期望 400, 实际 {code}, body={body[:300]}"
    print(f"  ✓ 空数组 → 400")


# ========== Case 7: 500 个 OEM 正好上限, 200 + 全部未命中 ==========
def test_exact_500():
    """边界: 正好 500 个 OEM → 200 (上限内允许)"""
    oems = [f"EXACT-500-{i:04d}" for i in range(500)]
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": oems}, timeout=10)
    assert code == 200, f"期望 200, 实际 {code}, body={body[:300]}"
    obj = json.loads(body)
    assert obj["total"] == 500
    print(f"  ✓ 500 OEM 整 → 200 (上限通过)")


# ========== Case 8: 同一 OEM 重复输入应去重 ==========
def test_duplicate_input():
    """去重: 同一 OEM 多次出现, 后端响应只一条"""
    sample = db_sample_oem2(1)
    if not sample:
        raise SkipTest("DB 无 oem_2 数据, 跳过")
    oem = sample[0]
    code, body = http("POST", "/api/public/search/batch-oem",
                      body={"oems": [oem, oem, oem, oem, oem]})
    assert code == 200
    obj = json.loads(body)
    assert obj["total"] == 1, f"去重后 total={obj['total']} 期望 1"
    assert len(obj["results"]) == 1
    assert obj["results"][0]["oem"] == oem
    print(f"  ✓ 5 个相同 OEM → 去重 1 条")


# ========== Case 9: 前端 SearchView.vue 含 el-tabs 批量粘贴 UI ==========
def test_frontend_ui_present():
    """前端: SearchView.vue 含批量粘贴 Tab + 解析逻辑 + 表格"""
    vue_path = os.path.join(PROJECT_ROOT, "frontend", "src", "views", "SearchView.vue")
    assert os.path.exists(vue_path), f"SearchView.vue 不存在: {vue_path}"
    content = open(vue_path, encoding="utf-8").read()
    # 1. Tab 切换
    assert "el-tabs" in content, "SearchView.vue 缺 el-tabs 组件"
    assert "批量粘贴" in content, "SearchView.vue 缺 '批量粘贴' Tab label"
    # 2. 解析逻辑 (split /[\t\n,;]+/)
    assert "/[\\t\\n,;]+/" in content or "split" in content, "缺 split 解析逻辑"
    # 3. 去重
    assert "new Set(" in content, "缺 Set 去重"
    # 4. 表格列
    assert "OEM 编号" in content and "命中" in content and "OEM Brand" in content, "缺表格列定义"
    # 5. 进度条
    assert "el-progress" in content, "缺 el-progress 进度条"
    # 6. API 调用
    assert "batchOem" in content, "缺 searchApi.batchOem 调用"
    # 7. 中文/斜杠/引号在 placeholder 出现, 证明边界覆盖
    assert "滤清器" in content or "AB/CD" in content or "OEN-123" in content, (
        "缺边界案例 (中文/斜杠/引号) 示例"
    )
    print(f"  ✓ SearchView.vue 含 7 项必要 UI 元素")


# ========== Case 10: 前端 API 类型 + 客户端方法注册 ==========
def test_frontend_api_registered():
    """前端: api/index.ts 含 batchOem 方法 + types.ts 含 BatchOemRequest 类型"""
    api_index = os.path.join(PROJECT_ROOT, "frontend", "src", "api", "index.ts")
    api_types = os.path.join(PROJECT_ROOT, "frontend", "src", "api", "types.ts")
    for p in (api_index, api_types):
        assert os.path.exists(p), f"文件不存在: {p}"
    idx_content = open(api_index, encoding="utf-8").read()
    type_content = open(api_types, encoding="utf-8").read()
    assert "batchOem" in idx_content, "api/index.ts 缺 batchOem 方法"
    assert "/public/search/batch-oem" in idx_content, "api/index.ts 缺端点 URL"
    assert "BatchOemRequest" in type_content, "api/types.ts 缺 BatchOemRequest 类型"
    assert "BatchOemResponse" in type_content, "api/types.ts 缺 BatchOemResponse 类型"
    assert "BatchOemResult" in type_content, "api/types.ts 缺 BatchOemResult 类型"
    print(f"  ✓ 前端 API 类型 + 客户端方法已注册")


# ========== 主流程 ==========
if __name__ == "__main__":
    print(f"=== P3.2 (Task 10) 批量 OEM 粘贴查询 E2E ===")
    print(f"BASE={BASE}")
    # 前置: 检查 products 表是否有数据
    pcount = db_product_count()
    if pcount == 0:
        print(f"[WARN] products 表为空 ({pcount} 行), DB 抽样相关 case 将 SKIP")
    else:
        print(f"[INFO] products 表有 {pcount} 行")

    case("1. POST /batch-oem 基本流程 (200 + 列表)", test_basic_post)
    case("2. 100 OEM < 1s (性能)", test_perf_100_oems)
    case("3. 边界: 中文/斜杠/引号/空行/重复", test_edge_cases)
    case("4. 未命中仍返回 (Hit=false)", test_unmatched_returns)
    case("5. 501 OEM → 400 (上限)", test_too_many_oems)
    case("6. 空数组 → 400", test_empty_oems)
    case("7. 500 OEM 整 → 200 (上限通过)", test_exact_500)
    case("8. 重复输入去重", test_duplicate_input)
    case("9. 前端 SearchView.vue UI 完整", test_frontend_ui_present)
    case("10. 前端 API 注册完整", test_frontend_api_registered)

    print(f"\n=== 总结: {PASS} PASS, {FAIL} FAIL, {SKIP_COUNT} SKIP ===")
    for n, s, e in RESULTS:
        marker = "✓" if s == "PASS" else ("○" if s == "SKIP" else "✗")
        print(f"  {marker} [{s}] {n}" + (f"  ({e})" if e else ""))
    # SKIP 不影响 exit code
    sys.exit(0 if FAIL == 0 else 1)
