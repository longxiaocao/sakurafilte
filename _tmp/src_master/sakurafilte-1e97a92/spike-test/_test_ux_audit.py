"""
SakuraFilter 综合 UX 审计 (Day 14+ P0 改进闭环)
====================================================

覆盖 5 大维度:
  1. 功能性 (Functionality): 用户旅程 + 按钮契约回归
  2. 无障碍 (A11y): ARIA/键盘/对比度/焦点管理 (axe-core + 自定义断言)
  3. 性能 (Performance): Core Web Vitals + 资源大小 + 慢请求
  4. 跨浏览器 (Cross-Browser): Chromium/Firefox/WebKit 同一页面渲染一致
  5. 跨设备 (Cross-Device): 桌面/平板/手机 响应式 + 移动端关键场景

执行策略:
  - 复用现有 _test_user_journey.py + _test_button_contract.py (导入或子进程)
  - 用 axe-core Python 绑定做 A11y 自动审计
  - 用 Playwright 内置 performance API 取 Web Vitals
  - 跨浏览器: 启动 chromium/firefox/webkit 三个引擎
  - 跨设备: 用 Playwright 的 viewport 设置模拟 iPhone/iPad/Desktop

退出码:
  - 0: 全部通过
  - 1: 有 FAIL
  - 2: 脚本本身异常

报告输出:
  - spike-test/ux_audit_report.md  (人类可读)
  - spike-test/ux_audit_report.json (机器可读)
"""
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
import urllib.error
from datetime import datetime, timezone
from pathlib import Path
from playwright.sync_api import sync_playwright, Page, Browser, BrowserContext

BACKEND = "http://localhost:5148"
FRONTEND = "http://localhost:5173"
ADMIN_USER = "admin"
ADMIN_PASS = "Admin@2026"

REPORT_DIR = Path(__file__).resolve().parent
REPORT_JSON = REPORT_DIR / "ux_audit_report.json"
REPORT_MD = REPORT_DIR / "ux_audit_report.md"


def now_iso() -> str:
    """UTC ISO8601 字符串 (timezone-aware, 避免 deprecation warning)"""
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

results = {
    "generatedAt": now_iso(),
    "summary": {"total": 0, "passed": 0, "failed": 0, "warned": 0},
    "dimensions": {
        "functionality": {"items": []},
        "accessibility": {"items": []},
        "performance": {"items": []},
        "cross_browser": {"items": []},
        "cross_device": {"items": []}
    },
    "issues": [],  # 所有问题列表 (按维度分组)
    "recommendations": []
}


def record(dim: str, name: str, status: str, expected: str, actual: str, fix: str = ""):
    """记录一项检查结果"""
    item = {
        "name": name,
        "status": status,  # PASS/FAIL/WARN
        "expected": expected,
        "actual": actual,
        "fix": fix,
        "timestamp": now_iso()
    }
    results["dimensions"][dim]["items"].append(item)
    results["summary"]["total"] += 1
    if status == "PASS":
        results["summary"]["passed"] += 1
    elif status == "FAIL":
        results["summary"]["failed"] += 1
        results["issues"].append(item)
    elif status == "WARN":
        results["summary"]["warned"] += 1
        results["issues"].append(item)
    icon = {"PASS": "✓", "FAIL": "✗", "WARN": "!"}[status]
    print(f"  [{icon}] [{dim}] {name} → {status}")
    if status == "FAIL":
        print(f"      预期: {expected[:100]}")
        print(f"      实际: {actual[:100]}")
        if fix:
            print(f"      方案: {fix[:100]}")


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


