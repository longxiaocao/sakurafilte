"""
一键三件套前端全量审计
========================
WHY: 用户每次手动跑 3 个脚本容易遗漏, 用 1 个入口串起来:
  1. 静态扫描: 5s 内, import / i18n / API 完整性
  2. 运行时扫描: ~1min, 21 路由 console 监听
  3. E2E console 审计: ~5min, 完整登录态下 151 用例 + console 断言

退出码:
  0 = 全部干净
  1 = 有 WARN (非阻断)
  2 = 有 FAIL (阻断)

用法:
  python _full_audit.py              # 跑三件套
  python _full_audit.py --skip-e2e   # 跳过 E2E (快, 1min 内完成)
  python _full_audit.py --skip-runtime  # 只跑静态 + E2E
"""
import argparse
import json
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPIKE = ROOT / "spike-test"
STATIC_SCRIPT = SPIKE / "_frontend_static_audit.py"
RUNTIME_SCRIPT = SPIKE / "_frontend_runtime_audit.py"
E2E_SCRIPT = SPIKE / "_test_e2e_destructive.py"

STATIC_REPORT = SPIKE / "frontend_static_audit.json"
RUNTIME_REPORT = SPIKE / "frontend_runtime_audit.json"
E2E_REPORT = SPIKE / "e2e_test_report.json"
OUT = SPIKE / "full_audit_summary.json"


def run_step(name: str, cmd: list, report: Path = None) -> dict:
    """运行一步, 返回 {name, ok, exit_code, duration_sec, error}"""
    print()
    print("=" * 70)
    print(f"  [{name}]")
    print("=" * 70)
    t0 = time.time()
    try:
        r = subprocess.run(cmd, cwd=str(SPIKE), capture_output=True, text=True, timeout=900, encoding="utf-8", errors="replace")
        duration = time.time() - t0
        # 提取末尾 15 行作为概要
        lines = (r.stdout + r.stderr).splitlines()
        summary = "\n".join(lines[-15:]) if lines else ""
        print(summary)
        return {
            "name": name,
            "ok": r.returncode == 0,
            "exit_code": r.returncode,
            "duration_sec": round(duration, 1),
            "report": str(report.relative_to(ROOT)) if report and report.exists() else None,
            "tail": summary,
        }
    except subprocess.TimeoutExpired:
        return {"name": name, "ok": False, "exit_code": -1, "duration_sec": time.time() - t0, "error": "timeout"}
    except FileNotFoundError as e:
        return {"name": name, "ok": False, "exit_code": -1, "duration_sec": 0, "error": str(e)}


def main():
    parser = argparse.ArgumentParser(description="一键三件套前端审计")
    parser.add_argument("--skip-e2e", action="store_true", help="跳过 E2E (快模式)")
    parser.add_argument("--skip-runtime", action="store_true", help="跳过运行时扫描")
    parser.add_argument("--skip-static", action="store_true", help="跳过静态扫描")
    args = parser.parse_args()

    print("=" * 70)
    print("  SakuraFilter 一键三件套前端审计")
    print(f"  启动时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 70)

    steps = []

    if not args.skip_static:
        steps.append(run_step(
            "1. 静态扫描 (import + i18n + API 调用点)",
            ["python", str(STATIC_SCRIPT.name)],
            STATIC_REPORT,
        ))

    if not args.skip_runtime:
        steps.append(run_step(
            "2. 运行时扫描 (21 路由 console 监听)",
            ["python", str(RUNTIME_SCRIPT.name)],
            RUNTIME_REPORT,
        ))

    if not args.skip_e2e:
        steps.append(run_step(
            "3. E2E console 审计 (151 用例 + console 断言)",
            ["python", str(E2E_SCRIPT.name)],
            E2E_REPORT,
        ))

    # === 汇总 ===
    fail = sum(1 for s in steps if not s["ok"])
    print()
    print("=" * 70)
    print("  三件套汇总")
    print("=" * 70)
    for s in steps:
        icon = "✓" if s["ok"] else "✗"
        print(f"  [{icon}] {s['name']}  exit={s['exit_code']}  {s['duration_sec']}s")
    print()
    if fail == 0:
        print("  ✅ 全部干净, 退出码 0")
    else:
        print(f"  ❌ {fail} 个失败, 退出码 2")

    # 解析每个报告的核心数据
    detailed = []
    for s in steps:
        rep = s.get("report")
        if not rep:
            continue
        try:
            data = json.loads(Path(rep).read_text(encoding="utf-8"))
            if "summary" in data and isinstance(data["summary"], dict):
                # 静态 / 运行时
                detailed.append({"step": s["name"], "summary": data["summary"]})
            elif "total" in data:
                # E2E
                detailed.append({"step": s["name"], "summary": {
                    "PASS": data.get("summary", {}).get("PASS", 0),
                    "FAIL": data.get("summary", {}).get("FAIL", 0),
                    "WARN": data.get("summary", {}).get("WARN", 0),
                    "total": data.get("total", 0),
                }})
        except (json.JSONDecodeError, OSError):
            pass

    summary = {
        "ts": datetime.now().isoformat(),
        "all_pass": fail == 0,
        "step_count": len(steps),
        "fail_count": fail,
        "exit_code": 2 if fail else 0,
        "steps": steps,
        "details": detailed,
    }
    OUT.write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\n汇总: {OUT.relative_to(ROOT)}")

    sys.exit(2 if fail else 0)


if __name__ == "__main__":
    main()
