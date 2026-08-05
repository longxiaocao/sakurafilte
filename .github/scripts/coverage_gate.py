#!/usr/bin/env python3
"""覆盖率门禁 (防回退): 解析 cobertura XML 根 line-rate, 低于阈值则 exit 1

用法: coverage_gate.py <cobertura-glob> <min-line-rate-percent>
示例: coverage_gate.py "tests/SakuraFilter.Api.Tests/TestResults/*/coverage.cobertura.xml" 5

WHY 自写而非 reportgenerator -thresholds:
  - GitHub Actions runner 自带 python, 零依赖 (无需 nuget 安装 dotnet tool, 网络不稳时 CI 不脆)
  - cobertura 根元素 line-rate 即总体行覆盖率, 解析简单可靠

门禁语义: 防回退而非达标 — 阈值取当前基线以下 (基线 7.12%, 阈值 5%),
  防止新增大量未测代码把覆盖率拉低; 覆盖提升后阈值可逐步上调
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    if len(sys.argv) < 3:
        print("用法: coverage_gate.py <cobertura-glob> <min-line-rate-percent>")
        return 2

    files = glob.glob(sys.argv[1])
    if not files:
        print(f"::error::[coverage] 未找到覆盖率文件: {sys.argv[1]}")
        return 1

    threshold = float(sys.argv[2]) / 100.0
    # 🔧 fix(审查): 取最新文件 (glob 默认字典序, 历史低覆盖率文件可能被选中 → 误判 FAIL)
    files.sort(key=os.path.getmtime, reverse=True)
    root = ET.parse(files[0]).getroot()
    rate = float(root.get("line-rate", "0"))

    ok = rate >= threshold
    print(f"[coverage] line-rate={rate:.2%} 阈值={threshold:.2%} 文件数={len(files)} "
          f"选用={files[0]} {'PASS' if ok else 'FAIL (覆盖率低于阈值, 请补充测试或调整阈值)'}")
    if not ok:
        # GitHub Actions UI 可见 (无需 admin 日志权限)
        print(f"::error::[coverage] line-rate={rate:.2%} 低于阈值 {threshold:.2%} (文件: {files[0]})")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
