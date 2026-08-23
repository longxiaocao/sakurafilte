"""
轻量级性能 + a11y 审计
========================
WHY 不用 lighthouse / axe-core CLI:
  - lighthouse npm 包 ~50MB, CI 启动慢 + 网络下载失败风险
  - @axe-core/playwright 也需新依赖
  - 改用 Playwright 内置 Performance API + Web Vitals, 0 新依赖, 30 行代码

测量指标:
  - LCP (Largest Contentful Paint) - 性能核心指标, 应 < 2.5s
  - FCP (First Contentful Paint) - 首次内容绘制
  - CLS (Cumulative Layout Shift) - 布局稳定性, 应 < 0.1
  - 总加载时长 - 从 navigation 到 load 事件
  - 控制台错误 - 已知 a11y 风险的代理指标

退出码: 0 OK / 1 WARN (>= 阈值) / 2 FAIL (异常)
"""
import asyncio
import json
import sys
from datetime import datetime
from pathlib import Path
from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "spike-test" / "perf_audit.json"
BASE = "http://localhost:5173"

# 公开路由 (无登录)
ROUTES = [
    ("/", "home"),
    ("/search", "search"),
    ("/product/AC 010323", "product-detail"),
    ("/compare?ids=1,2,3", "compare"),
    ("/login", "login"),
]

# 阈值 (Google Core Web Vitals)
THRESHOLDS = {
    "lcp_ms": 2500,   # LCP < 2.5s = Good
    "fcp_ms": 1800,   # FCP < 1.8s = Good
    "cls": 0.1,       # CLS < 0.1 = Good
    "load_ms": 3000,  # 全加载 < 3s
}

ACCEPTED_CONSOLE = ["Download the Vue Devtools", "[HMR]", "[vite]", "chrome-extension://", "404"]


async def measure_route(page, route: str, name: str) -> dict:
    """测量单条路由的 web vitals"""
    issues = []
    t0 = datetime.now()
    try:
        # 用 Performance Observer API 抓 web vitals
        await page.goto(f"{BASE}{route}", wait_until="load", timeout=10000)
        # 注入并执行 web vitals 收集
        vitals = await page.evaluate("""() => {
            return new Promise((resolve) => {
                const data = { lcp: null, fcp: null, cls: 0 };
                // FCP via PerformanceObserver
                try {
                    new PerformanceObserver((list) => {
                        for (const entry of list.getEntries()) {
                            if (entry.name === 'first-contentful-paint') {
                                data.fcp = entry.startTime;
                            }
                        }
                    }).observe({ type: 'paint', buffered: true });
                } catch (e) {}
                // LCP via PerformanceObserver
                try {
                    new PerformanceObserver((list) => {
                        for (const entry of list.getEntries()) {
                            data.lcp = entry.startTime;
                        }
                    }).observe({ type: 'largest-contentful-paint', buffered: true });
                } catch (e) {}
                // CLS via PerformanceObserver
                try {
                    let cls = 0;
                    new PerformanceObserver((list) => {
                        for (const entry of list.getEntries()) {
                            if (!entry.hadRecentInput) cls += entry.value;
                        }
                        data.cls = cls;
                    }).observe({ type: 'layout-shift', buffered: true });
                } catch (e) {}
                // 等待 2s 让 LCP/CLS 稳定
                setTimeout(() => resolve(data), 2000);
            });
        }""")
        elapsed_ms = (datetime.now() - t0).total_seconds() * 1000

        # 检查阈值
        lcp = vitals.get("lcp") or 0
        fcp = vitals.get("fcp") or 0
        cls = vitals.get("cls") or 0
        if lcp > THRESHOLDS["lcp_ms"]:
            issues.append(f"LCP 慢: {int(lcp)}ms > {THRESHOLDS['lcp_ms']}ms")
        if fcp > THRESHOLDS["fcp_ms"]:
            issues.append(f"FCP 慢: {int(fcp)}ms > {THRESHOLDS['fcp_ms']}ms")
        if cls > THRESHOLDS["cls"]:
            issues.append(f"CLS 高: {cls:.3f} > {THRESHOLDS['cls']}")
        if elapsed_ms > THRESHOLDS["load_ms"]:
            issues.append(f"全加载慢: {int(elapsed_ms)}ms > {THRESHOLDS['load_ms']}ms")

        return {
            "route": route, "name": name,
            "lcp_ms": int(lcp), "fcp_ms": int(fcp),
            "cls": round(cls, 4), "load_ms": int(elapsed_ms),
            "issues": issues,
        }
    except Exception as e:
        return {
            "route": route, "name": name,
            "error": str(e)[:200],
            "issues": [f"页面加载异常: {str(e)[:100]}"],
        }


async def main():
    print("=" * 60)
    print(f"  性能 + a11y 审计  ({datetime.now().strftime('%H:%M:%S')})")
    print(f"  阈值: LCP<{THRESHOLDS['lcp_ms']}ms FCP<{THRESHOLDS['fcp_ms']}ms CLS<{THRESHOLDS['cls']}")
    print("=" * 60)
    results = []
    console_total = 0
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context(viewport={"width": 1440, "height": 900})
        page = await context.new_page()

        def on_console(msg):
            nonlocal console_total
            if msg.type in ("error", "warning"):
                if any(p in msg.text for p in ACCEPTED_CONSOLE): return
                console_total += 1
        page.on("console", on_console)

        for route, name in ROUTES:
            r = await measure_route(page, route, name)
            if "error" in r:
                icon = "FAIL"
                print(f"  [{icon:>4}] {r['name']:20}  ERROR {r['error'][:60]}")
            else:
                icon = "OK" if not r["issues"] else "WARN"
                print(f"  [{icon:>4}] {r['name']:20}  LCP={r['lcp_ms']}ms  FCP={r['fcp_ms']}ms  CLS={r['cls']:.3f}  load={r['load_ms']}ms")
                for iss in r["issues"]:
                    print(f"         {iss}")
            results.append(r)

        await browser.close()

    fail = sum(1 for r in results if "error" in r)
    warn = sum(1 for r in results if r.get("issues") and "error" not in r)
    summary = {
        "ts": datetime.now().isoformat(),
        "thresholds": THRESHOLDS,
        "total": len(results),
        "ok": len(results) - fail - warn,
        "warn": warn,
        "fail": fail,
        "console_issues": console_total,
        "exit_code": 2 if fail else (1 if (warn or console_total) else 0),
    }
    report = {"summary": summary, "results": results}
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"\n=== 汇总 ===")
    print(f"OK: {summary['ok']}  WARN: {summary['warn']}  FAIL: {summary['fail']}  console: {console_total}")
    print(f"报告: {OUT.relative_to(ROOT)}")
    sys.exit(summary["exit_code"])


if __name__ == "__main__":
    asyncio.run(main())
