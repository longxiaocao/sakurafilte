"""P0 修复回归测试 + 标准化问题修复循环脚本

用法:
  python _test_p0_fixes.py          # 验证所有 P0 修复
  python _test_p0_fixes.py --scan   # 扫描已知问题状态

流程 (问题验证-修复实施-测试验证-迭代优化):
  1. SCAN: 扫描代码,确认问题是否存在
  2. VERIFY: API 调用验证修复效果
  3. REPORT: 输出修复状态报告
  4. ITERATE: 未通过的问题进入下一轮修复
"""
import json
import re
import subprocess
import sys
import time
import urllib.request
import urllib.error
from pathlib import Path

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"Content-Type": "application/json", "X-Admin-Token": TOKEN}
REPO = Path(__file__).resolve().parent.parent

# 问题清单 (P0 已修复, P1+ 待处理)
ISSUES = [
    # P0 阻塞级 (已修复)
    {"id": "P0-1", "level": "P0", "title": "BaseDictService.ListAsync 无默认 limit OOM",
     "file": "backend/src/SakuraFilter.Api/Services/BaseDictService.cs",
     "pattern": r"if \(limit\.HasValue && limit\.Value > 0\)\s*\n\s*query = query\.Take\(limit\.Value\);",
     "fix_pattern": r"var effectiveLimit = \(limit\.HasValue && limit\.Value > 0\) \? limit\.Value : 200;",
     "verify": "dict_oem_no3_api",
     "status": "fixed"},
    {"id": "P0-2", "level": "P0", "title": "ProblemDetailsFactory 500 泄露 ex.Message",
     "file": "backend/src/SakuraFilter.Api/Services/ProblemDetailsFactory.cs",
     "pattern": r"detail: ex\.Message,\s*\n\s*statusCode: StatusCodes\.Status500InternalServerError",
     "fix_pattern": r'detail: "服务内部错误,请联系管理员"',
     "verify": "problem_details_500",
     "status": "fixed"},
    {"id": "P0-3", "level": "P0", "title": "硬编码 PG 连接串含密码",
     "file": "backend/src/SakuraFilter.Api/Program.cs",
     "pattern": r'\?\? "Host=localhost;Port=5432;Database=spike_test_v3;Username=postgres;Password=784533"',
     "fix_pattern": r'\?\? throw new InvalidOperationException\("ConnectionStrings:Postgres 未配置',
     "verify": "no_hardcoded_password",
     "status": "fixed"},
    # P1 严重级 (待处理)
    {"id": "P1-1", "level": "P1", "title": "N+1 查询风险", "status": "pending"},
    {"id": "P1-2", "level": "P1", "title": "Token 比较未用 FixedTimeEquals (时序攻击)", "status": "pending"},
    {"id": "P1-3", "level": "P1", "title": "限流缺 ForwardedHeaders", "status": "pending"},
    {"id": "P1-4", "level": "P1", "title": "前端 debounceTimer 未清理", "status": "pending"},
    {"id": "P1-5", "level": "P1", "title": "生产环境无 UseExceptionHandler", "status": "pending"},
    {"id": "P1-6", "level": "P1", "title": "Swagger 生产环境暴露", "status": "pending"},
]


def curl(method, path, body=None, timeout=10):
    """API 调用, 返回 (status_code, body, elapsed_sec)"""
    url = f"{BASE}{path}"
    data = json.dumps(body).encode("utf-8") if body else None
    req = urllib.request.Request(url, data=data, headers=HEADERS, method=method)
    start = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read().decode("utf-8", errors="replace"), time.time() - start
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", errors="replace"), time.time() - start
    except Exception as e:
        return 0, str(e), time.time() - start


def grep_file(file_path, pattern):
    """在文件中搜索正则, 返回匹配数"""
    try:
        content = Path(REPO / file_path).read_text(encoding="utf-8")
        return len(re.findall(pattern, content))
    except FileNotFoundError:
        return -1  # 文件不存在


def grep_dir(dir_rel, pattern, glob_pattern="*.cs"):
    """在目录中搜索, 返回匹配文件数"""
    try:
        result = subprocess.run(
            ["rg", "-l", pattern, str(REPO / dir_rel), "-g", glob_pattern],
            capture_output=True, text=True, timeout=10
        )
        return len(result.stdout.strip().split("\n")) if result.stdout.strip() else 0
    except Exception:
        return -1


# ========== 验证函数 ==========
def verify_dict_oem_no3_api():
    """P0-1: dict_oem_no3 API 不带 limit 应在 3s 内返回 (原 11s)"""
    code, body, elapsed = curl("GET", "/api/admin/dict/oem-no3s", timeout=15)
    if code != 200:
        return False, f"HTTP {code}"
    if elapsed > 5:
        return False, f"耗时 {elapsed:.2f}s 超过 5s 阈值"
    # 验证返回数据量 <= 200 (默认 limit)
    try:
        data = json.loads(body)
        count = data.get("count", 0)
        if count > 200:
            return False, f"返回 {count} 条, 超过默认 limit=200"
    except json.JSONDecodeError:
        return False, "响应非 JSON"
    return True, f"HTTP {code} time={elapsed:.2f}s count={count}"


