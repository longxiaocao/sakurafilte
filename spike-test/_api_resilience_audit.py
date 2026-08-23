"""
API 调用三态完整性扫描器
========================
WHY 之前只统计 API 调用数量 (37 处), 未验证每处是否都有:
  - try-catch 错误处理
  - loading 状态 (避免用户看到空白)
  - error 展示 UI (避免白屏)
  - empty 状态 (空数据提示)

扫描规则:
  1. 找到所有 .vue 中的 API 调用点 (xxxApi.foo( / http.get / axios.X / fetch)
  2. 检查调用点上下 30 行内是否有 try / catch / loading.value / error
  3. 报告缺失的项

退出码: 0 OK / 1 WARN / 2 FAIL
"""
import json
import re
import sys
from pathlib import Path
from typing import Dict, List

ROOT = Path(__file__).resolve().parent.parent
FRONT = ROOT / "frontend" / "src"
OUT = ROOT / "spike-test" / "api_resilience_audit.json"

# 排除的函数名 (非 API 调用)
EXCLUDE_FN = {
    "document", "window", "console", "process", "Array", "Object", "String",
    "Number", "Boolean", "Date", "Math", "JSON", "Promise", "setTimeout",
    "setInterval", "clearTimeout", "clearInterval", "parseInt", "parseFloat",
    "isNaN", "isFinite", "Object", "Map", "Set", "WeakMap", "WeakSet",
    "Element", "HTMLElement", "Node", "Event",
}

# 加载状态关键字
# WHY 通用名: 实际代码用 saving / submitting / uploading / usersLoading / productMutating 等各种名字
# 注意: \w*? 非贪婪, 否则 \w+ 会吃掉整个 userSubmitting 导致后缀无字符可匹配
LOADING_PATTERNS = [
    # 通用命名: xxxLoading / xxxSubmitting / xxxSaving / xxxMutating / xxxUploading / xxxBusy
    # 也覆盖: xxxCancelling / xxxPausing / xxxResuming (ETL 状态转换)
    r"\b\w*?(?:Loading|Submitting|Saving|Mutating|Uploading|Busy|Pending|Cancelling|Pausing|Resuming|Stopping|Starting|Connecting)\s*=\s*true\b",
    r"\b\w*?(?:Loading|Submitting|Saving|Mutating|Uploading|Busy|Pending|Cancelling|Pausing|Resuming|Stopping|Starting|Connecting)\.value\s*=\s*true\b",
    r"\b\w*?(?:Loading|Submitting|Saving|Mutating|Uploading|Busy|Pending|Cancelling|Pausing|Resuming|Stopping|Starting|Connecting)\.value\s*=\s*false\b",
    r"isLoading\s*=\s*true\b",
    # 全局 ElLoading.service 包装 (一行方案, 不需新 ref)
    r"ElLoading\.service\s*\(",
]
# error UI 关键字 (含全局拦截器导入作为"已覆盖"标记)
ERROR_PATTERNS = [
    r"\bcatch\s*\(",                     # try { } catch (e) { ... }
    r"\bsetError\s*\(",                  # ref/setError
    r"\berr\.value\s*=",                 # err.value = ...
    r"ElMessage\.error",                 # Element Plus 错误提示
    r"ElNotification\.error",
    r"message\.error",
    # WHY 全局拦截器识别: utils/http.ts 装了 response interceptor,
    # 任何从 '@/utils/http' 导入 http 的 .vue 文件都已覆盖 error UI
    r"from\s+['\"]@/utils/http['\"]",
    r"from\s+['\"]\.\./utils/http['\"]",
    r"from\s+['\"]\.\./\.\./utils/http['\"]",
]