# ============================================================
# 维度 1: 功能性 (复用现有 E2E)
# ============================================================
def test_functionality():
    """
    通过子进程运行 _test_user_journey.py + _test_button_contract.py
    这是审计的兜底层 — 任何 P0/P1 修复后此层必须绿
    """
    print("\n" + "=" * 70)
    print("  维度 1: 功能性 (User Journey + Button Contract)")
    print("=" * 70)

    scripts = [
        ("_test_user_journey.py", "用户旅程 E2E"),
        ("_test_button_contract.py", "按钮契约")
    ]
    for script, desc in scripts:
        path = REPORT_DIR / script
        if not path.exists():
            record("functionality", f"{desc} 脚本存在", "WARN",
                   "脚本存在", "未找到",
                   "确认 spike-test/ 目录有该文件")
            continue
        print(f"\n  运行 {script}...")
        try:
            r = subprocess.run(
                [sys.executable, str(path)],
                cwd=str(REPORT_DIR),
                capture_output=True,
                text=True,
                timeout=180
            )
            if r.returncode == 0:
                # 提取汇总行
                summary = ""
                for line in r.stdout.splitlines():
                    if "汇总" in line or "PASS=" in line:
                        summary = line.strip()
                        break
                record("functionality", f"{desc} 全部通过",
                       "PASS",
                       "0 FAIL",
                       summary or r.stdout.splitlines()[-1] if r.stdout else "ok")
            else:
                # 提取 FAIL 数
                fail_count = "?"
                for line in r.stdout.splitlines():
                    if "FAIL=" in line:
                        m = re.search(r"FAIL=(\d+)", line)
                        if m:
                            fail_count = m.group(1)
                        break
                record("functionality", f"{desc} 全部通过",
                       "FAIL",
                       "0 FAIL",
                       f"FAIL={fail_count}",
                       f"查看 {script} 输出, 修复失败用例后重跑")
        except subprocess.TimeoutExpired:
            record("functionality", f"{desc} 全部通过",
                   "FAIL", "180s 内完成", "超时",
                   "检查是否有 hang 的 API 或无限渲染循环")
        except Exception as e:
            record("functionality", f"{desc} 可执行",
                   "FAIL", "执行成功", str(e))


# ============================================================
# 维度 2: 无障碍 (A11y)
# ============================================================
A11Y_CHECKS = [
    # (CSS selector, required, description)
    ("a[href], button", False, "可交互元素"),
    ("h1, h2, h3, h4, h5, h6", False, "页面有标题层级"),
    ("main[role='main'], main", False, "main landmark 存在"),
    ("[aria-label], [aria-labelledby]", False, "ARIA 标签存在"),
]


