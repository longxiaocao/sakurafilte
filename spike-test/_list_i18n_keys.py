#!/usr/bin/env python3
"""检查 i18n 文件中的 key"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LOC = ROOT / "frontend" / "src" / "i18n" / "locales"

text = (LOC / "zh-CN.ts").read_text(encoding="utf-8")
# 匹配 'key_name': 形式
# key 允许 a-z 0-9 _ .
keys = re.findall(r"'([a-zA-Z_][a-zA-Z0-9_\.]*[a-zA-Z0-9_])':", text)
print(f"zh-CN.ts 中共有 {len(keys)} 个 key")
print("包含 admin.helpview 的 key:")
for k in keys:
    if "helpview" in k:
        print(f"  {k}")
print()
print("包含 admin.etlview.string.l354_ 的 key:")
for k in keys:
    if "etlview" in k and "l354" in k:
        print(f"  {k}")