def verify_problem_details_500():
    """P0-2: ProblemDetailsFactory 500 兜底不含 ex.Message"""
    # 代码审查: 检查 500 分支的 detail
    count = grep_file("backend/src/SakuraFilter.Api/Services/ProblemDetailsFactory.cs",
                      r'detail: "服务内部错误,请联系管理员"')
    if count == 0:
        return False, "未找到通用错误提示"
    # 检查 ex.Message 不在 500 分支
    content = Path(REPO / "backend/src/SakuraFilter.Api/Services/ProblemDetailsFactory.cs").read_text(encoding="utf-8")
    # 找 500 分支上下文
    match = re.search(r'_ => Results\.Problem\([^)]+\)', content, re.DOTALL)
    if match and "ex.Message" in match.group():
        return False, "500 分支仍含 ex.Message"
    return True, "500 兜底使用通用提示, 不泄露 ex.Message"


def verify_no_hardcoded_password():
    """P0-3: .cs 文件中无 Password=784533 硬编码"""
    count = grep_dir("backend/src", r'Password=784533', "*.cs")
    if count == 0:
        return True, ".cs 文件中 0 处硬编码密码"
    return False, f".cs 文件中仍有 {count} 处硬编码密码"


VERIFIERS = {
    "dict_oem_no3_api": verify_dict_oem_no3_api,
    "problem_details_500": verify_problem_details_500,
    "no_hardcoded_password": verify_no_hardcoded_password,
}


def run_verification(verify_name):
    """执行单个验证"""
    fn = VERIFIERS.get(verify_name)
    if not fn:
        return False, f"未知验证器: {verify_name}"
    try:
        return fn()
    except Exception as e:
        return False, f"验证异常: {e}"


def scan_issue(issue):
    """扫描单个问题状态"""
    if issue.get("status") != "fixed":
        return "pending", "待处理"

    file_path = issue.get("file")
    pattern = issue.get("pattern")
    fix_pattern = issue.get("fix_pattern")

    if not file_path or not pattern:
        return "unknown", "无扫描规则"

    # 1. 检查原问题是否还存在
    old_count = grep_file(file_path, pattern)
    # 2. 检查修复是否已应用
    new_count = grep_file(file_path, fix_pattern) if fix_pattern else 1

    if old_count == 0 and new_count > 0:
        return "fixed", "原问题已消除, 修复已应用"
    elif old_count > 0:
        return "regressed", f"原问题仍存在 ({old_count} 处匹配)"
    else:
        return "unknown", f"无法判定 (old={old_count} new={new_count})"


def main():
    scan_only = "--scan" in sys.argv

    print("=" * 70)
    print("P0 修复回归测试 + 问题状态扫描")
    print("=" * 70)

    # ===== 阶段 1: 扫描问题状态 =====
    print("\n【阶段 1】扫描问题状态 (代码层)")
    print("-" * 70)
    scan_results = []
    for issue in ISSUES:
        status, msg = scan_issue(issue)
        scan_results.append((issue["id"], issue["level"], status, msg, issue["title"]))
        emoji = {"fixed": "[OK]", "regressed": "[FAIL]", "pending": "[WAIT]", "unknown": "[?]"}[status]
        print(f"  {emoji} {issue['id']:6s} [{issue['level']}] {issue['title']}")
        print(f"         {msg}")

    if scan_only:
        print("\n" + "=" * 70)
        print("扫描完成 (--scan 模式, 跳过 API 验证)")
        return

    # ===== 阶段 2: API 验证 =====
    print("\n【阶段 2】API 验证 (修复效果)")
    print("-" * 70)
    verify_results = []
    for issue in ISSUES:
        verify_name = issue.get("verify")
        if not verify_name:
            continue
        ok, msg = run_verification(verify_name)
        verify_results.append((issue["id"], ok, msg))
        emoji = "[OK] " if ok else "[FAIL]"
        print(f"  {emoji} {issue['id']:6s} {verify_name}")
        print(f"         {msg}")

    # ===== 阶段 3: 系统健康检查 =====
    print("\n【阶段 3】系统健康检查")
    print("-" * 70)
    health_checks = [
        ("GET", "/health/live", None, 3),
        ("GET", "/health/ready", None, 3),
        ("GET", "/api/search/health", None, 5),
        ("GET", "/api/admin/dict/oem-no3s?limit=5", None, 5),
    ]
    all_healthy = True
    for method, path, body, timeout in health_checks:
        code, _, elapsed = curl(method, path, body, timeout)
        ok = 200 <= code < 300
        if not ok:
            all_healthy = False
        emoji = "[OK] " if ok else "[FAIL]"
        print(f"  {emoji} {method:4s} {path:40s} HTTP {code} time={elapsed:.2f}s")

    # ===== 阶段 4: 汇总报告 =====
    print("\n" + "=" * 70)
    print("【汇总报告】")
    print("=" * 70)

    fixed_count = sum(1 for r in scan_results if r[2] == "fixed")
    regressed_count = sum(1 for r in scan_results if r[2] == "regressed")
    pending_count = sum(1 for r in scan_results if r[2] == "pending")

    verify_pass = sum(1 for r in verify_results if r[1])
    verify_fail = sum(1 for r in verify_results if not r[1])

    print(f"  扫描: {fixed_count} 已修复 / {regressed_count} 回归 / {pending_count} 待处理")
    print(f"  验证: {verify_pass} 通过 / {verify_fail} 失败")
    print(f"  健康: {'全部正常' if all_healthy else '存在问题'}")

    # 退出码: 有回归或验证失败则非零
    if regressed_count > 0 or verify_fail > 0 or not all_healthy:
        print("\n  [RESULT] 存在问题,需要迭代修复")
        sys.exit(1)
    else:
        print("\n  [RESULT] 所有 P0 修复有效,系统健康")
        sys.exit(0)


if __name__ == "__main__":
    main()
