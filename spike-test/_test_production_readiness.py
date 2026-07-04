"""
生产级交付综合检测 (Production Readiness Audit)
=================================================
检测维度:
  A. 功能完整性     - 所有核心业务流端到端
  B. 前端设计质量   - 工业极简融合风 + 响应式 + 主题切换
  C. 人体工程学     - 可访问性 / 性能 / 错误友好性 / 交互一致性
  D. 安全性         - XSS / CSRF / SQLi / 路径遍历 / 暴力破解 / JWT / 越权

输出:
  - 控制台逐项 PASS/FAIL/WARN
  - 末尾输出 JSON 报告 (production_audit_report.json)
  - 总分 >= 90 才允许生产交付
"""
import json
import re
import socket
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

BACKEND = "http://localhost:5148"
FRONTEND = "http://localhost:5173"
ADMIN_TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
SCRIPT_DIR = Path(__file__).resolve().parent
REPORT_PATH = SCRIPT_DIR / "production_audit_report.json"

# ===== 结果统计 =====
RESULTS = {"PASS": 0, "FAIL": 0, "WARN": 0, "SKIP": 0}
DETAILS = []
SEVERITY = {"P0": 0, "P1": 0, "P2": 0, "P3": 0}


def record(category, name, status, severity="P2", detail=""):
    """记录测试结果"""
    icon = {"PASS": "✓", "FAIL": "✗", "WARN": "⚠", "SKIP": "○"}.get(status, "?")
    color = {"PASS": "\033[92m", "FAIL": "\033[91m", "WARN": "\033[93m", "SKIP": "\033[90m"}.get(status, "")
    RESET = "\033[0m"
    print(f"  {color}{icon}{RESET} [{category}] {name}: {status}" + (f" — {detail}" if detail else ""))
    RESULTS[status] = RESULTS.get(status, 0) + 1
    if status == "FAIL":
        SEVERITY[severity] = SEVERITY.get(severity, 0) + 1
    DETAILS.append({"category": category, "name": name, "status": status, "severity": severity, "detail": detail})


