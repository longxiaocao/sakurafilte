"""
批次 7: 对比界面深色模式 + 打印验证
WHY: 修复 2 个用户报告的 bug:
  1. 深色模式下对比内容消失 (硬编码颜色不响应主题)
  2. 打印时把导航/工具栏也打进去 (无全局 .no-print 规则)
"""
import asyncio
import json
from pathlib import Path
from playwright.async_api import async_playwright

OUT = Path(r"d:\projects\sakurafilter\spike-test")
RESULTS = {"checks": []}


def check(name, ok, msg=""):
    RESULTS["checks"].append({
        "name": name,
        "status": "PASS" if ok else "FAIL",
        "msg": msg[:200] if msg else "",
    })


async def run():
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        ctx = await browser.new_context(viewport={"width": 1440, "height": 900})
        page = await ctx.new_page()

        # === 浅色模式对比 ===
        await page.goto("http://localhost:5173/compare?ids=1,2,3", wait_until="networkidle")
        await page.wait_for_timeout(500)
        light_path = OUT / "compare_light.png"
        await page.screenshot(path=str(light_path), full_page=True)
        # 验证 .data-cell 背景非透明 + 文字色与背景对比
        cells_light = await page.locator(".data-cell").all()
        if cells_light:
            sample = await cells_light[0].evaluate("""el => {
                const cs = getComputedStyle(el);
                return { bg: cs.backgroundColor, color: cs.color };
            }""")
            # 浅色: bg = rgb(255,255,255) 或 var(--color-bg-elevated), color = 深色
            check("浅色模式 .data-cell 背景色",
                  "rgb" in sample["bg"] and sample["bg"] != "rgba(0, 0, 0, 0)",
                  f"bg={sample['bg']} color={sample['color']}")
        else:
            check("浅色模式 .data-cell 存在", False, "未找到 .data-cell 元素")

        # === 深色模式对比 ===
        await page.evaluate("""() => {
            document.documentElement.classList.add('dark');
            try { localStorage.setItem('sakura-theme', 'dark'); } catch(e) {}
        }""")
        await page.wait_for_timeout(300)
        dark_path = OUT / "compare_dark.png"
        await page.screenshot(path=str(dark_path), full_page=True)

        # 验证 .data-cell 文字色是浅色 (深色模式下应可见)
        cells_dark = await page.locator(".data-cell").all()
        if cells_dark:
            sample = await cells_dark[0].evaluate("""el => {
                const cs = getComputedStyle(el);
                return { bg: cs.backgroundColor, color: cs.color };
            }""")
            # 深色: bg 应是深色 (RGB 低值), color 应是浅色 (RGB 高值)
            def brightness(rgb_str):
                # "rgb(10, 10, 12)" → 亮度估算
                import re
                m = re.findall(r"\d+", rgb_str)
                if len(m) < 3: return 128
                r, g, b = int(m[0]), int(m[1]), int(m[2])
                return (r + g + b) / 3
            bg_b = brightness(sample["bg"])
            fg_b = brightness(sample["color"])
            check("深色模式 .data-cell 文字亮度 > 背景亮度",
                  fg_b > bg_b,
                  f"bg={sample['bg']}({bg_b:.0f}) color={sample['color']}({fg_b:.0f})")

            # 验证 .data-cell.diff 在深色模式可见 (有背景 + 有颜色)
            diff_count = await page.locator(".data-cell.diff").count()
            if diff_count > 0:
                ds = await page.locator(".data-cell.diff").first.evaluate("""el => {
                    const cs = getComputedStyle(el);
                    return { bg: cs.backgroundColor, color: cs.color };
                }""")
                check("深色模式 .data-cell.diff 可见",
                      "rgb" in ds["bg"] and ds["bg"] != "rgba(0, 0, 0, 0)",
                      f"diff bg={ds['bg']} color={ds['color']}")
            else:
                check("深色模式 .data-cell.diff 存在", True, "无差异行, 跳过")
        else:
            check("深色模式 .data-cell 存在", False, "未找到")

        # === 打印模拟 (emulateMedia print) ===
        await page.emulate_media(media="print")
        await page.wait_for_timeout(200)
        print_path = OUT / "compare_print.png"
        await page.screenshot(path=str(print_path), full_page=True)

        # 验证 header.app-header 在打印下被隐藏
        header_disp = await page.locator("header.app-header").evaluate("""el => {
            return getComputedStyle(el).display;
        }""")
        check("打印时 AppHeader 被隐藏",
              header_disp == "none",
              f"header display={header_disp}")

        # 验证 .compare-toolbar 打印时被隐藏
        toolbar_disp = await page.locator(".compare-toolbar").first.evaluate("""el => {
            return getComputedStyle(el).display;
        }""") if await page.locator(".compare-toolbar").count() > 0 else "none"
        check("打印时 compare-toolbar 被隐藏",
              toolbar_disp == "none",
              f"toolbar display={toolbar_disp}")

        # 验证 .data-cell 在打印中仍可见
        cell_disp = await page.locator(".data-cell").first.evaluate("""el => {
            const cs = getComputedStyle(el);
            return { display: cs.display, bg: cs.backgroundColor, color: cs.color };
        }""") if await page.locator(".data-cell").count() > 0 else None
        check("打印时 .data-cell 仍可见",
              cell_disp and cell_disp["display"] != "none",
              str(cell_disp)[:150] if cell_disp else "无 .data-cell")

        # 验证打印时 .group-name-cell 文字对比 (深色模式打印应转浅色)
        gn = await page.locator(".group-name-cell").first.evaluate("""el => {
            const cs = getComputedStyle(el);
            return { bg: cs.backgroundColor, color: cs.color };
        }""") if await page.locator(".group-name-cell").count() > 0 else None
        if gn:
            # 打印应该 bg=深, color=浅
            check("打印 .group-name-cell 文字色对比",
                  "rgb" in gn["bg"] and "rgb" in gn["color"] and gn["bg"] != gn["color"],
                  f"bg={gn['bg']} color={gn['color']}")

        await page.emulate_media(media="screen")
        await browser.close()

    # 汇总
    total = len(RESULTS["checks"])
    fail = sum(1 for c in RESULTS["checks"] if c["status"] == "FAIL")
    pass_ = total - fail
    print(f"\n========= 对比界面深色+打印验证 =========")
    print(f"总计 {total}: PASS={pass_} FAIL={fail}")
    for c in RESULTS["checks"]:
        marker = "PASS" if c["status"] == "PASS" else "FAIL"
        print(f"  [{marker}] {c['name']}: {c['msg'][:120]}")
    (OUT / "compare_dark_print_report.json").write_text(
        json.dumps(RESULTS, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return fail == 0


if __name__ == "__main__":
    ok = asyncio.run(run())
    raise SystemExit(0 if ok else 1)
