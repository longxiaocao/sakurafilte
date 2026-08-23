"""
SakuraFilter 按钮权限契约静态检查 (P0 权限改造 Day 14)
=========================================================

WHY 需要这个测试:
  之前的 "加入对比" 按钮让游客跳到 /admin/compare 需手输 ID, 是典型的
  "按钮功能 vs 用户权限不匹配" 的契约破坏. 仅靠用户旅程 (e2e) 难以
  一次性覆盖所有按钮 × 角色 的笛卡尔积, 需要专门的契约扫描.

测试范围 (页面 × 角色 笛卡尔积):
  - 公开页: /, /search, /public/search, /product/:oem, /compare
  - 公开按钮 (游客必须可见可用): 查询替代 / 加入对比 / 搜索 / 公开对比
  - 后台页: /admin/* (路由守卫 + JWT 鉴权)
  - 后台按钮 (未登录不可见, 登录后可见): 新增产品 / 批量对比 / 编辑 / 停售 / 恢复
  - 后台专属按钮 (仅 admin 角色): 用户管理

执行策略:
  1. 静态扫描前端 router/index.ts + AppHeader.vue + 关键 view 的 data-testid / aria-label
  2. 浏览器实测: 游客态 / 登录态访问, 断言按钮 DOM 存在性 + 可见性
  3. API 层契约: 每个后台按钮对应 API 必须 JWT 鉴权

退出码:
  - 0: 全部通过
  - 1: 有 FAIL
  - 2: 脚本本身异常
"""
import json
import re
import time
import urllib.error
import urllib.request
from pathlib import Path
from playwright.sync_api import sync_playwright, Page, BrowserContext

BACKEND = "http://localhost:5148"
FRONTEND = "http://localhost:5173"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
ADMIN_USER = "admin"
ADMIN_PASS = "Admin@2026"

REPORT_PATH = Path(__file__).resolve().parent / "button_contract_report.json"
results = []


# ============================================================
# 工具
# ============================================================
def record(role: str, page: str, button: str, status: str, expected: str, actual: str, evidence: str = ""):
    """记录契约检查结果"""
    results.append({
        "role": role,
        "page": page,
        "button": button,
        "status": status,
        "expected": expected,
        "actual": actual,
        "evidence": evidence,
    })
    icon = {"PASS": "✓", "FAIL": "✗", "WARN": "!", "SKIP": "·"}[status]
    line = f"  [{icon}] [{role}@{page}] {button} → {status}"
    if status == "FAIL":
        line += f"\n      预期: {expected[:100]}"
        line += f"\n      实际: {actual[:100]}"
    print(line)


def curl(method, path, body=None, headers=None, timeout=10):
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


def is_visible(page: Page, selector: str, timeout: int = 2000) -> bool:
    """DOM 存在 + 可见 (非 hidden, 非 display:none)"""
    try:
        loc = page.locator(selector)
        if loc.count() == 0:
            return False
        # 用 first().is_visible() 避免 strict mode 报错
        return loc.first.is_visible(timeout=timeout)
    except Exception:
        return False


def is_disabled(page: Page, selector: str) -> bool:
    try:
        loc = page.locator(selector)
        if loc.count() == 0:
            return False
        return loc.first.is_disabled()
    except Exception:
        return False


