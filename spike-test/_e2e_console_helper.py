"""
Playwright Console 断言辅助器
=============================
WHY: 现有 E2E 测试只验证 DOM/API, 不捕获 console 错误,
  导致 Vue 警告 (未注册组件 / 缺 i18n key) 在测试中不浮现
  直到人工点击才发现.

用法:
  from _e2e_console_helper import attach_console_asserts

  async with async_playwright() as p:
      browser = await p.chromium.launch()
      page = await browser.new_page()
      attach_console_asserts(page)
      ...
      # 测试结束前断言
      assert_console_clean(page)  # 失败抛出 AssertionError 含全部错误细节
"""
from typing import List, Dict
from playwright.async_api import Page, ConsoleMessage, Error

# 可接受的"已知"警告 (开发环境固有, 不算 bug)
ACCEPTED_PATTERNS = [
    "Download the Vue Devtools",
    "[HMR]",
    "[vite]",
    "chrome-extension://",
]


def is_accepted(text: str) -> bool:
    return any(pat in text for pat in ACCEPTED_PATTERNS)


def attach_console_asserts(page: Page) -> List[Dict]:
    """
    给 page 附加 console + pageerror 监听器
    返回错误列表 (供测试结束前断言)
    """
    issues: List[Dict] = []

    def on_console(msg: ConsoleMessage):
        if msg.type in ("error", "warning"):
            text = msg.text
            if is_accepted(text):
                return
            issues.append({
                "type": msg.type,
                "text": text,
                "location": msg.location,
            })

    def on_pageerror(err: Error):
        text = str(err)
        if is_accepted(text):
            return
        issues.append({
            "type": "pageerror",
            "text": text,
            "location": {},
        })

    page.on("console", on_console)
    page.on("pageerror", on_pageerror)
    return issues


def assert_console_clean(page: Page, issues: List[Dict], allow_404: bool = False, allow_401: bool = False, allow_401_patterns: List[str] = None):
    """
    断言控制台干净
    Args:
        page: Playwright Page (用于在失败时附加截图)
        issues: attach_console_asserts 返回的列表
        allow_404: 是否允许 404 错误 (用于负面用例)
        allow_401: 是否允许 401 错误 (默认 False, 因 401 可能是真实安全问题)
        allow_401_patterns: 允许 401 的 URL 子串列表 (精确控制, 如 ['/api/auth/login', '/api/admin/etl/progress/stream'])
    Raises:
        AssertionError: 包含全部错误细节
    """
    filtered: List[Dict] = []
    allow_401_patterns = allow_401_patterns or []
    for x in issues:
        text = x["text"]
        if allow_404 and "404" in text:
            continue
        if "401" in text or "Unauthorized" in text:
            if not allow_401:
                # 完全不允许 401, 一律算 FAIL
                filtered.append(x)
                continue
            # 仅当 URL 命中白名单才放行
            url = x.get("location", {}).get("url", "")
            if any(pat in url for pat in allow_401_patterns):
                continue
            filtered.append(x)
            continue
        filtered.append(x)

    if filtered:
        lines = [f"控制台发现 {len(filtered)} 条问题:"]
        for x in filtered[:20]:
            loc = x.get("location", {}).get("url", "")
            lines.append(f"  [{x['type']}] {x['text'][:150]}  ({loc})")
        if len(filtered) > 20:
            lines.append(f"  ... 还有 {len(filtered) - 20} 条")
        raise AssertionError("\n".join(lines))
