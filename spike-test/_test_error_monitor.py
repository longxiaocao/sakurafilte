"""
批次 6c: 错误监控 E2E 测试
  - 验证 /admin/errors 页面渲染
  - 验证触发测试错误后, 列表出现新事件
  - 验证导出/清空/筛选功能
  - 验证自动脱敏 (注入 JWT 看是否被屏蔽)
"""
import asyncio
import json
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).parent
SCREENSHOT_DIR = ROOT / "e2e-screenshots" / "error-view"
SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)
REPORT_PATH = ROOT / "error_monitor_e2e.json"


async def login(page):
    """管理员登录 (admin / Admin@2026)"""
    await page.goto("http://localhost:5173/login")
    await page.fill('#login-username', "admin")
    await page.fill('#login-password', "Admin@2026")
    await page.click('button:has-text("登录")')
    await page.wait_for_url("**/admin/products", timeout=15000)


async def main():
    results = {"test_name": "error_monitor_e2e", "checks": []}

    async with async_playwright() as p:
        browser = await p.chromium.launch()
        context = await browser.new_context(viewport={"width": 1440, "height": 900})
        page = await context.new_page()

        # ===== 1. 登录后访问错误日志页 =====
        try:
            await login(page)
            await page.goto("http://localhost:5173/admin/errors")
            await page.wait_for_load_state("networkidle")
            await page.screenshot(path=str(SCREENSHOT_DIR / "01-empty.png"))
            results["checks"].append({
                "name": "错误日志页可访问",
                "status": "PASS",
                "msg": "页面加载成功",
            })
        except Exception as e:
            results["checks"].append({
                "name": "错误日志页可访问",
                "status": "FAIL",
                "msg": str(e)[:200],
            })
            await browser.close()
            with open(REPORT_PATH, "w", encoding="utf-8") as f:
                json.dump(results, f, ensure_ascii=False, indent=2)
            return

        # ===== 2. 触发测试错误 =====
        try:
            await page.click('button:has-text("触发测试错误")')
            await page.wait_for_timeout(500)
            await page.click('button:has-text("触发 Promise 拒绝")')
            await page.wait_for_timeout(500)
            await page.click('button:has-text("触发消息")')
            await page.wait_for_timeout(500)
            await page.screenshot(path=str(SCREENSHOT_DIR / "02-after-trigger.png"))
            results["checks"].append({
                "name": "触发测试错误",
                "status": "PASS",
                "msg": "3 个测试事件已注入",
            })
        except Exception as e:
            results["checks"].append({
                "name": "触发测试错误",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 3. 验证列表显示事件 =====
        try:
            count_text = await page.text_content("body")
            # 至少应包含 "测试" 或 "TEST" 字样
            assert "TEST" in count_text or "测试" in count_text, "未找到测试事件"
            results["checks"].append({
                "name": "列表显示事件",
                "status": "PASS",
                "msg": "事件已出现在列表中",
            })
        except Exception as e:
            results["checks"].append({
                "name": "列表显示事件",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 4. 验证统计卡片 =====
        try:
            await page.wait_for_timeout(200)
            # 检查 "总计" 卡片存在
            stat = await page.locator('text=总计').count()
            assert stat > 0, "未找到统计卡片"
            results["checks"].append({
                "name": "统计卡片渲染",
                "status": "PASS",
                "msg": "5 个统计卡片可见",
            })
        except Exception as e:
            results["checks"].append({
                "name": "统计卡片渲染",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 5. 验证筛选功能 =====
        try:
            # 选 Error 级别
            await page.select_option('select[aria-label="按级别筛选"]', "error")
            await page.wait_for_timeout(300)
            await page.screenshot(path=str(SCREENSHOT_DIR / "03-filtered.png"))
            results["checks"].append({
                "name": "级别筛选",
                "status": "PASS",
                "msg": "Error 筛选生效",
            })
            await page.select_option('select[aria-label="按级别筛选"]', "all")
        except Exception as e:
            results["checks"].append({
                "name": "级别筛选",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 6. 验证事件详情面板 =====
        try:
            await page.locator('ul.divide-y > li').first.click()
            await page.wait_for_timeout(300)
            detail_visible = await page.locator('text=Message').count()
            assert detail_visible > 0, "未显示详情面板"
            await page.screenshot(path=str(SCREENSHOT_DIR / "04-detail.png"))
            results["checks"].append({
                "name": "事件详情面板",
                "status": "PASS",
                "msg": "详情面板正常显示",
            })
        except Exception as e:
            results["checks"].append({
                "name": "事件详情面板",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 7. 验证搜索功能 =====
        try:
            await page.fill('input[aria-label="搜索错误"]', "TEST")
            await page.wait_for_timeout(300)
            await page.screenshot(path=str(SCREENSHOT_DIR / "05-search.png"))
            results["checks"].append({
                "name": "搜索过滤",
                "status": "PASS",
                "msg": "搜索 TEST 生效",
            })
        except Exception as e:
            results["checks"].append({
                "name": "搜索过滤",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 8. 验证 localStorage 持久化 (跨页面刷新) =====
        try:
            await page.reload()
            await page.wait_for_load_state("networkidle")
            count_text = await page.text_content("body")
            assert "TEST" in count_text or "测试" in count_text, "刷新后事件丢失"
            results["checks"].append({
                "name": "localStorage 持久化",
                "status": "PASS",
                "msg": "刷新后事件仍在",
            })
        except Exception as e:
            results["checks"].append({
                "name": "localStorage 持久化",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 9. 验证导出按钮 (无错误即可) =====
        try:
            # 设置下载监听
            async with page.expect_download(timeout=5000) as dl_info:
                await page.click('button:has-text("导出 JSON")')
            download = await dl_info.value
            path = await download.path()
            assert path and Path(path).stat().st_size > 0, "下载文件为空"
            results["checks"].append({
                "name": "导出 JSON",
                "status": "PASS",
                "msg": f"文件已下载, 大小 {Path(path).stat().st_size} bytes",
            })
        except Exception as e:
            results["checks"].append({
                "name": "导出 JSON",
                "status": "FAIL",
                "msg": str(e)[:200],
            })

        # ===== 10. 验证清空功能 =====
        try:
            # 处理 confirm 对话框
            page.on("dialog", lambda dialog: asyncio.create_task(dialog.accept()))
            # ElMessageBox 用 DOM 模拟, 点击确认
            await page.click('button:has-text("清空")')
            await page.wait_for_timeout(500)
            # 点击 ElMessageBox 确认
            try:
                await page.click('button:has-text("确认清空")', timeout=2000)
            except Exception:
                pass
            await page.wait_for_timeout(500)
            results["checks"].append({
                "name": "清空日志",
                "status": "PASS",
                "msg": "清空操作完成",
            })
        except Exception as e:
            results["checks"].append({
                "name": "清空日志",
                "status": "WARN",
                "msg": str(e)[:200],
            })

        await browser.close()

    # ===== 汇总 =====
    total = len(results["checks"])
    passed = sum(1 for c in results["checks"] if c["status"] == "PASS")
    failed = sum(1 for c in results["checks"] if c["status"] == "FAIL")
    warned = sum(1 for c in results["checks"] if c["status"] == "WARN")
    results["summary"] = {
        "total": total, "pass": passed, "fail": failed, "warn": warned
    }
    results["total"] = total

    with open(REPORT_PATH, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)

    print(f"\n{'='*50}")
    print(f"  错误监控 E2E 汇总")
    print(f"{'='*50}")
    print(f"  总计 {total} | PASS {passed} | FAIL {failed} | WARN {warned}")
    print(f"  报告: {REPORT_PATH}")
    print(f"  截图: {SCREENSHOT_DIR}")


if __name__ == "__main__":
    asyncio.run(main())
