"""
SakuraFilter 用户旅程测试 (P0 权限改造 Day 14)
================================================
覆盖 2 个角色的完整用户旅程:
  1. 游客 (Guest): 无 token, 验证可使用公开功能
     - 首页/搜索/详情/公开对比
     - 访问 /admin/* 应被踢回 /login (前端路由守卫)
     - 直接请求 /api/admin/* 应被后端鉴权中间件拒 (401)
  2. 管理员 (Admin): 完整登录, 验证可使用管理功能
     - 登录 + 自动跳回 redirect 目标
     - 后台产品/字典/ETL/对比

WHY 独立脚本 (不并入 _test_e2e_destructive):
  - 专注权限边界, 失败信号明确 (越权访问 vs 业务 Bug)
  - 可独立 CI 跑, 不依赖完整 ETL 状态
  - 产物可被人肉测复核 (TestRecorder 输出)
"""
import json
import time
import requests
import urllib.error
import urllib.request
from pathlib import Path
from playwright.sync_api import sync_playwright

BACKEND = "http://localhost:5148"
FRONTEND = "http://localhost:5173"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
ADMIN_USER = "admin"
ADMIN_PASS = "Admin@2026"

REPORT_PATH = Path(__file__).resolve().parent / "user_journey_report.json"
results = []


def record(scenario, check, status, expected="", actual="", evidence=""):
    """记录测试结果"""
    results.append({
        "scenario": scenario,
        "check": check,
        "status": status,
        "expected": expected,
        "actual": actual,
        "evidence": evidence,
    })
    icon = {"PASS": "[PASS]", "FAIL": "[FAIL]", "WARN": "[WARN]", "SKIP": "[SKIP]"}[status]
    print(f"  {icon} {check}")
    if status == "FAIL":
        print(f"      预期: {expected[:120]}")
        print(f"      实际: {actual[:120]}")


def curl(method, path, body=None, headers=None, timeout=10):
    """API 调用, 返回 (status_code, body)"""
    url = f"{BACKEND}{path}"
    data = json.dumps(body).encode("utf-8") if body is not None else None
    h = {"Content-Type": "application/json"}
    if headers:
        h.update(headers)
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", errors="replace")
    except Exception as e:
        return 0, str(e)