# ============================================================
# 静态扫描: 解析 router/index.ts 的 requireAuth 元数据
# ============================================================
def static_route_scan():
    """
    扫描前端源码, 验证每个 /admin/* 路由都标了 requireAuth: true
    WHY: 防止新增后台路由时遗漏 requireAuth 导致游客可访问
    """
    print("\n" + "=" * 70)
    print("  阶段 1: 静态扫描 — 路由元数据契约")
    print("=" * 70)

    router_path = Path(__file__).resolve().parent.parent / "frontend" / "src" / "router" / "index.ts"
    if not router_path.exists():
        record("STATIC", "router/index.ts", "存在性", "FAIL", "文件存在", "未找到")
        return
    text = router_path.read_text(encoding="utf-8")

    # 解析 path 块 + meta 块
    # 简化: 匹配 path: '/admin/xxx' 块内是否含 requireAuth: true
    pattern = re.compile(
        r"path:\s*['\"]([^'\"]+)['\"][\s\S]{0,400}?meta:\s*\{([\s\S]{0,400}?)\}",
        re.MULTILINE,
    )
    admin_routes = []
    for m in pattern.finditer(text):
        path, meta = m.group(1), m.group(2)
        if path.startswith("/admin") and not path.endswith("*"):
            has_auth = "requireAuth" in meta and re.search(r"requireAuth:\s*true", meta) is not None
            admin_routes.append((path, has_auth))

    # 已知后台路由 (从代码已知, 用于交叉验证)
    known_admin_routes = [
        "/admin/products", "/admin/products/new", "/admin/products/:id/edit",
        "/admin/etl", "/admin/users", "/change-password",
        "/admin/dict/oem-brands", "/admin/dict/product-name1s", "/admin/dict/product-name2s",
        "/admin/dict/types", "/admin/dict/oem-no3s", "/admin/dict/medias",
        "/admin/dict/machines", "/admin/dict/engines",
        "/admin/compare", "/admin/help", "/admin/perf",
    ]

    # 任何 /admin/* 路由必须 requireAuth
    missing = [p for p, has in admin_routes if not has]
    record("STATIC", "all", "/admin/* 路由 requireAuth 覆盖",
           "PASS" if not missing else "FAIL",
           "所有 /admin/* 路由均需 requireAuth: true",
           f"扫描到 {len(admin_routes)} 个 /admin 路由, 缺 requireAuth: {missing}",
           f"已知路由: {known_admin_routes}")


def static_public_route_scan():
    """验证公开页路由 (/, /search, /public/search, /product/:oem, /compare) 不含 requireAuth"""
    router_path = Path(__file__).resolve().parent.parent / "frontend" / "src" / "router" / "index.ts"
    text = router_path.read_text(encoding="utf-8")

    public_paths = ["/", "/search", "/public/search", "/product/:oem", "/compare", "/login", "/demo"]
    pattern = re.compile(
        r"path:\s*['\"]([^'\"]+)['\"][\s\S]{0,400}?meta:\s*\{([\s\S]{0,400}?)\}",
        re.MULTILINE,
    )
    leaks = []
    for m in pattern.finditer(text):
        path, meta = m.group(1), m.group(2)
        # 公开路由不应该 requireAuth (login 除外, login 是登录页本身就是公开的)
        if path in public_paths:
            if "requireAuth" in meta and re.search(r"requireAuth:\s*true", meta) is not None:
                leaks.append(path)
    record("STATIC", "all", "公开路由不应 requireAuth",
           "PASS" if not leaks else "FAIL",
           "公开路由 (游客可访问) 不应带 requireAuth: true",
           f"误标 requireAuth 的公开路由: {leaks}")


# ============================================================
# 浏览器实测: 按钮可见性契约
# ============================================================
def inject_auth_to_browser(context: BrowserContext, login_resp: dict):
    """将 JWT 注入 localStorage (admin 角色)"""
    expires_at = int((time.time() + (login_resp.get("expiresIn") or 1800)) * 1000)
    payload = {
        "token": login_resp.get("accessToken") or login_resp.get("token"),
        "refreshToken": login_resp.get("refreshToken", ""),
        "user": login_resp.get("user"),
        "expiresAt": expires_at,
    }
    # 通过初始化脚本注入, 后续所有页面自动生效
    context.add_init_script(f"""
        (() => {{
            try {{
                localStorage.setItem('sakura_admin_auth', JSON.stringify({json.dumps(payload)}));
            }} catch (e) {{}}
        }})();
    """)


def clear_auth_in_browser(context: BrowserContext):
    """通过 init script 强制清空 auth, 保证游客态"""
    context.add_init_script("""
        (() => {
            try {
                localStorage.removeItem('sakura_admin_auth');
                localStorage.removeItem('sakura_admin_token');
            } catch (e) {}
        })();
    """)


