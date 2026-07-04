"""
主题切换端到端测试 (Theme Switch E2E Test)
==========================================
测试目标:
  1. 遍历所有核心页面 (公开 + 后台)
  2. 白天模式 + 黑夜模式各截图一次
  3. 检测黑夜模式下是否有未正确应用样式的白色组件
  4. 检查文字对比度 (深色背景上的深色文字 = 问题)
  5. 生成详细测试报告

输出:
  - spike-test/theme-screenshots/*.png  (白天/黑夜模式截图)
  - spike-test/theme_test_report.json   (测试报告)
"""
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

BACKEND = "http://localhost:5148"
FRONTEND = "http://localhost:5173"
ADMIN_USER = "admin"
ADMIN_PASS = "Admin@2026"
SCREENSHOT_DIR = Path(__file__).resolve().parent / "theme-screenshots"
REPORT_PATH = Path(__file__).resolve().parent / "theme_test_report.json"
SCREENSHOT_DIR.mkdir(exist_ok=True)

# 测试页面清单 (公开 + 后台)
PAGES = [
    {"name": "01-login", "url": "/login", "require_auth": False, "group": "public"},
    {"name": "02-search", "url": "/search", "require_auth": False, "group": "public"},
    {"name": "03-public-search", "url": "/public/search", "require_auth": False, "group": "public"},
    {"name": "04-product-detail", "url": "/product/P00050000", "require_auth": False, "group": "public"},
    {"name": "05-demo", "url": "/demo", "require_auth": False, "group": "public"},
    {"name": "06-admin-products", "url": "/admin/products", "require_auth": True, "group": "admin"},
    {"name": "07-admin-etl", "url": "/admin/etl", "require_auth": True, "group": "admin"},
    {"name": "08-admin-users", "url": "/admin/users", "require_auth": True, "group": "admin"},
    {"name": "09-admin-compare", "url": "/admin/compare", "require_auth": True, "group": "admin"},
    {"name": "10-admin-help", "url": "/admin/help", "require_auth": True, "group": "admin"},
    {"name": "11-admin-perf", "url": "/admin/perf", "require_auth": True, "group": "admin"},
    {"name": "12-admin-oem-brands", "url": "/admin/dict/oem-brands", "require_auth": True, "group": "admin"},
    {"name": "13-admin-types", "url": "/admin/dict/types", "require_auth": True, "group": "admin"},
    {"name": "14-admin-machines", "url": "/admin/dict/machines", "require_auth": True, "group": "admin"},
    {"name": "15-admin-medias", "url": "/admin/dict/medias", "require_auth": True, "group": "admin"},
    {"name": "16-admin-engines", "url": "/admin/dict/engines", "require_auth": True, "group": "admin"},
    {"name": "17-change-password", "url": "/change-password", "require_auth": True, "group": "admin"},
]

# 白色背景检测阈值
WHITE_COLORS = {
    "rgb(255, 255, 255)": "#ffffff",
    "rgb(254, 254, 254)": "#fefefe",
    "rgb(253, 253, 253)": "#fdfdfd",
    "rgb(252, 252, 252)": "#fcfcfc",
    "rgb(250, 250, 250)": "#fafafa",
    "rgb(245, 245, 245)": "#f5f5f5",
}


def login(page):
    """登录后台获取 JWT token"""
    print("\n[登录] 开始登录后台...")
    page.goto(f"{FRONTEND}/login", wait_until="networkidle", timeout=15000)
    time.sleep(1)
    # 填写表单
    page.fill("#login-username", ADMIN_USER)
    page.fill("#login-password", ADMIN_PASS)
    # 点击登录按钮
    page.click("button:has-text('登录')")
    page.wait_for_url("**/admin/**", timeout=10000)
    print(f"[登录] 登录成功, 当前 URL: {page.url}")


def switch_to_dark(page):
    """切换到黑夜模式"""
    try:
        btn = page.query_selector('button[aria-label="主题切换"]')
        if btn:
            has_dark = page.evaluate("document.documentElement.classList.contains('dark')")
            if not has_dark:
                btn.click()
                time.sleep(0.5)
                # 将鼠标移到页面左上角, 避免触发按钮 hover 状态
                page.mouse.move(0, 0)
    except Exception as e:
        print(f"  ⚠ 切换黑夜模式失败: {e}")