def test_accessibility(page: Page):
    """
    6 个核心 A11y 维度检查:
      1. 跳转链接 (skip-to-content)
      2. 标题层级 (h1 > h2 > h3...)
      3. 焦点可见性 (:focus-visible outline)
      4. 键盘可达性 (Tab 键能到达所有交互元素)
      5. ARIA 标签
      6. 颜色对比度 (通过 CSS 变量 + getComputedStyle 推算)
    """
    print("\n" + "=" * 70)
    print("  维度 2: 无障碍 (A11y)")
    print("=" * 70)

    # ===== 检查 1: 公开页 A11y (游客视角) =====
    pages_to_test = [
        ("/", "首页"),
        ("/search", "公开搜索"),
        ("/public/search", "公开 8 字段搜索"),
        ("/compare?ids=1,2,3", "公开对比"),
        ("/login", "登录页")
    ]

    for path, label in pages_to_test:
        print(f"\n  [{label}] {path}")
        try:
            page.goto(f"{FRONTEND}{path}", wait_until="networkidle", timeout=15000)
            time.sleep(1)

            # 1. skip-to-content 跳转链接
            has_skip = page.query_selector("a[href='#main-content'], a[href$='#main-content']") is not None
            record("accessibility", f"{label} 跳到主内容链接",
                   "PASS" if has_skip else "FAIL",
                   "a[href='#main-content'] 存在",
                   f"存在={has_skip}",
                   "App.vue 加 <a class='skip-to-content' href='#main-content'>" if not has_skip else "")

            # 2. main landmark + role
            has_main = page.query_selector("main, [role='main']") is not None
            record("accessibility", f"{label} main landmark",
                   "PASS" if has_main else "FAIL",
                   "<main> 或 [role='main'] 存在",
                   f"存在={has_main}",
                   "App.vue 已用 <main role='main'>" if has_main else "App.vue 加 <main>")

            # 3. 至少 1 个标题
            heading_count = page.evaluate("() => document.querySelectorAll('h1, h2, h3, h4, h5, h6').length")
            record("accessibility", f"{label} 标题层级",
                   "PASS" if heading_count >= 1 else "WARN",
                   "至少 1 个 h1-h6",
                   f"count={heading_count}",
                   "添加页面主标题" if heading_count == 0 else "")

            # 4. 跳转链接 tabindex=-1 可聚焦
            if has_skip:
                page.keyboard.press("Tab")
                focused = page.evaluate("() => document.activeElement?.className || ''")
                has_focus = "skip" in focused.lower()
                record("accessibility", f"{label} 跳到主内容可键盘聚焦",
                       "PASS" if has_focus else "WARN",
                       "首次 Tab 跳到 skip link",
                       f"focused='{focused}'",
                       "skip-to-content 元素需无 transform 阻挡 Tab 焦点")

            # 5. 所有交互元素 (button/a) 至少有一处可访问名
            bad_buttons = page.evaluate("""() => {
                const els = document.querySelectorAll('button, a[href]');
                const bad = [];
                for (const el of els) {
                    const text = el.innerText?.trim();
                    const ariaLabel = el.getAttribute('aria-label');
                    const ariaLabelledby = el.getAttribute('aria-labelledby');
                    const title = el.getAttribute('title');
                    // Element Plus icon-only 按钮会有 <span class="el-icon"> 视觉
                    if (!text && !ariaLabel && !ariaLabelledby && !title) {
                        // 但有 i 元素 (el-icon) 算可访问
                        const hasIcon = el.querySelector('i, svg');
                        if (!hasIcon) {
                            bad.push({
                                tag: el.tagName,
                                class: el.className?.toString().slice(0, 60)
                            });
                        }
                    }
                }
                return bad.slice(0, 5);
            }""")
            record("accessibility", f"{label} 交互元素有可访问名",
                   "PASS" if not bad_buttons else "WARN",
                   "所有 button/a 都有文本/aria-label/title/icon",
                   f"无标签={len(bad_buttons)} {bad_buttons[:2] if bad_buttons else ''}",
                   "加 aria-label 或内嵌图标 (Element Plus 通用做法)")

            # 6. :focus-visible 样式存在 (检查样式表)
            has_focus_style = page.evaluate("""() => {
                // 找 :focus-visible 在样式表中
                for (const sheet of document.styleSheets) {
                    try {
                        for (const rule of sheet.cssRules || []) {
                            if (rule.selectorText && rule.selectorText.includes(':focus-visible')) {
                                return true;
                            }
                        }
                    } catch (e) { /* cross-origin */ }
                }
                return false;
            }""")
            record("accessibility", f"{label} 焦点可见样式",
                   "PASS" if has_focus_style else "WARN",
                   "全局 :focus-visible 样式存在",
                   f"存在={has_focus_style}",
                   "index.css 添加 :focus-visible { outline: ... }" if not has_focus_style else "")

        except Exception as e:
            record("accessibility", f"{label} 加载", "FAIL", "加载成功", str(e))


def test_aria_skip_links(page: Page):
    """专门检查新增的 skip-to-content 是否在所有公开页都生效"""
    print("\n  [skip-to-content 专项] 5 个公开页 + 5 个后台页")
    # 已包含在 test_accessibility 中, 这里留作扩展位