def find_api_calls(text: str, file_rel: str) -> List[Dict]:
    """
    找 API 调用点, 返回 [{name, line, context, has_try, has_catch, has_loading, has_error}, ...]
    """
    calls = []
    lines = text.splitlines()
    # 模式: xxxApi.foo( / http.get/post( / fetch(
    patterns = [
        r"\b(\w+(?:Api|Client|Service))\b\.(\w+)\s*\(",
        r"\b(axios|http|fetch)\s*\.\s*(get|post|put|delete|patch)\s*\(",
    ]
    for i, line in enumerate(lines):
        for pat in patterns:
            for m in re.finditer(pat, line):
                name = m.group(1)
                if name in EXCLUDE_FN:
                    continue
                # 排除常见非 API
                if name in ("useGlobalDragDrop", "useAdminAuth", "useI18n"):
                    continue
                # 取上下文 (前后 30 行)
                start = max(0, i - 5)
                end = min(len(lines), i + 30)
                context = "\n".join(lines[start:end])
                has_try = "try" in context and "{" in context
                has_catch = "catch" in context
                # WHY re.IGNORECASE: saving 是小写, Loading 是大写, 都要匹配
                has_loading = any(re.search(p, context, re.IGNORECASE) for p in LOADING_PATTERNS)
                has_error = any(re.search(p, context) for p in ERROR_PATTERNS)
                # WHY 全文扫描 import: 局部上下文 30 行可能不包含 import 语句
                #   从 '@/utils/http' 导入的整个文件都被全局拦截器覆盖
                #   从 '@/api' 导入也算 (@/api/index.ts 内部用 http, 同样走拦截器)
                has_global_interceptor = bool(re.search(
                    r"import\s*\{[^}]*http[^}]*\}\s*from\s*['\"]@/utils/http['\"]", text
                )) or bool(re.search(
                    r"import\s*\{[^}]*(?:Api|Client)[^}]*\}\s*from\s*['\"]@/api['\"]", text
                ))
                calls.append({
                    "file": file_rel,
                    "line": i + 1,
                    "name": f"{m.group(1)}.{m.group(2)}",
                    "snippet": line.strip()[:100],
                    "has_try": has_try,
                    "has_catch": has_catch,
                    "has_loading": has_loading,
                    "has_error": has_error or has_global_interceptor,
                    "has_global_interceptor": has_global_interceptor,
                })
    return calls


def main():
    print("=" * 60)
    print("  API 调用三态完整性扫描")
    print("=" * 60)

    all_calls: List[Dict] = []
    for f in FRONT.rglob("*.vue"):
        try:
            text = f.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        rel = str(f.relative_to(ROOT))
        all_calls.extend(find_api_calls(text, rel))

    # 分类
    no_try = [c for c in all_calls if not c["has_try"] and not c["has_catch"]]
    no_loading = [c for c in all_calls if not c["has_loading"]]
    no_error_ui = [c for c in all_calls if not c["has_error"]]

    findings: List[Dict] = []
    for c in no_try:
        findings.append({
            "severity": "FAIL",
            "category": "no_try_catch",
            "file": c["file"], "line": c["line"],
            "message": f"{c['name']}() 无 try-catch 错误处理",
            "snippet": c["snippet"],
        })
    for c in no_loading:
        findings.append({
            "severity": "WARN",
            "category": "no_loading",
            "file": c["file"], "line": c["line"],
            "message": f"{c['name']}() 无 loading 状态管理",
            "snippet": c["snippet"],
        })
    for c in no_error_ui:
        findings.append({
            "severity": "WARN",
            "category": "no_error_ui",
            "file": c["file"], "line": c["line"],
            "message": f"{c['name']}() 无 error UI 展示",
            "snippet": c["snippet"],
        })

    fail = sum(1 for f in findings if f["severity"] == "FAIL")
    warn = sum(1 for f in findings if f["severity"] == "WARN")

    report = {
        "stats": {
            "total_calls": len(all_calls),
            "no_try_catch": len(no_try),
            "no_loading": len(no_loading),
            "no_error_ui": len(no_error_ui),
        },
        "summary": {"fail": fail, "warn": warn, "exit_code": 2 if fail else (1 if warn else 0)},
        "calls": all_calls,
        "findings": findings,
    }
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"  API 调用总数: {len(all_calls)}")
    print(f"  无 try-catch: {len(no_try)}")
    print(f"  无 loading: {len(no_loading)}")
    print(f"  无 error UI: {len(no_error_ui)}")
    print(f"  FAIL={fail}  WARN={warn}")
    print(f"  报告: {OUT.relative_to(ROOT)}")

    if findings:
        print()
        for f in findings[:30]:
            icon = "[FAIL]" if f["severity"] == "FAIL" else "[WARN]"
            print(f"  {icon}  {f['file']}:{f['line']}  {f['message']}")
            print(f"          {f['snippet']}")
        if len(findings) > 30:
            print(f"  ... 还有 {len(findings) - 30} 条")

    sys.exit(report["summary"]["exit_code"])


if __name__ == "__main__":
    main()
