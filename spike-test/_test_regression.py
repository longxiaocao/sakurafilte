"""P1/P2/P3 修复回归测试 - 防止已修复问题再次引入

用法:
  python _test_regression.py          # 完整扫描 (代码层 grep + 健康检查)
  python _test_regression.py --scan   # 仅扫描代码层, 不调 API

设计:
  - 阶段 1 SCAN: grep 验证 P1/P2/P3 修复模式存在 (14 个修复点)
  - 阶段 2 HEALTH: 系统健康检查 (4 个端点)
  - 阶段 3 REPORT: 汇总报告 + 退出码 (0=全绿, 1=有回归)
  - 与 _test_p0_fixes.py 互补: P0 用 API 验证, P1/P2/P3 用 grep 验证 (大多无 API 表面)
"""
import re
import sys
import time
import urllib.request
import urllib.error
from pathlib import Path

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"Content-Type": "application/json", "X-Admin-Token": TOKEN}
REPO = Path(__file__).resolve().parent.parent

# P1/P2/P3 修复点清单 (id, level, title, file, fix_pattern)
#   fix_pattern: 修复后代码应包含的正则 (匹配 = 已修复, 不匹配 = 回归)
REGRESSION_CHECKS = [
    # ===== P1 严重级 (6 项) =====
    {
        "id": "P1-1a", "level": "P1",
        "title": "IndexReplayWorker N+1 查询 (批量预拉候选)",
        "file": "backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs",
        "fix_pattern": r"批量预拉候选.*existingDict",
    },
    {
        "id": "P1-1b", "level": "P1",
        "title": "OemBrandDictService N+1 查询 (GroupBy 聚合)",
        "file": "backend/src/SakuraFilter.Api/Services/OemBrandDictService.cs",
        "fix_pattern": r"GroupBy\(x => x\.OemBrand\)",
    },
    {
        "id": "P1-2", "level": "P1",
        "title": "Token 比较用 FixedTimeEquals (时序攻击防护)",
        "file": "backend/src/SakuraFilter.Api/Services/DevTokenAuthMiddleware.cs",
        "fix_pattern": r"CryptographicOperations\.FixedTimeEquals",
    },
    {
        "id": "P1-3", "level": "P1",
        "title": "限流 ForwardedHeaders 中间件",
        "file": "backend/src/SakuraFilter.Api/Program.cs",
        "fix_pattern": r"app\.UseForwardedHeaders",
    },
    {
        "id": "P1-4", "level": "P1",
        "title": "前端 debounceTimer onUnmounted 清理",
        "file": "frontend/src/views/public/PublicSearchView.vue",
        "fix_pattern": r"onUnmounted\(\(\) => \{[^}]*clearTimeout\(debounceTimer\)",
    },
    {
        "id": "P1-5", "level": "P1",
        "title": "生产环境 UseExceptionHandler",
        "file": "backend/src/SakuraFilter.Api/Program.cs",
        "fix_pattern": r"app\.UseExceptionHandler",
    },
    {
        "id": "P1-6", "level": "P1",
        "title": "Swagger 仅 Development 暴露",
        "file": "backend/src/SakuraFilter.Api/Program.cs",
        "fix_pattern": r"if \(app\.Environment\.IsDevelopment\(\)\)\s*\{[^}]*UseSwagger",
    },
    # ===== P2 中等级 (6 项) =====
    {
        "id": "P2-1", "level": "P2",
        "title": "AdminProductService Oem2 搜索用 ILike + EscapeLikePattern",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r"EF\.Functions\.ILike\(p\.OemNoDisplay.*EscapeLikePattern",
    },
    {
        "id": "P2-2", "level": "P2",
        "title": "ValidateForm 13 字段长度校验",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r"var checks = new \(string Label, string\? Value, int Max\)\[\]",
    },
    {
        "id": "P2-3", "level": "P2",
        "title": "PublicProductController slug 长度校验 (200 字符)",
        "file": "backend/src/SakuraFilter.Api/Controllers/PublicProductController.cs",
        "fix_pattern": r"slug\.Length > 200",
    },
    {
        "id": "P2-4", "level": "P2",
        "title": "perf.ts fetch 调用注释说明 keepalive",
        "file": "frontend/src/utils/perf.ts",
        "fix_pattern": r"keepalive: true.*ingest.*已豁免",
    },
    {
        "id": "P2-5", "level": "P2",
        "title": "AuthTokenBroadcaster Dispose 后置 null",
        "file": "backend/src/SakuraFilter.Api/Services/AuthTokenBroadcaster.cs",
        "fix_pattern": r"Dispose 后置 null.*防止外部访问已 Dispose 对象",
    },
    {
        "id": "P2-7", "level": "P2",
        "title": "perf.ts uninstallPerfInterceptor 导出",
        "file": "frontend/src/utils/perf.ts",
        "fix_pattern": r"export function uninstallPerfInterceptor",
    },
    # ===== P3 轻微级 (2 项, P3-3/4/5 跳过) =====
    {
        "id": "P3-1", "level": "P3",
        "title": "EtlProgressBroadcaster 参数化 pg_notify",
        "file": "backend/src/SakuraFilter.Api/Services/EtlProgressBroadcaster.cs",
        "fix_pattern": r"SELECT pg_notify\(@channel, @payload\)",
    },
    {
        "id": "P3-2", "level": "P3",
        "title": "GetBySlug 3 次 fallback 合并为 1 次 OR 查询",
        "file": "backend/src/SakuraFilter.Api/Controllers/PublicProductController.cs",
        "fix_pattern": r"3 次 fallback 合并为 1 次 OR 查询",
    },
]