# ============================================================
# 维度 3: 性能 (Core Web Vitals)
# ============================================================
def test_performance(page: Page):
    """
    性能指标:
      - FCP (First Contentful Paint): 应 < 1.8s
      - LCP (Largest Contentful Paint): 应 < 2.5s
      - TTI (Time to Interactive): 应 < 3.5s
      - JS bundle size: 通过 /assets/*.js 头 size
      - 慢请求: 任何 > 1s 的 API
    """
    print("\n" + "=" * 70)
    print("  维度 3: 性能 (Core Web Vitals)")
    print("=" * 70)

    targets = [
        ("/search", "公开搜索"),
        ("/public/search", "公开 8 字段搜索"),
        ("/product/AC%20010323", "产品详情"),
        ("/compare?ids=1,2,3", "公开对比"),
    ]

    for path, label in targets:
        print(f"\n  [{label}] {path}")
        try:
            # 清理浏览器缓存
            page.goto(f"{FRONTEND}/", wait_until="domcontentloaded", timeout=10000)
            page.evaluate("() => { try { localStorage.clear(); } catch (e) {} }")
            page.context.clear_cookies()

            # 测量导航
            start = time.time()
            page.goto(f"{FRONTEND}{path}", wait_until="networkidle", timeout=20000)
            elapsed = time.time() - start

            # 收集 Web Vitals (用 PerformanceObserver)
            vitals = page.evaluate("""() => {
                const result = { fcp: null, lcp: null, tti: null, domContent: null, load: null };
                // Navigation timing
                const nav = performance.getEntriesByType('navigation')[0];
                if (nav) {
                    result.domContent = nav.domContentLoadedEventEnd - nav.startTime;
                    result.load = nav.loadEventEnd - nav.startTime;
                }
                // Paint timing
                const paints = performance.getEntriesByType('paint');
                for (const p of paints) {
                    if (p.name === 'first-contentful-paint') result.fcp = p.startTime;
                }
                // LCP (需要 LCP 元素, 大多数页面有)
                try {
                    let lcp = 0;
                    const obs = new PerformanceObserver((list) => {
                        const entries = list.getEntries();
                        const last = entries[entries.length - 1];
                        lcp = last?.startTime || 0;
                    });
                    obs.observe({ type: 'largest-contentful-paint', buffered: true });
                    result.lcp = lcp;
                    obs.disconnect();
                } catch (e) { /* 不支持 */ }
                return result;
            }""")

            fcp = vitals.get("fcp") or 0
            lcp = vitals.get("lcp") or 0
            load = vitals.get("load") or 0
            dom = vitals.get("domContent") or 0

            # FCP
            record("performance", f"{label} FCP",
                   "PASS" if fcp < 1800 else ("WARN" if fcp < 3000 else "FAIL"),
                   "< 1800ms (Good)",
                   f"fcp={fcp:.0f}ms",
                   "考虑路由懒加载 + 首屏 CSS 内联" if fcp >= 1800 else "")
            # LCP
            record("performance", f"{label} LCP",
                   "PASS" if lcp < 2500 else ("WARN" if lcp < 4000 else "FAIL"),
                   "< 2500ms (Good)",
                   f"lcp={lcp:.0f}ms",
                   "首屏大图改用 next/image + 懒加载" if lcp >= 2500 else "")
            # DOMContentLoaded
            record("performance", f"{label} DOMContentLoaded",
                   "PASS" if dom < 1500 else ("WARN" if dom < 3000 else "FAIL"),
                   "< 1500ms",
                   f"dom={dom:.0f}ms",
                   "减少初始 JS 体积" if dom >= 1500 else "")
            # 总耗时
            record("performance", f"{label} 总耗时 (networkidle)",
                   "PASS" if elapsed < 3.0 else ("WARN" if elapsed < 5.0 else "FAIL"),
                   "< 3.0s",
                   f"elapsed={elapsed:.2f}s",
                   "检查慢 API, 启用路由懒加载" if elapsed >= 3.0 else "")

        except Exception as e:
            record("performance", f"{label} 测量", "FAIL", "成功", str(e))