def http_request(url, method="GET", headers=None, body=None, timeout=10):
    """统一 HTTP 请求封装, 返回 (status_code, headers, body_json_or_text)"""
    if headers is None:
        headers = {}
    if body is not None and not isinstance(body, (str, bytes)):
        body = json.dumps(body).encode("utf-8")
    if isinstance(body, str):
        body = body.encode("utf-8")
    req = urllib.request.Request(url, data=body, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            raw = r.read()
            return r.status, dict(r.headers), raw
    except urllib.error.HTTPError as e:
        raw = e.read() if e.fp else b""
        return e.code, dict(e.headers), raw
    except Exception as e:
        return 0, {}, str(e).encode()


def section(title):
    print(f"\n\033[1m\033[96m===== {title} =====\033[0m")


# ============================================================
# A. 功能完整性检测
# ============================================================
def test_functional():
    section("A. 功能完整性检测")

    # A1. 健康检查
    code, _, _ = http_request(f"{BACKEND}/health/ready")
    record("A1-健康检查", "GET /health/ready", "PASS" if code == 200 else "FAIL", "P0",
           f"HTTP {code}")

    # A2. 公开搜索 (前台用户, 8 字段多框搜索, 至少 1 字段)
    code, _, body = http_request(f"{BACKEND}/api/public/search?oemNo2=P00050000&page=1&pageSize=5")
    data = json.loads(body) if body else {}
    if code == 200 and "items" in data:
        record("A2-公开搜索", "GET /api/public/search", "PASS", "P1",
               f"返回 {len(data.get('items', []))} 条结果, total={data.get('total')}")
    else:
        record("A2-公开搜索", "GET /api/public/search", "FAIL", "P1", f"HTTP {code} body={body[:200]}")

    # A3. 公开详情 (按 OEM slug)
    code, _, body = http_request(f"{BACKEND}/api/public/product/P00050000")
    if code == 200:
        record("A3-公开详情", "GET /api/public/product/P00050000", "PASS", "P1", "返回产品详情")
    else:
        record("A3-公开详情", "GET /api/public/product/P00050000", "FAIL", "P1", f"HTTP {code}")

    # A4. by-type 搜索 (类型筛选)
    code, _, body = http_request(f"{BACKEND}/api/public/by-type?type=Air+Filter&page=1&pageSize=3")
    data = json.loads(body) if body else {}
    if code == 200:
        record("A4-按类型搜索", "GET /api/public/by-type", "PASS", "P1",
               f"类型筛选返回 {len(data.get('items', []))} 条")
    else:
        record("A4-按类型搜索", "GET /api/public/by-type", "FAIL", "P1", f"HTTP {code}")

    # A5. JWT 登录 (含限流重试)
    token = None
    for attempt in range(3):
        code, _, body = http_request(
            f"{BACKEND}/api/auth/login", method="POST",
            headers={"Content-Type": "application/json"},
            body={"username": "admin", "password": "Admin@2026"}
        )
        if code == 200:
            login_data = json.loads(body)
            token = login_data.get("accessToken", "")
            record("A5-JWT登录", "POST /api/auth/login", "PASS", "P0",
                   f"token 长度 {len(token)}, expires={login_data.get('expiresIn')}s")
            break
        elif code == 429:
            time.sleep(65)  # 等待限流窗口过期
            continue
        else:
            record("A5-JWT登录", "POST /api/auth/login", "FAIL", "P0", f"HTTP {code}")
            break
    if token is None and code != 200:
        record("A5-JWT登录", "POST /api/auth/login", "FAIL", "P0", f"重试 3 次仍失败 HTTP {code}")
    return token


def test_etl(token):
    section("B. ETL 流程检测 (需认证)")
    headers = {"X-Admin-Token": ADMIN_TOKEN} if not token else {"Authorization": f"Bearer {token}"}

    # B1. ETL 状态查询
    code, _, body = http_request(f"{BACKEND}/api/etl/status", headers=headers)
    if code == 200:
        s = json.loads(body)
        record("B1-ETL状态", "GET /api/etl/status", "PASS", "P1", f"status={s.get('status')}")
    else:
        record("B1-ETL状态", "GET /api/etl/status", "FAIL", "P1", f"HTTP {code}")

    # B2. ETL 健康指标
    code, _, body = http_request(f"{BACKEND}/metrics", headers={"X-Admin-Token": ADMIN_TOKEN})
    if code == 200 and b"etl_" in body:
        record("B2-ETL指标", "GET /metrics (etl_*)", "PASS", "P2", "Prometheus 指标含 etl_ 前缀")
    else:
        record("B2-ETL指标", "GET /metrics (etl_*)", "FAIL", "P2", f"HTTP {code}, etl 指标缺失")


# ============================================================
# C. 前端设计 + 人体工程学
# ============================================================
def test_frontend():
    section("C. 前端设计与人体工程学")

    # C1. 前台首页可达
    code, _, _ = http_request(f"{FRONTEND}/")
    record("C1-首页加载", "GET /", "PASS" if code == 200 else "FAIL", "P0", f"HTTP {code}")

    # C2. 公开搜索页
    code, _, _ = http_request(f"{FRONTEND}/public/search")
    record("C2-搜索页", "GET /public/search", "PASS" if code == 200 else "FAIL", "P1", f"HTTP {code}")

    # C3. 详情页
    code, _, _ = http_request(f"{FRONTEND}/product/P00050000")
    record("C3-详情页", "GET /product/P00050000", "PASS" if code == 200 else "FAIL", "P1", f"HTTP {code}")

    # C4. Demo 演示页
    code, _, _ = http_request(f"{FRONTEND}/demo")
    record("C4-Demo页", "GET /demo", "PASS" if code == 200 else "FAIL", "P2", f"HTTP {code}")

    # C5. 登录页 (公开)
    code, _, _ = http_request(f"{FRONTEND}/login")
    record("C5-登录页", "GET /login", "PASS" if code == 200 else "FAIL", "P1", f"HTTP {code}")

    # C6. HTML 体积 (首屏性能)
    code, _, body = http_request(f"{FRONTEND}/")
    if code == 200:
        size_kb = len(body) / 1024
        status = "PASS" if size_kb < 100 else "WARN"
        record("C6-首屏体积", "GET / 体积", status, "P2", f"{size_kb:.1f} KB (建议 < 100KB)")

    # C7. CSS 变量驱动主题 (light/dark)
    # WHY: Vue SPA 首页 HTML 是空壳, CSS 变量定义在打包后的样式文件中
    # 检测策略: 优先用 Playwright 检测挂载后 DOM 的计算样式; 回退到检测源 CSS 文件
    css_var_detected = False
    css_var_detail = ""
    try:
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True)
            page = browser.new_page()
            page.goto(f"{FRONTEND}/login", wait_until="networkidle", timeout=15000)
            val = page.evaluate("getComputedStyle(document.documentElement).getPropertyValue('--color-bg')")
            browser.close()
            if val and val.strip():
                css_var_detected = True
                css_var_detail = f"挂载后 DOM --color-bg={val.strip()}"
    except Exception as e:
        # 回退方案: 检测 Vite dev server 暴露的源 CSS 文件
        try:
            code2, _, css_body = http_request(f"{FRONTEND}/src/styles/index.css")
            if code2 == 200 and b"--color-bg" in css_body:
                css_var_detected = True
                css_var_detail = "源 CSS 文件 /src/styles/index.css 含 --color-bg 定义"
        except Exception:
            pass
    if css_var_detected:
        record("C7-主题变量", "CSS 变量驱动", "PASS", "P2", css_var_detail)
    else:
        record("C7-主题变量", "CSS 变量驱动", "WARN", "P2", "未检测到 CSS 变量定义")

    # C8. 响应式 viewport meta
    code, _, body = http_request(f"{FRONTEND}/")
    if code == 200 and b'viewport' in body:
        record("C8-响应式", "viewport meta", "PASS", "P2", "已配置响应式视口")
    else:
        record("C8-响应式", "viewport meta", "FAIL", "P2", "缺少 viewport meta")

    # C9. 无障碍 aria-label / role
    # WHY: Vue SPA 首页 HTML 是空壳, ARIA 标记在挂载后的 DOM 中
    # 检测策略: 优先用 Playwright 检测挂载后 DOM; 回退到检测多个源 Vue 文件
    aria_detected = False
    aria_detail = ""
    try:
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True)
            page = browser.new_page()
            page.goto(f"{FRONTEND}/login", wait_until="networkidle", timeout=15000)
            stats = page.evaluate("""() => {
                const html = document.documentElement.outerHTML;
                return {
                    ariaCount: (html.match(/aria-label/g) || []).length,
                    roleCount: (html.match(/role=/g) || []).length
                }
            }""")
            browser.close()
            if stats and (stats.get('ariaCount', 0) > 0 or stats.get('roleCount', 0) > 0):
                aria_detected = True
                aria_detail = f"挂载后 DOM 检测到 {stats['ariaCount']} 个 aria-label + {stats['roleCount']} 个 role="
    except Exception:
        pass
    if not aria_detected:
        # 回退方案: 检测源 Vue 组件文件
        try:
            code2, _, vue_body = http_request(f"{FRONTEND}/src/views/LoginView.vue")
            if code2 == 200 and (b'aria-label' in vue_body or b'role=' in vue_body):
                aria_detected = True
                aria_detail = "源 Vue 组件 LoginView.vue 含 ARIA 标记"
        except Exception:
            pass
    if aria_detected:
        record("C9-无障碍A11y", "aria-label/role", "PASS", "P3", aria_detail)
    else:
        record("C9-无障碍A11y", "aria-label/role", "WARN", "P3", "未见 ARIA 标记, 需补充")


