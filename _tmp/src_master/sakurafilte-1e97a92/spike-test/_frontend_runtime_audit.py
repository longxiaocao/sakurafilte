"""
前端运行时控制台审计
=====================
WHY 静态扫描不够: 静态扫只能发现 import/i18n 缺失,
  但 Vue 运行时还会触发:
  - element-plus 警告 (如 Form 组件未传 model)
  - v-for 缺 key
  - 路由懒加载失败
  - 跨域资源加载失败
  - 自定义指令 / mixin 异常
本脚本:
  1. 启动 Playwright Chromium (headless)
  2. 依次访问每条路由, 等待 2s
  3. 收集所有 console.{error,warning} + pageerror
  4. 输出报告, 退出码非 0 = 有问题

退出码: 0 干净 / 1 有 WARN / 2 有 ERROR
"""
import asyncio
import json
import sys
from datetime import datetime
from pathlib import Path
from typing import Dict, List

from playwright.async_api import async_playwright, ConsoleMessage, Error

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "spike-test" / "frontend_runtime_audit.json"

# 待扫描路由 (公开 + 管理)
PUBLIC_ROUTES = [
    "/", "/search", "/login",
    "/product/AC 010323",  # 已知存在
    "/product/DOES_NOT_EXIST_12345",  # 已知 404, 用于测试错误处理
    "/compare?ids=1,2,3",
]

ADMIN_ROUTES = [
    "/admin", "/admin/products", "/admin/etl", "/admin/perf",
    "/admin/dict/type", "/admin/dict/product-name1", "/admin/dict/product-name2",
    "/admin/dict/machine", "/admin/dict/engine", "/admin/dict/media",
    "/admin/dict/oem-brands", "/admin/dict/oem-no3",
    "/admin/users", "/admin/help", "/admin/compare",
]

# 已知可接受的警告 (开发模式固有)
ACCEPTED_PATTERNS = [
    "Download the Vue Devtools",  # Vue 调试工具提示
    "[HMR]",  # 热更新
    "[vite]",  # Vite 自身日志
    "Vue Router warn",  # 路由重复 (已知)
    "chrome-extension://",  # 浏览器扩展
]


def is_accepted(text: str) -> bool:
    return any(pat in text for pat in ACCEPTED_PATTERNS)


async def audit_route(page, route: str, log: List[Dict], expect_404: bool = False) -> Dict:
    """访问一条路由, 返回审计结果"""
    before = len(log)
    try:
        # 设置 5s 超时
        await page.goto(f"http://localhost:5173{route}", wait_until="domcontentloaded", timeout=5000)
        # 等待 2s 让异步警告浮出
        await page.wait_for_timeout(2000)
        # 标记预期 404: 路由内 fetch 引发的 404 都视为预期 (主文档 200 是正常的)
        if expect_404:
            for item in log[before:]:
                if item["type"] in ("error", "pageerror") and "404" in item["text"]:
                    item["is_404"] = True
                    item["expected"] = True
    except Exception as e:
        return {"route": route, "ok": False, "load_error": str(e), "new_issues": []}
    new_issues = log[before:]
    return {"route": route, "ok": True, "new_issues": new_issues}


async def main():
    log: List[Dict] = []
    report_routes: List[Dict] = []

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context()
        page = await context.new_page()

        # 收集 console
        def on_console(msg: ConsoleMessage):
            if msg.type in ("error", "warning"):
                text = msg.text
                if is_accepted(text):
                    return
                log.append({
                    "type": msg.type,
                    "text": text,
                    "location": msg.location,
                    "ts": datetime.now().isoformat(),
                })

        def on_pageerror(err: Error):
            text = str(err)
            if is_accepted(text):
                return
            log.append({
                "type": "pageerror",
                "text": text,
                "location": {},
                "ts": datetime.now().isoformat(),
            })

        page.on("console", on_console)
        page.on("pageerror", on_pageerror)

        # 公开路由 (无需登录)
        # expected_404 标记: 故意触发 404, 验证错误处理路径, 不计入 ERROR
        PUBLIC_ROUTES_EXPECTED_404 = {"/product/DOES_NOT_EXIST_12345"}
        print(f"=== 扫描公开路由 ({len(PUBLIC_ROUTES)}) ===")
        for r in PUBLIC_ROUTES:
            is_expected_404 = r in PUBLIC_ROUTES_EXPECTED_404
            rpt = await audit_route(page, r, log, expect_404=is_expected_404)
            issue_count = sum(
                1 for x in rpt.get("new_issues", [])
                if not (is_expected_404 and x.get("is_404"))
            )
            tag = "OK" if issue_count == 0 else f"!!{issue_count}"
            print(f"  [{tag:>4}] {r}{' (预期 404)' if is_expected_404 else ''}")
            report_routes.append(rpt)

        # 管理路由 (需登录) — 简化为只访问 /admin, 看看重定向
        print(f"\n=== 扫描管理路由重定向 ({len(ADMIN_ROUTES)}) ===")
        for r in ADMIN_ROUTES:
            rpt = await audit_route(page, r, log)
            issue_count = len(rpt.get("new_issues", []))
            tag = "OK" if issue_count == 0 else f"!!{issue_count}"
            print(f"  [{tag:>4}] {r}")
            report_routes.append(rpt)

        await browser.close()

    # === 汇总 (排除 expected 404) ===
    err_count = sum(1 for x in log if x["type"] in ("error", "pageerror") and not x.get("expected"))
    warn_count = sum(1 for x in log if x["type"] == "warning" and not x.get("expected"))
    by_text: Dict[str, int] = {}
    for x in log:
        # 提取关键文本 (前 80 字符)
        key = x["text"][:80]
        by_text[key] = by_text.get(key, 0) + 1

    summary = {
        "total_issues": len(log),
        "errors": err_count,
        "warnings": warn_count,
        "exit_code": 2 if err_count else (1 if warn_count else 0),
        "issue_groups": sorted(
            [{"text": k, "count": v} for k, v in by_text.items()],
            key=lambda x: -x["count"]
        )[:20],
    }

    report = {
        "summary": summary,
        "routes": report_routes,
        "all_issues": log,
    }
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"\n=== 汇总 ===")
    print(f"ERROR: {err_count}  WARN: {warn_count}")
    if log:
        print(f"\n问题分组 (top 20):")
        for g in summary["issue_groups"][:20]:
            print(f"  [{g['count']:>3}] {g['text']}")
    print(f"\n报告: {OUT.relative_to(ROOT)}")

    sys.exit(summary["exit_code"])


if __name__ == "__main__":
    asyncio.run(main())