# ============================================================
# 场景 A: 游客旅程
# ============================================================
def test_guest_journey(page):
    """游客无需登录, 可使用查询/对比功能"""
    print("\n" + "=" * 70)
    print("  场景 A: 游客 (Guest) 用户旅程")
    print("=" * 70)

    # ---- A.1 公开搜索 ----
    print("\n[A.1] 游客访问公开搜索 /public/search (无 token)")
    try:
        page.goto(f"{FRONTEND}/public/search", wait_until="domcontentloaded", timeout=10000)
        time.sleep(1)
        url = page.url
        # 验证未被踢回 /login
        on_login = "/login" in url
        has_search = page.query_selector("input[placeholder*='OEM'], input[placeholder*='oem']") is not None
        record("A-游客", "公开搜索页可访问",
               "PASS" if (not on_login and has_search) else "FAIL",
               "URL 含 /public/search 且有搜索输入框",
               f"url={url} hasSearchInput={has_search}")
    except Exception as e:
        record("A-游客", "公开搜索页可访问", "FAIL", "页面加载", str(e))

    # ---- A.2 公开产品详情 ----
    print("\n[A.2] 游客访问产品详情 (无 token)")
    try:
        # 用一个已知的有效 OEM (AC 010323 是 seed 数据常见值)
        page.goto(f"{FRONTEND}/product/AC%20010323", wait_until="domcontentloaded", timeout=10000)
        time.sleep(2)
        url = page.url
        on_login = "/login" in url
        has_add_to_compare = page.query_selector("button:has-text('加入对比')") is not None
        has_query_alternatives = page.query_selector("button:has-text('查询替代')") is not None
        record("A-游客", "产品详情可访问",
               "PASS" if (not on_login) else "FAIL",
               "URL 保持 /product/* 不被踢到 /login",
               f"url={url}")
        record("A-游客", "产品详情含'加入对比'按钮",
               "PASS" if has_add_to_compare else "FAIL",
               "存在 加入对比 按钮",
               f"hasButton={has_add_to_compare}")
        record("A-游客", "产品详情含'查询替代'按钮",
               "PASS" if has_query_alternatives else "FAIL",
               "存在 查询替代 按钮",
               f"hasButton={has_query_alternatives}")
    except Exception as e:
        record("A-游客", "产品详情可访问", "FAIL", "页面加载", str(e))

    # ---- A.3 游客直接访问 /compare?ids=1,2,3 (公开对比) ----
    print("\n[A.3] 游客访问公开对比 /compare?ids=1,2,3 (无 token)")
    try:
        page.goto(f"{FRONTEND}/compare?ids=1,2,3", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        url = page.url
        on_login = "/login" in url
        on_admin_compare = "/admin/compare" in url
        # 公开对比页应有"产品对比"标题 + 至少一个数据行
        page_text = page.evaluate("() => document.body.innerText")
        has_title = "产品对比" in page_text
        has_data = "OEM 编号" in page_text
        record("A-游客", "公开对比页可访问",
               "PASS" if (not on_login and not on_admin_compare and has_title) else "FAIL",
               "URL=/compare, 不被踢到 /login 或 /admin/compare",
               f"url={url} hasTitle={has_title}")
        record("A-游客", "对比表格加载",
               "PASS" if has_data else "FAIL",
               "页面含 'OEM 编号' 字段行",
               f"hasData={has_data}")
    except Exception as e:
        record("A-游客", "公开对比页可访问", "FAIL", "页面加载", str(e))

    # ---- A.4 游客尝试访问 /admin/* 应被踢回 /login ----
    print("\n[A.4] 游客尝试访问后台 /admin/products 应被踢到 /login")
    try:
        page.goto(f"{FRONTEND}/admin/products", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        url = page.url
        on_login = "/login" in url
        record("A-游客", "后台产品页需登录",
               "PASS" if on_login else "FAIL",
               "URL 跳到 /login (路由守卫拦截)",
               f"url={url}")
    except Exception as e:
        record("A-游客", "后台产品页需登录", "FAIL", "页面跳 /login", str(e))

    # ---- A.5 游客尝试直接调管理 API 应 401 ----
    print("\n[A.5] 游客直接调 /api/admin/products (无 token) 应 401")
    code, body = curl("GET", "/api/admin/products?pageSize=1")
    record("A-游客", "匿名调管理 API 被拒",
           "PASS" if code in (401, 403) else "FAIL",
           "401/403 Unauthorized",
           f"code={code} body={body[:200]}")

    # ---- A.6 公开 /api/public/compare 匿名可访问 ----
    print("\n[A.6] 游客调 /api/public/compare?ids=1,2,3 应 200")
    code, body = curl("GET", "/api/public/compare?ids=1,2,3")
    parsed = None
    try:
        parsed = json.loads(body)
    except Exception:
        pass
    has_count = parsed.get("count", 0) > 0 if isinstance(parsed, dict) else False
    record("A-游客", "匿名公开对比 API 可用",
           "PASS" if code == 200 and has_count else "FAIL",
           "200 + count >= 1",
           f"code={code} count={parsed.get('count') if parsed else '?'}")

    # ---- A.7 公开 /api/public/search 匿名可访问 ----
    print("\n[A.7] 游客调 /api/public/search 应 200 (带搜索字段)")
    code, body = curl("GET", "/api/public/search?oemBrand=Bosch&pageSize=1")
    record("A-游客", "匿名公开搜索 API 可用",
           "PASS" if code == 200 else "FAIL",
           "200 (带 oemBrand 字段)",
           f"code={code}")

    # ---- A.8 公开 /api/public/product/{oem} 匿名可访问 ----
    print("\n[A.8] 游客调 /api/public/product/AC%20010323 应 200")
    code, body = curl("GET", "/api/public/product/AC%20010323")
    record("A-游客", "匿名产品详情 API 可用",
           "PASS" if code == 200 else "FAIL",
           "200",
           f"code={code}")


# ============================================================
# 场景 B: 管理员旅程
# ============================================================
def login_api():
    """通过 API 登录获取 JWT"""
    try:
        resp = requests.post(f"{BACKEND}/api/auth/login",
                             json={"username": ADMIN_USER, "password": ADMIN_PASS},
                             timeout=10)
        if resp.status_code == 200:
            return resp.json()
    except Exception as e:
        print(f"  [WARN] 登录失败: {e}")
    return None


def inject_auth_to_browser(page, login_resp):
    """将 JWT 注入浏览器 localStorage"""
    expires_at = int((time.time() + (login_resp.get("expiresIn") or 1800)) * 1000)
    payload = {
        "token": login_resp.get("accessToken") or login_resp.get("token"),
        "refreshToken": login_resp.get("refreshToken", ""),
        "user": login_resp.get("user"),
        "expiresAt": expires_at,
    }
    page.goto(f"{FRONTEND}/login", wait_until="domcontentloaded", timeout=10000)
    page.evaluate("""(payload) => {
        localStorage.setItem('sakura_admin_auth', JSON.stringify(payload));
        localStorage.removeItem('sakura_admin_token');
    }""", payload)
    page.reload(wait_until="domcontentloaded", timeout=10000)
    time.sleep(1)


def test_admin_journey(page, login_resp):
    """管理员: 登录 → 访问管理页 → 退出"""
    print("\n" + "=" * 70)
    print("  场景 B: 管理员 (Admin) 用户旅程")
    print("=" * 70)

    if not login_resp:
        record("B-管理员", "登录 API 成功", "FAIL", "200", "登录返回 None")
        return

    record("B-管理员", "登录 API 成功",
           "PASS",
           "200 + accessToken",
           f"user={login_resp.get('user', {}).get('username')}")

    token = login_resp.get("accessToken") or login_resp.get("token")
    user = login_resp.get("user", {})
    record("B-管理员", "登录响应含 user 对象",
           "PASS" if user and user.get("role") in ("admin", "operator") else "FAIL",
           "user.role ∈ {admin, operator}",
           f"role={user.get('role') if user else '?'}")

    # ---- B.1 注入 token → 访问后台产品 ----
    print("\n[B.1] 登录后访问 /admin/products")
    try:
        inject_auth_to_browser(page, login_resp)
        page.goto(f"{FRONTEND}/admin/products", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        url = page.url
        on_products = "/admin/products" in url and "/login" not in url
        # 验证页面含 "产品" 标题
        has_title = page.evaluate("() => document.body.innerText.includes('产品')")
        record("B-管理员", "后台产品页可访问",
               "PASS" if on_products else "FAIL",
               "URL 保持 /admin/products 不被踢回",
               f"url={url} hasTitle={has_title}")
    except Exception as e:
        record("B-管理员", "后台产品页可访问", "FAIL", "页面加载", str(e))

    # ---- B.2 后台产品 API 带 JWT 可访问 ----
    print("\n[B.2] 后台产品 API /api/admin/products (带 JWT)")
    code, body = curl("GET", "/api/admin/products?pageSize=1",
                      headers={"Authorization": f"Bearer {token}"})
    parsed = None
    try:
        parsed = json.loads(body)
    except Exception:
        pass
    has_total = parsed.get("total", 0) > 0 if isinstance(parsed, dict) else False
    record("B-管理员", "JWT 鉴权访问管理 API",
           "PASS" if code == 200 and has_total else "FAIL",
           "200 + total >= 1",
           f"code={code} total={parsed.get('total') if parsed else '?'}")

    # ---- B.3 登录后访问字典管理 ----
    print("\n[B.3] 访问字典管理 /admin/dict/oem-brands")
    try:
        page.goto(f"{FRONTEND}/admin/dict/oem-brands", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        url = page.url
        on_dict = "/admin/dict" in url and "/login" not in url
        record("B-管理员", "字典管理页可访问",
               "PASS" if on_dict else "FAIL",
               "URL 保持 /admin/dict/*",
               f"url={url}")
    except Exception as e:
        record("B-管理员", "字典管理页可访问", "FAIL", "页面加载", str(e))

    # ---- B.4 公开 /compare 仍可用 (登录后也能用) ----
    print("\n[B.4] 登录后访问 /compare?ids=1,2 仍可加载 (公开接口登录后也能用)")
    try:
        page.goto(f"{FRONTEND}/compare?ids=1,2", wait_until="networkidle", timeout=10000)
        time.sleep(2)
        url = page.url
        on_compare = "/compare" in url and "/login" not in url
        record("B-管理员", "公开对比页 (登录后)",
               "PASS" if on_compare else "FAIL",
               "URL 保持 /compare",
               f"url={url}")
    except Exception as e:
        record("B-管理员", "公开对比页 (登录后)", "FAIL", "页面加载", str(e))

    # ---- B.5 退出登录 ----
    print("\n[B.5] 退出登录后访问 /admin/products 应被踢回 /login")
    try:
        # 清理 localStorage 模拟退出
        page.evaluate("() => { localStorage.removeItem('sakura_admin_auth'); }")
        page.goto(f"{FRONTEND}/admin/products", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        url = page.url
        on_login = "/login" in url
        record("B-管理员", "退出后访问后台被拦截",
               "PASS" if on_login else "FAIL",
               "URL 跳到 /login",
               f"url={url}")
    except Exception as e:
        record("B-管理员", "退出后访问后台被拦截", "FAIL", "页面跳 /login", str(e))


# ============================================================
# 场景 C: 公开 API 边界检查
# ============================================================
def test_public_api_boundaries():
    """公开 API 必须支持 401 拒绝 + 正确排除下架"""
    print("\n" + "=" * 70)
    print("  场景 C: 公开 API 边界")
    print("=" * 70)

    # ---- C.1 /api/public/compare 空 ids 应 400 ----
    print("\n[C.1] /api/public/compare (空 ids) 应 400")
    code, body = curl("GET", "/api/public/compare")
    record("C-边界", "空 ids 拒绝",
           "PASS" if code == 400 else "FAIL",
           "400",
           f"code={code} body={body[:100]}")

    # ---- C.2 /api/public/compare 非法 id 应 400 ----
    print("\n[C.2] /api/public/compare (非法 id) 应 400")
    code, body = curl("GET", "/api/public/compare?ids=abc")
    record("C-边界", "非法 id 拒绝",
           "PASS" if code == 400 else "FAIL",
           "400",
           f"code={code} body={body[:100]}")

    # ---- C.3 /api/public/compare 超 6 个 id 应 400 ----
    print("\n[C.3] /api/public/compare (7 个 id) 应 400")
    code, body = curl("GET", "/api/public/compare?ids=1,2,3,4,5,6,7")
    record("C-边界", "超过 6 个 id 拒绝",
           "PASS" if code == 400 else "FAIL",
           "400",
           f"code={code} body={body[:100]}")

    # ---- C.4 /api/public/compare 不存在 id 应返回空 ----
    print("\n[C.4] /api/public/compare (不存在 id) 应返回 count=0")
    code, body = curl("GET", "/api/public/compare?ids=99999999")
    parsed = None
    try:
        parsed = json.loads(body)
    except Exception:
        pass
    has_zero = parsed.get("count") == 0 if isinstance(parsed, dict) else False
    record("C-边界", "不存在 id 返回空",
           "PASS" if code == 200 and has_zero else "FAIL",
           "200 + count=0",
           f"code={code} count={parsed.get('count') if parsed else '?'}")

    # ---- C.5 /api/auth/login 错误密码应 401 ----
    print("\n[C.5] /api/auth/login 错误密码应 401")
    code, body = curl("POST", "/api/auth/login",
                      body={"username": "admin", "password": "wrongpass"},
                      timeout=5)
    record("C-边界", "错误密码登录被拒",
           "PASS" if code in (401, 400) else "FAIL",
           "401/400",
           f"code={code} body={body[:100]}")


# ============================================================
# 主流程
# ============================================================
def main():
    print("=" * 70)
    print(" SakuraFilter 用户旅程测试 (P0 权限改造 Day 14)")
    print(f" Backend: {BACKEND}")
    print(f" Frontend: {FRONTEND}")
    print("=" * 70)

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context()
        page = context.new_page()
        try:
            # 1) 游客旅程 (不登录)
            test_guest_journey(page)

            # 2) 公开 API 边界
            test_public_api_boundaries()

            # 3) 管理员旅程
            print("\n[B.0] 管理员登录 (API)")
            login_resp = login_api()
            test_admin_journey(page, login_resp)
        finally:
            browser.close()

    # ===== 汇总 =====
    total = len(results)
    passed = sum(1 for r in results if r["status"] == "PASS")
    failed = sum(1 for r in results if r["status"] == "FAIL")
    warned = sum(1 for r in results if r["status"] == "WARN")
    skipped = sum(1 for r in results if r["status"] == "SKIP")

    print("\n" + "=" * 70)
    print(f" 总计 {total}: PASS={passed} FAIL={failed} WARN={warned} SKIP={skipped}")
    print("=" * 70)

    # 写报告
    report = {
        "generatedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "summary": {
            "total": total, "passed": passed, "failed": failed,
            "warned": warned, "skipped": skipped,
        },
        "results": results,
    }
    REPORT_PATH.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n  报告已写入: {REPORT_PATH}")

    if failed > 0:
        print(f"\n  [FAILED] {failed} 项检查未通过, 详见 {REPORT_PATH}")
        sys_exit_code = 1
    else:
        print(f"\n  [OK] 全部 {passed} 项检查通过")
        sys_exit_code = 0

    import sys
    sys.exit(sys_exit_code)


if __name__ == "__main__":
    main()