def switch_to_light(page):
    """切换到白天模式"""
    try:
        btn = page.query_selector('button[aria-label="主题切换"]')
        if btn:
            has_dark = page.evaluate("document.documentElement.classList.contains('dark')")
            if has_dark:
                btn.click()
                time.sleep(0.5)
                # 将鼠标移到页面左上角, 避免触发按钮 hover 状态
                page.mouse.move(0, 0)
    except Exception as e:
        print(f"  ⚠ 切换白天模式失败: {e}")


def detect_white_components(page):
    """检测黑夜模式下仍是白色背景的组件"""
    return page.evaluate("""() => {
        const whiteColors = ['rgb(255, 255, 255)', 'rgb(254, 254, 254)', 'rgb(253, 253, 253)',
                             'rgb(252, 252, 252)', 'rgb(250, 250, 250)', 'rgb(245, 245, 245)'];
        const results = [];
        const all = document.querySelectorAll('*');
        for (let i = 0; i < all.length && results.length < 30; i++) {
            const el = all[i];
            const bg = getComputedStyle(el).backgroundColor;
            const rect = el.getBoundingClientRect();
            if (whiteColors.includes(bg) && rect.width > 50 && rect.height > 30) {
                results.push({
                    tag: el.tagName,
                    class: (el.className?.toString?.() || '').slice(0, 120),
                    id: el.id || '',
                    bg: bg,
                    w: Math.round(rect.width),
                    h: Math.round(rect.height),
                    x: Math.round(rect.x),
                    y: Math.round(rect.y)
                });
            }
        }
        return results;
    }""")


def detect_contrast_issues(page):
    """检测对比度问题 (深色背景上的深色文字)"""
    return page.evaluate("""() => {
        const results = [];
        const all = document.querySelectorAll('*');
        for (let i = 0; i < all.length && results.length < 20; i++) {
            const el = all[i];
            if (el.children.length > 0) continue;
            const style = getComputedStyle(el);
            const bg = style.backgroundColor;
            const color = style.color;
            const rect = el.getBoundingClientRect();
            const text = el.textContent?.trim();
            if (!text || rect.width < 50 || rect.height < 15) continue;

            // 解析 RGB 值
            const bgMatch = bg.match(/rgb\\((\\d+),\\s*(\\d+),\\s*(\\d+)/);
            const colorMatch = color.match(/rgb\\((\\d+),\\s*(\\d+),\\s*(\\d+)/);
            if (!bgMatch || !colorMatch) continue;

            const bgR = parseInt(bgMatch[1]), bgG = parseInt(bgMatch[2]), bgB = parseInt(bgMatch[3]);
            const cR = parseInt(colorMatch[1]), cG = parseInt(colorMatch[2]), cB = parseInt(colorMatch[3]);

            // 计算亮度 (luminance)
            const bgLum = (0.299 * bgR + 0.587 * bgG + 0.114 * bgB) / 255;
            const cLum = (0.299 * cR + 0.587 * cG + 0.114 * cB) / 255;

            // 深色背景 + 深色文字 = 对比度不足
            if (bgLum < 0.2 && cLum < 0.3) {
                results.push({
                    tag: el.tagName,
                    text: text.slice(0, 60),
                    bg: bg,
                    color: color,
                    bgLum: bgLum.toFixed(3),
                    textLum: cLum.toFixed(3)
                });
            }
            // 浅色背景 + 浅色文字 = 对比度不足
            if (bgLum > 0.8 && cLum > 0.7) {
                results.push({
                    tag: el.tagName,
                    text: text.slice(0, 60),
                    bg: bg,
                    color: color,
                    bgLum: bgLum.toFixed(3),
                    textLum: cLum.toFixed(3)
                });
            }
        }
        return results;
    }""")