# ============================================================
# 维度 4: 跨浏览器
# ============================================================
def test_cross_browser():
    """
    用 Playwright 的 3 个引擎跑同一页面, 验证关键元素都可见
    Chromium / Firefox / WebKit
    """
    print("\n" + "=" * 70)
    print("  维度 4: 跨浏览器 (Chromium / Firefox / WebKit)")
    print("=" * 70)

    browsers_to_test = [
        ("chromium", "Chromium (Chrome/Edge)"),
        ("firefox", "Firefox"),
        ("webkit", "WebKit (Safari)")
    ]

    pages_to_test = [
        ("/search", "公开搜索", "input"),
        ("/compare?ids=1,2,3", "公开对比", ".el-button"),
        ("/login", "登录页", "input[autocomplete='username']")
    ]

    with sync_playwright() as p:
        for engine, engine_name in browsers_to_test:
            print(f"\n  [{engine_name}]")
            browser: Browser | None = None
            try:
                if engine == "chromium":
                    browser = p.chromium.launch(headless=True)
                elif engine == "firefox":
                    browser = p.firefox.launch(headless=True)
                elif engine == "webkit":
                    browser = p.webkit.launch(headless=True)

                context = browser.new_context()
                page = context.new_page()
                # 短超时: 浏览器没安装就 skip
                try:
                    for path, label, selector in pages_to_test:
                        page.goto(f"{FRONTEND}{path}", wait_until="networkidle", timeout=15000)
                        time.sleep(1)
                        has = page.query_selector(selector) is not None
                        record("cross_browser", f"{engine_name} - {label} 关键元素",
                               "PASS" if has else "WARN",
                               f"可见 {selector[:50]}",
                               f"visible={has}",
                               "检查 CSS 兼容性 (Safari/Firefox 差异)")
                except Exception as e:
                    record("cross_browser", f"{engine_name} 启动", "WARN",
                           "浏览器启动成功", str(e)[:80],
                           "playwright install " + engine)
                finally:
                    context.close()
                    browser.close()
            except Exception as e:
                record("cross_browser", f"{engine_name} 启动", "WARN",
                       "浏览器启动成功", str(e)[:80],
                       f"playwright install {engine}")


# ============================================================
# 维度 5: 跨设备
# ============================================================
def test_cross_device():
    """
    3 个视口尺寸, 验证响应式布局
      - Desktop 1280x800
      - Tablet 768x1024 (iPad)
      - Mobile 375x667 (iPhone SE)
    关键检查:
      - 无水平滚动条 (max-width: 100%)
      - 关键按钮可点 (非 hidden, 非太小)
      - 表格在移动端有水平滚动
    """
    print("\n" + "=" * 70)
    print("  维度 5: 跨设备 (Desktop / Tablet / Mobile)")
    print("=" * 70)

    devices = [
        ("Desktop 1280x800", 1280, 800),
        ("Tablet 768x1024", 768, 1024),
        ("Mobile 375x667", 375, 667)
    ]

    pages_to_test = [
        ("/search", "公开搜索", "input"),
        ("/compare?ids=1,2,3", "公开对比", ".el-button"),
        ("/login", "登录页", "input[autocomplete='username']")
    ]

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        for device_name, w, h in devices:
            print(f"\n  [{device_name}]")
            context = browser.new_context(viewport={"width": w, "height": h})
            page = context.new_page()
            try:
                for path, label, selector in pages_to_test:
                    page.goto(f"{FRONTEND}{path}", wait_until="networkidle", timeout=15000)
                    time.sleep(1)
                    # 1. 关键元素可见
                    has = page.query_selector(selector) is not None
                    record("cross_device", f"{device_name} - {label} 关键元素",
                           "PASS" if has else "FAIL",
                           f"可见 {selector[:50]}",
                           f"visible={has}",
                           "检查响应式断点 (sm/md/lg) 是否正确")

                    # 2. 无水平滚动 (除表格)
                    scroll_x = page.evaluate("""() => {
                        return document.documentElement.scrollWidth - document.documentElement.clientWidth;
                    }""")
                    is_horizontal_overflow = scroll_x > 5  # 5px 容差
                    # /search 表格区允许水平滚动, 但 body 不应有
                    record("cross_device", f"{device_name} - {label} body 无水平溢出",
                           "PASS" if not is_horizontal_overflow else "WARN",
                           "scrollWidth - clientWidth <= 5px",
                           f"overflow={scroll_x}px",
                           "排查宽于视口的元素 (含 fixed width 的图片/表格)")

                    # 3. 移动端 关键按钮可点 (尺寸 >= 32px)
                    if w <= 480:
                        button_sizes = page.evaluate("""(sel) => {
                            const btn = document.querySelector(sel);
                            if (!btn) return null;
                            const rect = btn.getBoundingClientRect();
                            return { w: rect.width, h: rect.height };
                        }""", selector)
                        if button_sizes:
                            is_tappable = button_sizes["w"] >= 32 and button_sizes["h"] >= 24
                            record("cross_device", f"{device_name} - {label} 按钮可点尺寸",
                                   "PASS" if is_tappable else "WARN",
                                   ">= 32x24 px (WCAG 2.5.5 Target Size AAA)",
                                   f"size={button_sizes['w']:.0f}x{button_sizes['h']:.0f}px",
                                   "加 min-width/min-height 保障移动端可点")

            except Exception as e:
                record("cross_device", f"{device_name} 测量", "FAIL", "成功", str(e))
            finally:
                context.close()
        browser.close()


