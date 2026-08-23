"""
SakuraFilter 无障碍 (A11y) 自动审计 — 集成 axe-core
====================================================

WHAT:
  使用 axe-core (业界标准 WCAG 2.1 AA 自动审计引擎) 扫描所有公开页
  + 后台关键页, 输出 violation 列表.

WHY:
  - WCAG 2.1 AA 是行业默认标准 (政府/医疗系统必须)
  - 手动 A11y 检查不可扩展, 自动化是基础
  - axe-core 覆盖 ~57 类 A11y 规则, 比手工检查完整得多

HOW:
  - Playwright 加载页面后, evaluate() 注入 axe-core source
  - 调用 axe.run() 获取 violations JSON
  - 按 impact 等级 (critical/serious/moderate/minor) 分类
  - 报告输出: spike-test/a11y_audit_report.{json,md}

退出码:
  - 0: 全部页面 0 critical/serious violation
  - 1: 有 critical/serious violation

依赖:
  - axe-core source 下载到 spike-test/axe.min.js (一次下载, 离线使用)
"""
import json
import sys
import time
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from playwright.sync_api import sync_playwright, Page

BACKEND = "http://localhost:5148"
FRONTEND = "http://localhost:5173"
SCRIPT_DIR = Path(__file__).resolve().parent
AXE_FILE = SCRIPT_DIR / "axe.min.js"
REPORT_JSON = SCRIPT_DIR / "a11y_audit_report.json"
REPORT_MD = SCRIPT_DIR / "a11y_audit_report.md"

# axe-core CDN URL (离线优先: 优先用本地 axe.min.js, 不存在则下载)
AXE_CDN = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.0/axe.min.js"


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def ensure_axe() -> str:
    """
    确保 axe-core 源码可用, 返回 JS 源码字符串.
    策略: 本地优先 (离线), 不存在则从 CDN 下载.
    """
    if AXE_FILE.exists() and AXE_FILE.stat().st_size > 100_000:
        return AXE_FILE.read_text(encoding="utf-8")

    print(f"  下载 axe-core from {AXE_CDN}...")
    try:
        with urllib.request.urlopen(AXE_CDN, timeout=30) as r:
            js = r.read().decode("utf-8")
        AXE_FILE.write_text(js, encoding="utf-8")
        print(f"  ✓ 已保存 {AXE_FILE.name} ({len(js):,} bytes)")
        return js
    except Exception as e:
        raise RuntimeError(f"无法下载 axe-core: {e}. 请手动将 axe.min.js 放到 {AXE_FILE}")


# 公开页 + 后台关键页
PAGES_TO_TEST = [
    # (path, label, requires_auth)
    ("/", "首页", False),
    ("/search", "公开搜索", False),
    ("/public/search", "公开 8 字段搜索", False),
    ("/compare?ids=1,2,3", "公开对比", False),
    ("/login", "登录页", False),
    ("/admin/products", "后台产品列表", True),
    ("/admin/etl", "后台 ETL", True),
    ("/admin/help", "后台帮助", True),
]


results = {
    "generatedAt": now_iso(),
    "summary": {"total": 0, "passed": 0, "failed": 0, "warned": 0,
                "violations_critical": 0, "violations_serious": 0,
                "violations_moderate": 0, "violations_minor": 0},
    "pages": [],
    "violations": []
}


def record_page(label, path, requires_auth, violations, axe_runtime_ms, error=None):
    """记录一个页面的 A11y 审计结果"""
    # 按 impact 分组
    by_impact = {"critical": [], "serious": [], "moderate": [], "minor": []}
    for v in violations:
        impact = v.get("impact", "minor")
        by_impact[impact].append({
            "id": v.get("id"),
            "description": v.get("description"),
            "help": v.get("help"),
            "helpUrl": v.get("helpUrl"),
            "nodes": len(v.get("nodes", [])),
            "targets": [n.get("target", []) for n in v.get("nodes", [])][:3]
        })

    page_result = {
        "label": label,
        "path": path,
        "requires_auth": requires_auth,
        "axe_runtime_ms": axe_runtime_ms,
        "violations_count": len(violations),
        "by_impact": {k: len(v) for k, v in by_impact.items()},
        "violations": by_impact,
        "error": error
    }
    results["pages"].append(page_result)

    # 全局统计
    results["summary"]["total"] += 1
    if error:
        results["summary"]["failed"] += 1
        return

    # 判定: critical/serious = FAIL, moderate = WARN, minor = PASS
    if by_impact["critical"] or by_impact["serious"]:
        results["summary"]["failed"] += 1
    elif by_impact["moderate"]:
        results["summary"]["warned"] += 1
    else:
        results["summary"]["passed"] += 1

    for impact, items in by_impact.items():
        results["summary"][f"violations_{impact}"] += len(items)

    # 收集所有 violations 到全局列表
    for v in violations:
        results["violations"].append({
            "page": label,
            "path": path,
            "impact": v.get("impact"),
            "id": v.get("id"),
            "help": v.get("help")
        })