def test_guest_button_contracts(page: Page):
    """
    游客态按钮契约:
      - 公开页所有按钮必须可见可用
      - 后台页按钮不可见 (被路由守卫踢到 /login)
    """
    print("\n" + "=" * 70)
    print("  阶段 2A: 游客态按钮契约 (Guest Button Contract)")
    print("=" * 70)

    # ===== 公开搜索页 =====
    print("\n[1/5] /public/search 公开搜索页按钮")
    try:
        page.goto(f"{FRONTEND}/public/search", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        # 8 字段输入框 (按 label / placeholder 任意匹配)
        has_search_input = is_visible(page, "input[placeholder*='e.g. MANN']") or \
                           is_visible(page, "input[placeholder*='e.g. Caterpillar']") or \
                           is_visible(page, "input[placeholder*='OEM Brand']") or \
                           is_visible(page, "input[placeholder*='MANN']")
        # 搜索按钮文本
        has_search_btn = is_visible(page, "button:has-text('搜索')") or is_visible(page, "button:has-text('Search')")
        record("GUEST", "/public/search", "8 字段搜索输入框",
               "PASS" if has_search_input else "FAIL",
               "可见", f"visible={has_search_input}")
        record("GUEST", "/public/search", "搜索按钮",
               "PASS" if has_search_btn else "FAIL",
               "可见", f"visible={has_search_btn}")
    except Exception as e:
        record("GUEST", "/public/search", "页面加载", "FAIL", "加载成功", str(e))

    # ===== 产品详情页 =====
    print("\n[2/5] /product/:oem 产品详情页按钮 (P0 修复重点)")
    try:
        page.goto(f"{FRONTEND}/product/AC%20010323", wait_until="domcontentloaded", timeout=10000)
        time.sleep(2)
        has_query_alt = is_visible(page, "button:has-text('查询替代')")
        has_add_compare = is_visible(page, "button:has-text('加入对比')")
        record("GUEST", "/product/:oem", "查询替代 按钮",
               "PASS" if has_query_alt else "FAIL",
               "可见 (游客可滚动到替代 OEM 表)", f"visible={has_query_alt}")
        record("GUEST", "/product/:oem", "加入对比 按钮",
               "PASS" if has_add_compare else "FAIL",
               "可见 (跳 /compare?ids=<id>, 游客可用)", f"visible={has_add_compare}")
    except Exception as e:
        record("GUEST", "/product/:oem", "页面加载", "FAIL", "加载成功", str(e))

    # ===== 公开对比页 =====
    print("\n[3/5] /compare 公开对比页按钮")
    try:
        page.goto(f"{FRONTEND}/compare?ids=1,2,3", wait_until="networkidle", timeout=20000)
        time.sleep(3)
        # 工具条: 加入按钮 / 仅看差异 / 清空 / 打印 (按 input placeholder 区分, 兼容 el-input wrapper)
        has_join_input = is_visible(page, "input[placeholder*='产品 ID']")
        has_join_btn = is_visible(page, "button:has-text('加入')")
        # el-checkbox 渲染为 label.el-checkbox (内含隐藏 input + span)
        # 直接按文本节点定位更稳定
        has_only_diff = is_visible(page, "label:has-text('仅看差异')") or \
                        is_visible(page, ".el-checkbox:has-text('仅看差异')")
        has_clear = is_visible(page, "button:has-text('清空')")
        has_print = is_visible(page, "button:has-text('打印')")
        record("GUEST", "/compare", "加入产品 ID 输入框",
               "PASS" if has_join_input else "FAIL",
               "可见", f"visible={has_join_input}")
        record("GUEST", "/compare", "加入按钮",
               "PASS" if has_join_btn else "FAIL",
               "可见", f"visible={has_join_btn}")
        record("GUEST", "/compare", "仅看差异 复选框",
               "PASS" if has_only_diff else "FAIL",
               "可见", f"visible={has_only_diff}")
        record("GUEST", "/compare", "清空按钮",
               "PASS" if has_clear else "FAIL",
               "可见 (有产品时可点)", f"visible={has_clear}")
        record("GUEST", "/compare", "打印按钮",
               "PASS" if has_print else "FAIL",
               "可见 (有产品时可点)", f"visible={has_print}")
    except Exception as e:
        record("GUEST", "/compare", "页面加载", "FAIL", "加载成功", str(e))

    # ===== 顶栏: 公开 vs 后台 入口区分 =====
    print("\n[4/5] 顶栏入口: 游客应看到 进入后台 按钮, 不应看到 用户菜单")
    try:
        # 跳到任意公开页
        page.goto(f"{FRONTEND}/search", wait_until="domcontentloaded", timeout=10000)
        time.sleep(1)
        # 公开页应看到: 产品搜索 / OEM 查询 / 产品对比
        has_enter_admin = is_visible(page, "button:has-text('进入后台')") or \
                          is_visible(page, "button:has-text('进入后台登录')")
        # 不应看到用户下拉 (未登录)
        # 顶栏有 el-dropdown 包含 username 才会显示
        has_user_menu = page.query_selector("header .el-dropdown:has-text('admin')") is not None
        # 也不应该看到 admin 专属 nav (产品管理/字典/ETL/高级对比/性能/帮助/用户管理)
        admin_nav_visible = is_visible(page, "nav button:has-text('产品管理')") or \
                            is_visible(page, "nav button:has-text('字典管理')") or \
                            is_visible(page, "nav button:has-text('ETL 触发')")
        record("GUEST", "/search (顶栏)", "进入后台 按钮",
               "PASS" if has_enter_admin else "FAIL",
               "可见 (跳 /login)", f"visible={has_enter_admin}")
        record("GUEST", "/search (顶栏)", "用户菜单 (admin 下拉)",
               "PASS" if not has_user_menu else "FAIL",
               "不可见 (未登录)", f"visible={has_user_menu}")
        record("GUEST", "/search (顶栏)", "后台 nav (产品管理/字典/ETL)",
               "PASS" if not admin_nav_visible else "FAIL",
               "不可见 (路由守卫拦截)", f"visible={admin_nav_visible}")
    except Exception as e:
        record("GUEST", "/search (顶栏)", "页面加载", "FAIL", "加载成功", str(e))

    # ===== 尝试访问后台: 必须跳 /login =====
    print("\n[5/5] 游客访问 /admin/products 必须被踢回 /login (后台按钮不可见)")
    try:
        # 用 networkidle 确保 router guard 跑完
        page.goto(f"{FRONTEND}/admin/products", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        url = page.url
        kicked_to_login = "/login" in url
        # 后台按钮: 新增产品 / 批量对比 / 编辑 — 在 /login 页都不应可见
        has_new_btn = is_visible(page, "button:has-text('新增产品')")
        has_batch_compare = is_visible(page, "button:has-text('批量对比')")
        record("GUEST", "/admin/products", "路由守卫踢到 /login",
               "PASS" if kicked_to_login else "FAIL",
               "未登录访问 /admin/* 应跳 /login", f"url={url}")
        record("GUEST", "/admin/products", "新增产品 按钮",
               "PASS" if not has_new_btn else "FAIL",
               "不可见 (未登录)", f"visible={has_new_btn}")
        record("GUEST", "/admin/products", "批量对比 按钮",
               "PASS" if not has_batch_compare else "FAIL",
               "不可见 (未登录)", f"visible={has_batch_compare}")
    except Exception as e:
        record("GUEST", "/admin/products", "页面加载", "FAIL", "加载成功", str(e))


def test_admin_button_contracts(page: Page):
    """
    管理员态按钮契约:
      - 后台页所有按钮必须可见
      - 公开页按钮仍可用 (登录后也能用, 不影响)
    """
    print("\n" + "=" * 70)
    print("  阶段 2B: 管理员态按钮契约 (Admin Button Contract)")
    print("=" * 70)

    # ===== 后台产品页: 登录后所有管理按钮应可见 =====
    print("\n[1/4] /admin/products 后台产品管理页")
    try:
        page.goto(f"{FRONTEND}/admin/products", wait_until="networkidle", timeout=15000)
        time.sleep(3)
        url = page.url
        on_admin = "/admin/products" in url and "/login" not in url
        record("ADMIN", "/admin/products", "路由不被踢回",
               "PASS" if on_admin else "FAIL",
               "URL 保持 /admin/products", f"url={url}")

        # 核心按钮: 搜索 / 高级筛选 / 新增产品 / 批量对比 / 编辑
        has_search = is_visible(page, "button:has-text('搜索')")
        has_adv = is_visible(page, "button:has-text('高级筛选')")
        has_new = is_visible(page, "button:has-text('新增产品')")
        # 批量对比可能在没选中时禁用, 但应可见
        has_batch = is_visible(page, "button:has-text('批量对比')")
        record("ADMIN", "/admin/products", "搜索 按钮",
               "PASS" if has_search else "FAIL", "可见", f"v={has_search}")
        record("ADMIN", "/admin/products", "高级筛选 按钮",
               "PASS" if has_adv else "FAIL", "可见", f"v={has_adv}")
        record("ADMIN", "/admin/products", "新增产品 按钮",
               "PASS" if has_new else "FAIL", "可见 (管理操作)", f"v={has_new}")
        record("ADMIN", "/admin/products", "批量对比 按钮",
               "PASS" if has_batch else "FAIL",
               "可见 (无选中时禁用但可见)", f"v={has_batch}")
    except Exception as e:
        record("ADMIN", "/admin/products", "页面加载", "FAIL", "加载成功", str(e))

    # ===== 顶栏: 管理员应看到 用户菜单 + 后台 nav =====
    print("\n[2/4] 顶栏: 管理员应看到 用户菜单 (admin) + 后台 nav")
    try:
        page.goto(f"{FRONTEND}/admin/products", wait_until="domcontentloaded", timeout=10000)
        time.sleep(1)
        has_user_menu = is_visible(page, "header button:has-text('admin')")
        has_products_nav = is_visible(page, "nav button:has-text('产品管理')")
        has_dict_nav = is_visible(page, "nav button:has-text('字典管理')")
        has_etl_nav = is_visible(page, "nav button:has-text('ETL 触发')")
        has_users_nav = is_visible(page, "nav button:has-text('用户管理')")  # admin 专属
        has_perf_nav = is_visible(page, "nav button:has-text('性能')")
        record("ADMIN", "顶栏", "用户菜单 (含 admin)",
               "PASS" if has_user_menu else "FAIL", "可见", f"v={has_user_menu}")
        record("ADMIN", "顶栏", "产品管理 nav",
               "PASS" if has_products_nav else "FAIL", "可见", f"v={has_products_nav}")
        record("ADMIN", "顶栏", "字典管理 nav",
               "PASS" if has_dict_nav else "FAIL", "可见", f"v={has_dict_nav}")
        record("ADMIN", "顶栏", "ETL 触发 nav",
               "PASS" if has_etl_nav else "FAIL", "可见", f"v={has_etl_nav}")
        record("ADMIN", "顶栏", "用户管理 nav (仅 admin)",
               "PASS" if has_users_nav else "FAIL",
               "可见 (admin 角色专属)", f"v={has_users_nav}")
        record("ADMIN", "顶栏", "性能监控 nav",
               "PASS" if has_perf_nav else "FAIL", "可见", f"v={has_perf_nav}")
    except Exception as e:
        record("ADMIN", "顶栏", "页面加载", "FAIL", "加载成功", str(e))

    # ===== 用户管理页: 可见 =====
    print("\n[3/4] /admin/users 用户管理页 (admin 角色)")
    try:
        page.goto(f"{FRONTEND}/admin/users", wait_until="networkidle", timeout=15000)
        time.sleep(2)
        url = page.url
        on_users = "/admin/users" in url and "/login" not in url
        record("ADMIN", "/admin/users", "可访问",
               "PASS" if on_users else "FAIL",
               "URL 保持 /admin/users", f"url={url}")
    except Exception as e:
        record("ADMIN", "/admin/users", "页面加载", "FAIL", "加载成功", str(e))

    # ===== 公开页: 登录后按钮仍可用 =====
    print("\n[4/4] 登录后访问公开页 /product/:oem (按钮仍可用)")
    try:
        page.goto(f"{FRONTEND}/product/AC%20010323", wait_until="domcontentloaded", timeout=10000)
        time.sleep(2)
        has_query_alt = is_visible(page, "button:has-text('查询替代')")
        has_add_compare = is_visible(page, "button:has-text('加入对比')")
        record("ADMIN", "/product/:oem", "查询替代 按钮",
               "PASS" if has_query_alt else "FAIL", "可见 (登录后仍可用)", f"v={has_query_alt}")
        record("ADMIN", "/product/:oem", "加入对比 按钮",
               "PASS" if has_add_compare else "FAIL", "可见 (登录后仍可用)", f"v={has_add_compare}")
    except Exception as e:
        record("ADMIN", "/product/:oem", "页面加载", "FAIL", "加载成功", str(e))


# ============================================================
# API 层契约: 每个后台按钮对应 API 必须鉴权
# ============================================================
def test_api_contracts():
    """
    API 契约: 后台按钮触发的 API 必须 401 (无 token) / 200 (有 token)
    WHY: 即使前端隐藏了按钮, 后端 API 仍可能被直接调用, 越权访问.
    """
    print("\n" + "=" * 70)
    print("  阶段 3: API 层契约 (后端鉴权)")
    print("=" * 70)

    # 后台 API 列表 (与后台按钮一一对应)
    admin_apis = [
        ("GET",  "/api/admin/products?pageSize=1",     "后台产品列表"),
        ("GET",  "/api/admin/users?pageSize=1",         "后台用户列表"),
        ("GET",  "/api/admin/etl/progress",             "ETL 进度"),
        ("GET",  "/api/admin/dict/oem-brands?pageSize=1", "字典 OEM 品牌"),
        ("GET",  "/api/admin/audit/login?pageSize=1",   "登录审计"),
    ]

    for method, path, label in admin_apis:
        # 无 token 应 401
        code, body = curl(method, path, timeout=5)
        record("GUEST", "API", f"{label} ({method} {path}) 匿名",
               "PASS" if code in (401, 403) else "FAIL",
               "401/403 (无 token 拒绝)", f"code={code}")

    # 公开 API 无 token 应 200
    public_apis = [
        # 必须提供至少 1 个搜索字段, 否则 API 主动返回 400 (业务规则)
        ("GET", "/api/public/search?oemBrand=Bosch&pageSize=1", "公开搜索 (带字段)"),
        ("GET", "/api/public/product/AC%20010323",               "公开产品详情"),
        ("GET", "/api/public/compare?ids=1,2,3",                 "公开对比"),
    ]
    for method, path, label in public_apis:
        code, body = curl(method, path, timeout=5)
        record("GUEST", "API", f"{label} ({method} {path}) 匿名",
               "PASS" if code == 200 else "FAIL",
               "200 (游客可访问)", f"code={code}")


# ============================================================
# 主流程
# ============================================================
def main():
    print("=" * 70)
    print(" SakuraFilter 按钮权限契约静态检查 (P0 权限改造 Day 14)")
    print(f" Backend: {BACKEND}")
    print(f" Frontend: {FRONTEND}")
    print("=" * 70)

    # 阶段 1: 静态扫描源码契约
    static_route_scan()
    static_public_route_scan()

    # 阶段 2 + 3: 浏览器实测 + API 契约
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)

        # --- 2A: 游客态 (无 token) ---
        ctx_guest = browser.new_context()
        clear_auth_in_browser(ctx_guest)
        page_guest = ctx_guest.new_page()
        try:
            test_guest_button_contracts(page_guest)
            test_api_contracts()
        finally:
            ctx_guest.close()

        # --- 2B: 管理员态 (注入 JWT) ---
        import requests
        try:
            login_resp = requests.post(
                f"{BACKEND}/api/auth/login",
                json={"username": ADMIN_USER, "password": ADMIN_PASS},
                timeout=10,
            ).json()
        except Exception as e:
            print(f"  [WARN] 登录失败: {e}, 跳过 admin 测试")
            login_resp = None

        if login_resp and (login_resp.get("accessToken") or login_resp.get("token")):
            ctx_admin = browser.new_context()
            inject_auth_to_browser(ctx_admin, login_resp)
            page_admin = ctx_admin.new_page()
            try:
                test_admin_button_contracts(page_admin)
            finally:
                ctx_admin.close()
        else:
            record("ADMIN", "all", "登录 API 成功", "FAIL", "200 + token", "登录返回空")

        browser.close()

    # ===== 汇总 =====
    total = len(results)
    passed = sum(1 for r in results if r["status"] == "PASS")
    failed = sum(1 for r in results if r["status"] == "FAIL")
    warned = sum(1 for r in results if r["status"] == "WARN")
    skipped = sum(1 for r in results if r["status"] == "SKIP")

    print("\n" + "=" * 70)
    print(f" 汇总: 总 {total} 项, PASS={passed} FAIL={failed} WARN={warned} SKIP={skipped}")
    print("=" * 70)

    if failed > 0:
        print("\n  失败明细:")
        for r in results:
            if r["status"] == "FAIL":
                print(f"    ✗ [{r['role']}@{r['page']}] {r['button']}")
                print(f"        预期: {r['expected'][:80]}")
                print(f"        实际: {r['actual'][:80]}")

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

    import sys
    sys.exit(1 if failed > 0 else 0)


if __name__ == "__main__":
    main()