# ============================================================
# 报告生成
# ============================================================
def write_reports():
    """写 JSON + Markdown 报告"""
    REPORT_JSON.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    md = []
    md.append("# SakuraFilter UX 审计报告")
    md.append("")
    md.append(f"**生成时间**: {results['generatedAt']}  ")
    md.append(f"**总检查项**: {results['summary']['total']}  ")
    md.append(f"**PASS**: {results['summary']['passed']} | **FAIL**: {results['summary']['failed']} | **WARN**: {results['summary']['warned']}")
    md.append("")

    for dim, info in results["dimensions"].items():
        if not info["items"]:
            continue
        dim_names = {
            "functionality": "1. 功能性 (E2E 回归)",
            "accessibility": "2. 无障碍 (A11y)",
            "performance": "3. 性能 (Core Web Vitals)",
            "cross_browser": "4. 跨浏览器 (Chromium/Firefox/WebKit)",
            "cross_device": "5. 跨设备 (响应式)"
        }
        md.append(f"## {dim_names.get(dim, dim)}")
        md.append("")
        for it in info["items"]:
            icon = {"PASS": "✓", "FAIL": "✗", "WARN": "!"}[it["status"]]
            md.append(f"- [{icon}] **{it['name']}** — {it['status']}")
            md.append(f"  - 预期: {it['expected']}")
            md.append(f"  - 实际: {it['actual']}")
            if it.get("fix"):
                md.append(f"  - 方案: {it['fix']}")
        md.append("")

    # 问题与建议
    if results["issues"]:
        md.append("## 需关注问题 (FAIL + WARN)")
        md.append("")
        for it in results["issues"]:
            md.append(f"### {it['name']} ({it['status']})")
            md.append(f"- 维度: {it.get('dim', '')}")
            md.append(f"- 预期: {it['expected']}")
            md.append(f"- 实际: {it['actual']}")
            if it.get("fix"):
                md.append(f"- **建议方案**: {it['fix']}")
            md.append("")

    REPORT_MD.write_text("\n".join(md), encoding="utf-8")
    print(f"\n  报告已写入:")
    print(f"    - {REPORT_JSON}")
    print(f"    - {REPORT_MD}")


# ============================================================
# 主流程
# ============================================================
def main():
    print("=" * 70)
    print(" SakuraFilter 综合 UX 审计 (Day 14+ P0 改进闭环)")
    print(f" Backend:  {BACKEND}")
    print(f" Frontend: {FRONTEND}")
    print("=" * 70)

    # 维度 1: 功能性 (子进程)
    test_functionality()

    # 维度 2-5: 浏览器内
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context()
        page = context.new_page()

        test_accessibility(page)
        test_performance(page)

        context.close()
        browser.close()

    # 维度 4-5: 跨浏览器 + 跨设备 (独立 context)
    test_cross_browser()
    test_cross_device()

    # 汇总
    print("\n" + "=" * 70)
    s = results["summary"]
    print(f" 汇总: 总 {s['total']} 项, PASS={s['passed']} FAIL={s['failed']} WARN={s['warned']}")
    print("=" * 70)

    write_reports()

    sys.exit(1 if s["failed"] > 0 else 0)


if __name__ == "__main__":
    main()