def grep_file(file_path, pattern):
    """在文件中搜索正则, 返回匹配数 (-1 = 文件不存在)"""
    try:
        content = Path(REPO / file_path).read_text(encoding="utf-8")
        return len(re.findall(pattern, content, re.MULTILINE | re.DOTALL))
    except FileNotFoundError:
        return -1


def scan_phase():
    """阶段 1: 扫描代码层, 验证修复模式存在"""
    print("\n【阶段 1】扫描修复模式 (代码层 grep)")
    print("-" * 70)
    results = []
    for check in REGRESSION_CHECKS:
        cnt = grep_file(check["file"], check["fix_pattern"])
        ok = cnt > 0
        results.append((check, ok, cnt))
        status = "[OK]  " if ok else "[FAIL]"
        print(f"  {status} {check['id']:<6} [{check['level']}] {check['title']}")
        if ok:
            print(f"         修复模式匹配 {cnt} 处")
        else:
            print(f"         ⚠ 回归! 文件: {check['file']}")
    return results


def health_phase():
    """阶段 2: 系统健康检查"""
    print("\n【阶段 2】系统健康检查")
    print("-" * 70)
    endpoints = [
        ("/health/live", "存活探针"),
        ("/health/ready", "就绪探针"),
        ("/api/search/health", "Meilisearch 健康"),
        ("/api/admin/dict/oem-no3s?limit=5", "dict_oem_no3 (P0-1 验证)"),
    ]
    ok_count = 0
    for path, desc in endpoints:
        url = f"{BASE}{path}"
        req = urllib.request.Request(url, headers=HEADERS, method="GET")
        start = time.time()
        try:
            with urllib.request.urlopen(req, timeout=5) as r:
                elapsed = time.time() - start
                print(f"  [OK]  GET  {path:<40} HTTP {r.status} time={elapsed:.2f}s  {desc}")
                ok_count += 1
        except urllib.error.HTTPError as e:
            elapsed = time.time() - start
            print(f"  [FAIL] GET {path:<40} HTTP {e.code} time={elapsed:.2f}s  {desc}")
        except Exception as e:
            print(f"  [FAIL] GET {path:<40} ERR {type(e).__name__}: {e}")
    return ok_count, len(endpoints)


def report_phase(scan_results, health_ok, health_total):
    """阶段 3: 汇总报告"""
    print("\n" + "=" * 70)
    print("【汇总报告】")
    print("=" * 70)
    scan_ok = sum(1 for _, ok, _ in scan_results if ok)
    scan_fail = len(scan_results) - scan_ok
    by_level = {}
    for check, ok, _ in scan_results:
        level = check["level"]
        if level not in by_level:
            by_level[level] = {"ok": 0, "fail": 0}
        if ok:
            by_level[level]["ok"] += 1
        else:
            by_level[level]["fail"] += 1

    print(f"  扫描: {scan_ok}/{len(scan_results)} 修复模式有效, {scan_fail} 回归")
    for level in ["P1", "P2", "P3"]:
        if level in by_level:
            s = by_level[level]
            print(f"    {level}: {s['ok']} 通过 / {s['fail']} 回归", end="")
            if s["fail"] > 0:
                print("  ⚠ 需修复")
            else:
                print()
    print(f"  健康: {health_ok}/{health_total} 端点正常")
    print()
    if scan_fail == 0 and health_ok == health_total:
        print("  [RESULT] 全部回归测试通过, P1/P2/P3 修复有效")
        return 0
    else:
        print("  [RESULT] ⚠ 存在回归, 需立即修复!")
        return 1


def main():
    scan_only = "--scan" in sys.argv
    print("=" * 70)
    print("P1/P2/P3 修复回归测试 (防再次引入)")
    print("=" * 70)

    scan_results = scan_phase()
    if scan_only:
        health_ok = health_total = 0
        print("\n  [--scan 模式] 跳过健康检查")
    else:
        health_ok, health_total = health_phase()

    exit_code = report_phase(scan_results, health_ok, health_total)
    sys.exit(exit_code)


if __name__ == "__main__":
    main()