def login(page: Page):
    """后台测试前的管理员登录"""
    page.goto(f"{FRONTEND}/login", wait_until="networkidle", timeout=15000)
    page.fill("input[autocomplete='username']", "admin")
    page.fill("input[autocomplete='current-password']", "Admin@2026")
    # LoginView 使用 el-button (非 submit), 用文字定位
    page.locator("button:has-text('登录')").first.click()
    page.wait_for_url(f"{FRONTEND}/admin/**", timeout=10000)


def run_axe_on_page(page: Page, axe_js: str):
    """在当前页面执行 axe.run() 并返回 violations JSON"""
    # 注入 axe-core 源码
    page.evaluate(axe_js)
    # 执行审计, 配置为 WCAG 2.1 AA (默认)
    start = time.time()
    result = page.evaluate("""async () => {
        return await axe.run({
            runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'best-practice'] }
        });
    }""")
    elapsed_ms = int((time.time() - start) * 1000)
    return result.get("violations", []), elapsed_ms


def main():
    print("=" * 70)
    print(" SakuraFilter 无障碍审计 (axe-core WCAG 2.1 AA)")
    print(f" Frontend: {FRONTEND}")
    print(f" 规则集:   wcag2a, wcag2aa, wcag21a, wcag21aa, best-practice")
    print("=" * 70)

    axe_js = ensure_axe()

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(viewport={"width": 1280, "height": 800})
        page = context.new_page()

        # 公共页
        for path, label, requires_auth in PAGES_TO_TEST:
            print(f"\n  [{label}] {path}")
            try:
                if requires_auth:
                    login(page)
                page.goto(f"{FRONTEND}{path}", wait_until="networkidle", timeout=15000)
                time.sleep(1)
                violations, elapsed = run_axe_on_page(page, axe_js)
                print(f"    violations: {len(violations)} (耗时 {elapsed}ms)")
                by_impact = {}
                for v in violations:
                    impact = v.get("impact", "minor")
                    by_impact[impact] = by_impact.get(impact, 0) + 1
                if by_impact:
                    print(f"    impact: {by_impact}")
                    # 打印前 3 条
                    for v in violations[:3]:
                        print(f"      [{v.get('impact')}] {v.get('id')}: {v.get('help', '')[:80]}")
                record_page(label, path, requires_auth, violations, elapsed)
            except Exception as e:
                print(f"    [ERROR] {e}")
                record_page(label, path, requires_auth, [], 0, error=str(e))

        context.close()
        browser.close()

    # 写报告
    REPORT_JSON.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    # Markdown 报告
    md = ["# SakuraFilter 无障碍审计报告 (axe-core WCAG 2.1 AA)\n"]
    md.append(f"**生成时间**: {results['generatedAt']}  ")
    s = results["summary"]
    md.append(f"**页面数**: {s['total']}  ")
    md.append(f"**PASS**: {s['passed']} | **FAIL**: {s['failed']} | **WARN**: {s['warned']}  ")
    md.append(f"**Violations**: critical={s['violations_critical']} serious={s['violations_serious']} "
              f"moderate={s['violations_moderate']} minor={s['violations_minor']}\n")
    md.append("## 页面详情\n")
    for p in results["pages"]:
        icon = "✗" if p.get("error") or p["by_impact"]["critical"] or p["by_impact"]["serious"] else (
               "!" if p["by_impact"]["moderate"] else "✓")
        md.append(f"### {icon} {p['label']} `{p['path']}`")
        md.append(f"- 耗时: {p['axe_runtime_ms']}ms")
        if p.get("error"):
            md.append(f"- 错误: `{p['error']}`")
        else:
            md.append(f"- Violations: {p['violations_count']} "
                      f"(critical={p['by_impact']['critical']} serious={p['by_impact']['serious']} "
                      f"moderate={p['by_impact']['moderate']} minor={p['by_impact']['minor']})")
            for impact in ["critical", "serious", "moderate", "minor"]:
                for v in p["violations"].get(impact, []):
                    md.append(f"  - [{impact}] **{v['id']}** — {v['help']} ({v['nodes']} nodes)")
                    md.append(f"    - 修复: {v['helpUrl']}")
        md.append("")

    REPORT_MD.write_text("\n".join(md), encoding="utf-8")

    print("\n" + "=" * 70)
    print(f" 汇总: 页面 {s['total']}, FAIL={s['failed']} WARN={s['warned']} PASS={s['passed']}")
    print(f"       Violations: critical={s['violations_critical']} serious={s['violations_serious']} "
          f"moderate={s['violations_moderate']} minor={s['violations_minor']}")
    print("=" * 70)
    print(f"\n 报告:")
    print(f"   - {REPORT_JSON}")
    print(f"   - {REPORT_MD}")

    sys.exit(1 if s["failed"] > 0 else 0)


if __name__ == "__main__":
    main()