def test_page(page, page_info, logged_in):
    """测试单个页面"""
    name = page_info["name"]
    url = page_info["url"]
    require_auth = page_info["require_auth"]

    if require_auth and not logged_in:
        return {"name": name, "url": url, "status": "SKIP", "reason": "未登录"}

    result = {
        "name": name,
        "url": url,
        "group": page_info["group"],
        "light_mode": {"screenshot": None, "white_components": 0, "contrast_issues": 0},
        "dark_mode": {"screenshot": None, "white_components": [], "contrast_issues": []},
        "status": "PASS",
        "issues": []
    }

    try:
        print(f"\n[测试] {name} - {url}")
        page.goto(f"{FRONTEND}{url}", wait_until="networkidle", timeout=15000)
        time.sleep(1.5)

        # === 白天模式 ===
        switch_to_light(page)
        time.sleep(0.8)
        light_shot = str(SCREENSHOT_DIR / f"{name}-light.png")
        page.screenshot(path=light_shot, full_page=True)
        result["light_mode"]["screenshot"] = light_shot
        print(f"  ✓ 白天模式截图: {name}-light.png")

        # === 黑夜模式 ===
        switch_to_dark(page)
        time.sleep(1.0)
        dark_shot = str(SCREENSHOT_DIR / f"{name}-dark.png")
        page.screenshot(path=dark_shot, full_page=True)
        result["dark_mode"]["screenshot"] = dark_shot
        print(f"  ✓ 黑夜模式截图: {name}-dark.png")

        # 检测黑夜模式下的白色组件
        white_comps = detect_white_components(page)
        result["dark_mode"]["white_components"] = white_comps
        if white_comps:
            result["status"] = "FAIL"
            result["issues"].append({
                "type": "white_component_in_dark_mode",
                "count": len(white_comps),
                "details": white_comps[:5]
            })
            print(f"  ✗ 发现 {len(white_comps)} 个白色组件 (黑夜模式下)")

        # 检测对比度问题
        contrast_issues = detect_contrast_issues(page)
        result["dark_mode"]["contrast_issues"] = contrast_issues
        if contrast_issues:
            if result["status"] != "FAIL":
                result["status"] = "WARN"
            result["issues"].append({
                "type": "contrast_issue",
                "count": len(contrast_issues),
                "details": contrast_issues[:5]
            })
            print(f"  ⚠ 发现 {len(contrast_issues)} 个对比度问题")

        if not white_comps and not contrast_issues:
            print(f"  ✓ 黑夜模式无白色组件, 无对比度问题")

    except Exception as e:
        result["status"] = "ERROR"
        result["issues"].append({"type": "exception", "message": str(e)})
        print(f"  ✗ 测试异常: {e}")

    return result


def main():
    print("=" * 60)
    print("  SakuraFilter 主题切换端到端测试")
    print("=" * 60)

    results = []
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1440, "height": 900})
        page = context.new_page()

        # 登录后台
        logged_in = False
        try:
            login(page)
            logged_in = True
        except Exception as e:
            print(f"[登录] 登录失败: {e}")

        # 测试所有页面
        for page_info in PAGES:
            result = test_page(page, page_info, logged_in)
            results.append(result)

        browser.close()

    # 生成报告
    summary = {
        "PASS": sum(1 for r in results if r["status"] == "PASS"),
        "FAIL": sum(1 for r in results if r["status"] == "FAIL"),
        "WARN": sum(1 for r in results if r["status"] == "WARN"),
        "ERROR": sum(1 for r in results if r["status"] == "ERROR"),
        "SKIP": sum(1 for r in results if r["status"] == "SKIP"),
    }

    report = {
        "test_name": "主题切换端到端测试",
        "test_time": time.strftime("%Y-%m-%d %H:%M:%S"),
        "summary": summary,
        "total": len(results),
        "results": results
    }

    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)

    print("\n" + "=" * 60)
    print("  测试汇总")
    print("=" * 60)
    print(f"  总计: {len(results)} 页面")
    print(f"  ✓ PASS: {summary['PASS']}")
    print(f"  ✗ FAIL: {summary['FAIL']}")
    print(f"  ⚠ WARN: {summary['WARN']}")
    print(f"  ✗ ERROR: {summary['ERROR']}")
    print(f"  ○ SKIP: {summary['SKIP']}")
    print(f"\n  报告已保存: {REPORT_PATH}")
    print(f"  截图目录: {SCREENSHOT_DIR}")

    return report


if __name__ == "__main__":
    main()
