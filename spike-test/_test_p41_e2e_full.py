# -*- coding: utf-8 -*-
"""Day 10+ P4.1 E2E 全量编排器

P4.1: 每个字典配 1 个 E2E, P3.x 配 Playwright 截图测试, CI 并行跑 < 10 分钟

覆盖矩阵 (按 spec day10-plus-roadmap §P4.1):
  P0.1 - _test_escape_underscore.py
  P1.3 - _test_day10_oem_brands.py (Day 10 OEM Brand 字典)
  P2.2 - _test_p22_seven_dicts.py (7 个新字典)
  P2.3 - _test_type_ordering.py (Type 排序 + 机器分类)
  P3.2 - _test_batch_oem.py (Excel 多行粘贴)
  P3.3 - _test_public_product.py (前台产品页)
  P3.4 - _test_public_search.py (公开搜索 8 字段)
  P3.5 - _test_compare.py (对比 UI 后端)

退出: 任一 FAIL → exit 1
"""
import subprocess
import sys
import time
import os
from pathlib import Path

# 编排器目录
HERE = Path(__file__).parent
ADMIN_TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
BASE = "http://localhost:5148"

# 测试用例: (脚本名, [额外 args], 期望 P0/P1/P2/P3 标签)
TEST_SUITE = [
    ("_test_escape_underscore.py", [], "P0.1"),
    ("_test_day10_oem_brands.py", [], "P1.3"),
    ("_test_p22_seven_dicts.py", [], "P2.2"),
    ("_test_type_ordering.py", [], "P2.3"),
    ("_test_batch_oem.py", [], "P3.2"),
    ("_test_public_product.py", [], "P3.3"),
    ("_test_public_search.py", [], "P3.4"),
    ("_test_compare.py", [BASE, ADMIN_TOKEN], "P3.5"),
]


def run_one(script: str, args: list[str], label: str) -> tuple[bool, int]:
    """跑单个测试, 返 (passed, exit_code)"""
    print(f"\n{'=' * 70}")
    print(f"[{label}] {script} {' '.join(args)}")
    print(f"{'=' * 70}")
    cmd = [sys.executable, str(HERE / script)] + args
    t0 = time.time()
    try:
        # 实时透出输出, 失败时 ::error:: 注解
        result = subprocess.run(
            cmd, cwd=HERE, timeout=300,
            stdout=sys.stdout, stderr=subprocess.STDOUT,
        )
        elapsed = time.time() - t0
        passed = result.returncode == 0
        status = "PASS" if passed else f"FAIL (exit {result.returncode})"
        print(f"\n[{label}] {script}: {status} ({elapsed:.1f}s)")
        if not passed:
            print(f"::error::P4.1 FAIL [{label}] {script}: exit {result.returncode}")
        return passed, result.returncode
    except subprocess.TimeoutExpired:
        elapsed = time.time() - t0
        print(f"\n[{label}] {script}: TIMEOUT (>{elapsed:.0f}s)")
        print(f"::error::P4.1 TIMEOUT [{label}] {script}")
        return False, -1
    except Exception as e:
        print(f"\n[{label}] {script}: ERROR {e}")
        print(f"::error::P4.1 ERROR [{label}] {script}: {e}")
        return False, -1


def main():
    print(f"P4.1 E2E 全量编排: {len(TEST_SUITE)} 个测试, 基地址 {BASE}")
    print(f"{'=' * 70}")
    overall_t0 = time.time()
    passed_count = 0
    failed = []

    for script, args, label in TEST_SUITE:
        if not (HERE / script).exists():
            print(f"[SKIP] {script} 不存在")
            continue
        passed, exit_code = run_one(script, args, label)
        if passed:
            passed_count += 1
        else:
            failed.append((script, exit_code, label))

    overall_elapsed = time.time() - overall_t0
    print(f"\n{'=' * 70}")
    print(f"P4.1 E2E 编排汇总: {passed_count}/{len(TEST_SUITE)} PASS, {overall_elapsed:.1f}s")
    print(f"{'=' * 70}")
    for s, e, l in failed:
        print(f"  ✗ [{l}] {s}: exit {e}")
    sys.exit(0 if not failed else 1)


if __name__ == "__main__":
    main()