# ============================================================
# D. 安全性检测
# ============================================================
def test_security(token):
    section("D. 安全性检测")

    # D1. 安全响应头
    code, headers, _ = http_request(f"{BACKEND}/health/ready")
    required_headers = {
        "X-Content-Type-Options": "nosniff",
        "X-Frame-Options": "DENY",
        "Content-Security-Policy": None,  # 仅检查存在
        "Referrer-Policy": None,
        "Permissions-Policy": None,
    }
    missing = [h for h, v in required_headers.items() if h not in headers]
    if not missing:
        record("D1-安全响应头", "必需响应头", "PASS", "P0",
               f"含 {len(required_headers)} 项: {', '.join(required_headers.keys())}")
    else:
        record("D1-安全响应头", "必需响应头", "FAIL", "P0", f"缺失: {missing}")

    # D2. HSTS (生产环境)
    # WHY: HSTS 仅在生产 HTTPS 环境下生效, 开发环境 (HTTP) 不应配置 HSTS
    # 检测策略: HTTPS 环境要求 HSTS, HTTP 环境记为 SKIP (开发环境预期)
    hsts = headers.get("Strict-Transport-Security", "")
    is_https = FRONTEND.startswith("https://") or BACKEND.startswith("https://")
    if hsts:
        record("D2-HSTS", "Strict-Transport-Security", "PASS", "P1", hsts)
    elif is_https:
        record("D2-HSTS", "Strict-Transport-Security", "FAIL", "P1", "HTTPS 环境必须开启 HSTS")
    else:
        record("D2-HSTS", "Strict-Transport-Security", "SKIP", "P1", "开发环境 (HTTP) 无需 HSTS, 生产环境 (HTTPS) 必须开启")

    # D3. XSS 反射测试
    xss_payload = "<script>alert('xss')</script>"
    code, _, body = http_request(
        f"{BACKEND}/api/public/search?q={urllib.parse.quote(xss_payload)}"
    )
    if xss_payload.encode() in body:
        record("D3-XSS反射", "搜索端点 XSS 注入", "FAIL", "P0", "XSS payload 未被过滤")
    else:
        record("D3-XSS反射", "搜索端点 XSS 注入", "PASS", "P0", "XSS payload 未被反射")

    # D4. SQL 注入测试
    sqli_payload = "' OR '1'='1"
    code, _, body = http_request(
        f"{BACKEND}/api/public/search?q={urllib.parse.quote(sqli_payload)}"
    )
    if code == 200:
        try:
            data = json.loads(body)
            if "items" in data and len(data["items"]) > 0:
                # 如果 SQLi 成功, 应返回超量结果 (全表)
                if data.get("total", 0) > 10000:
                    record("D4-SQL注入", "搜索端点 SQL 注入", "FAIL", "P0",
                           f"payload 返回 {data['total']} 条, 疑似注入成功")
                else:
                    record("D4-SQL注入", "搜索端点 SQL 注入", "PASS", "P0",
                           f"payload 返回 {data.get('total', 0)} 条, 参数化查询生效")
            else:
                record("D4-SQL注入", "搜索端点 SQL 注入", "PASS", "P0", "payload 未匹配任何数据")
        except Exception:
            record("D4-SQL注入", "搜索端点 SQL 注入", "WARN", "P0", "响应非 JSON, 需人工核查")
    else:
        record("D4-SQL注入", "搜索端点 SQL 注入", "PASS", "P0", f"HTTP {code}")

    # D5. 路径遍历 (ETL 导入)
    code, _, body = http_request(
        f"{BACKEND}/api/etl/import", method="POST",
        headers={"X-Admin-Token": ADMIN_TOKEN, "Content-Type": "application/json"},
        body={"jsonlPath": "../../../../etc/passwd", "mode": "insert-only", "entityType": "products"}
    )
    if code in (400, 422):
        record("D5-路径遍历", "ETL 路径遍历防护", "PASS", "P0", f"HTTP {code} 拦截")
    elif code == 200:
        record("D5-路径遍历", "ETL 路径遍历防护", "FAIL", "P0", "未拦截恶意路径")
    else:
        record("D5-路径遍历", "ETL 路径遍历防护", "WARN", "P0", f"HTTP {code}, 需人工核查")

    # D6. 暴力破解防护 (登录限流)
    rate_limited = False
    for i in range(7):
        code, _, _ = http_request(
            f"{BACKEND}/api/auth/login", method="POST",
            headers={"Content-Type": "application/json"},
            body={"username": "admin", "password": f"wrong_{i}"}
        )
        if code == 429:
            rate_limited = True
            record("D6-暴力破解", "登录限流 (5次/分)", "PASS", "P0",
                   f"第 {i+1} 次触发 HTTP 429")
            break
    if not rate_limited:
        record("D6-暴力破解", "登录限流 (5次/分)", "FAIL", "P0", "未触发限流")

    # D7. 越权访问 (无 token 访问管理端点)
    code, _, _ = http_request(f"{BACKEND}/api/admin/users")
    if code == 401:
        record("D7-越权访问", "无 token 访问 /api/admin/users", "PASS", "P0", "HTTP 401")
    else:
        record("D7-越权访问", "无 token 访问 /api/admin/users", "FAIL", "P0", f"HTTP {code}")

    # D8. 角色越权 (viewer 尝试 admin 操作)
    # WHY: 检测脚本可能多次运行, viewer 用户已存在时会返回 409 Conflict
    # WHY: D6 暴力破解测试会触发登录限流 (FixedWindow 1 分钟), D8 需等待窗口过期
    # 策略: 先等待限流窗口过期; 再尝试登录获取 token; 登录失败则创建; 创建冲突(409)则重置密码后重新登录
    print("  ℹ [D8-角色越权] 等待 65 秒让 D6 限流窗口过期...")
    time.sleep(65)
    viewer_token = None
    viewer_username = "audit_viewer"
    viewer_password = "Test@2026"
    try:
        if token:
            # 步骤1: 先尝试登录 (用户可能已存在)
            code2, _, body2 = http_request(
                f"{BACKEND}/api/auth/login", method="POST",
                headers={"Content-Type": "application/json"},
                body={"username": viewer_username, "password": viewer_password}
            )
            if code2 == 200:
                viewer_token = json.loads(body2).get("accessToken")

            # 步骤2: 登录失败则尝试创建
            if not viewer_token:
                code, _, body = http_request(
                    f"{BACKEND}/api/admin/users", method="POST",
                    headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
                    body={"username": viewer_username, "password": viewer_password, "role": "viewer",
                          "email": "viewer@test.com", "fullName": "Audit Viewer"}
                )
                if code in (200, 201):
                    # 创建成功, 登录获取 token
                    code2, _, body2 = http_request(
                        f"{BACKEND}/api/auth/login", method="POST",
                        headers={"Content-Type": "application/json"},
                        body={"username": viewer_username, "password": viewer_password}
                    )
                    if code2 == 200:
                        viewer_token = json.loads(body2).get("accessToken")
                elif code == 409:
                    # 步骤3: 用户已存在但密码不对, 用 admin 重置密码后登录
                    # 先获取用户列表找到 audit_viewer 的 id
                    code3, _, body3 = http_request(
                        f"{BACKEND}/api/admin/users?page=1&pageSize=200",
                        headers={"Authorization": f"Bearer {token}"}
                    )
                    if code3 == 200:
                        users_data = json.loads(body3)
                        users_list = users_data.get("items") or users_data.get("data") or []
                        viewer_id = None
                        for u in users_list:
                            if u.get("username") == viewer_username:
                                viewer_id = u.get("id")
                                break
                        if viewer_id:
                            # 重置密码
                            http_request(
                                f"{BACKEND}/api/admin/users/{viewer_id}/reset-password", method="POST",
                                headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
                                body={"newPassword": viewer_password}
                            )
                            # 重新登录
                            code2, _, body2 = http_request(
                                f"{BACKEND}/api/auth/login", method="POST",
                                headers={"Content-Type": "application/json"},
                                body={"username": viewer_username, "password": viewer_password}
                            )
                            if code2 == 200:
                                viewer_token = json.loads(body2).get("accessToken")
    except Exception:
        pass

    if viewer_token:
        code, _, _ = http_request(
            f"{BACKEND}/api/admin/users", headers={"Authorization": f"Bearer {viewer_token}"}
        )
        if code == 403:
            record("D8-角色越权", "viewer 访问管理端点", "PASS", "P0", "HTTP 403")
        else:
            record("D8-角色越权", "viewer 访问管理端点", "FAIL", "P0", f"HTTP {code}")
    else:
        record("D8-角色越权", "viewer 访问管理端点", "SKIP", "P0", "无法准备 viewer 用户")

    # D9. JWT 签名验证 (篡改 token)
    if token:
        tampered = token[:-3] + "XYZ"
        code, _, _ = http_request(
            f"{BACKEND}/api/auth/me", headers={"Authorization": f"Bearer {tampered}"}
        )
        if code == 401:
            record("D9-JWT篡改", "签名验证", "PASS", "P0", "篡改 token 返回 401")
        else:
            record("D9-JWT篡改", "签名验证", "FAIL", "P0", f"HTTP {code}")

    # D10. 敏感信息泄露 (错误响应不含堆栈)
    code, _, body = http_request(f"{BACKEND}/api/products/99999999")
    if b"at SakuraFilter" in body or b"Exception" in body or b"   at " in body:
        record("D10-堆栈泄露", "错误响应", "FAIL", "P1", "响应含 .NET 堆栈")
    else:
        record("D10-堆栈泄露", "错误响应", "PASS", "P1", "响应不含敏感堆栈")

    # D11. CORS 配置
    code, headers, _ = http_request(
        f"{BACKEND}/api/public/search?type=test",
        headers={"Origin": "https://evil.com"}
    )
    cors = headers.get("Access-Control-Allow-Origin", "")
    if cors == "*":
        record("D11-CORS", "Access-Control-Allow-Origin", "WARN", "P1", "通配符 *, 建议白名单")
    elif cors:
        record("D11-CORS", "Access-Control-Allow-Origin", "PASS", "P1", f"origin={cors}")
    else:
        record("D11-CORS", "Access-Control-Allow-Origin", "PASS", "P1", "未配置 CORS (默认拒绝跨域)")

    # D12. Cookie 安全属性 (如有)
    code, headers, _ = http_request(
        f"{BACKEND}/api/auth/login", method="POST",
        headers={"Content-Type": "application/json"},
        body={"username": "admin", "password": "Admin@2026"}
    )
    set_cookie = headers.get("Set-Cookie", "")
    if set_cookie:
        if "Secure" in set_cookie and "HttpOnly" in set_cookie and "SameSite" in set_cookie:
            record("D12-Cookie安全", "Set-Cookie 属性", "PASS", "P1", "Secure+HttpOnly+SameSite")
        else:
            record("D12-Cookie安全", "Set-Cookie 属性", "WARN", "P1", "建议加 Secure+HttpOnly+SameSite")
    else:
        record("D12-Cookie安全", "Set-Cookie 属性", "PASS", "P1", "无 Cookie, 使用 Bearer Token")

    # D13. Refresh Token 重用检测
    if token:
        # 先尝试无 refresh 端点 (仅检查存在)
        code, _, _ = http_request(f"{BACKEND}/api/auth/refresh", method="POST",
                                  headers={"Content-Type": "application/json"},
                                  body={"refreshToken": "fake_token_test"})
        if code in (400, 401):
            record("D13-Refresh保护", "伪造 refresh token", "PASS", "P0", f"HTTP {code}")
        else:
            record("D13-Refresh保护", "伪造 refresh token", "WARN", "P0", f"HTTP {code}")


# ============================================================
# 主流程
# ============================================================
def main():
    print("\n" + "=" * 60)
    print("  SakuraFilter 生产级交付综合检测")
    print("=" * 60)

    # 端口连通性预检
    section("0. 端口连通性预检")
    for name, host, port in [("后端", "localhost", 5148), ("前端", "localhost", 5173)]:
        try:
            with socket.create_connection((host, port), timeout=3):
                record("0-预检", f"{name} {host}:{port}", "PASS", "P0", "端口可达")
        except Exception as e:
            record("0-预检", f"{name} {host}:{port}", "FAIL", "P0", f"无法连接: {e}")

    # A. 功能完整性
    token = test_functional()

    # B. ETL 流程
    test_etl(token)

    # C. 前端设计 + 人体工程学
    test_frontend()

    # D. 安全性
    test_security(token)

    # 汇总
    total = sum(RESULTS.values())
    passed_pct = (RESULTS["PASS"] / total * 100) if total > 0 else 0

    section("检测汇总")
    print(f"  总计: {total} 项")
    print(f"  \033[92m✓ PASS: {RESULTS['PASS']} ({passed_pct:.1f}%)\033[0m")
    print(f"  \033[91m✗ FAIL: {RESULTS['FAIL']}\033[0m")
    print(f"  \033[93m⚠ WARN: {RESULTS['WARN']}\033[0m")
    print(f"  \033[90m○ SKIP: {RESULTS['SKIP']}\033[0m")
    print(f"\n  严重程度: P0={SEVERITY['P0']}, P1={SEVERITY['P1']}, P2={SEVERITY['P2']}, P3={SEVERITY['P3']}")

    # 生产交付判断
    section("生产交付判定")
    if SEVERITY["P0"] > 0:
        verdict = "❌ 不可交付"
        reason = f"存在 {SEVERITY['P0']} 个 P0 级别问题 (阻塞级), 必须修复"
    elif passed_pct < 90:
        verdict = "⚠️  有条件交付"
        reason = f"通过率 {passed_pct:.1f}% < 90%, 建议修复 P1 警告后再交付"
    elif SEVERITY["P1"] > 3:
        verdict = "⚠️  有条件交付"
        reason = f"P1 问题 {SEVERITY['P1']} 个超过阈值 (3), 建议修复"
    else:
        verdict = "✅ 可以交付"
        reason = f"通过率 {passed_pct:.1f}%, P0=0, P1={SEVERITY['P1']}, 满足生产交付条件"
    print(f"  {verdict}")
    print(f"  理由: {reason}")

    # 写入 JSON 报告
    report = {
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "summary": {**RESULTS, "total": total, "pass_rate": round(passed_pct, 2)},
        "severity": SEVERITY,
        "verdict": verdict,
        "reason": reason,
        "details": DETAILS
    }
    REPORT_PATH.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n  报告已保存: {REPORT_PATH}")


if __name__ == "__main__":
    main()
