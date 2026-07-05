#!/usr/bin/env python3
"""检查 i18n 文件中 admin.helpview 已有的 key"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LOC = ROOT / "frontend" / "src" / "i18n" / "locales"

for lang in ["zh-CN", "en-US"]:
    fp = LOC / f"{lang}.ts"
    text = fp.read_text(encoding="utf-8")
    # 找 admin.helpview 块
    m = re.search(r"helpview:\s*\{", text)
    if not m:
        print(f"[{lang}] no helpview block")
        continue
    start = m.end()
    # 计数大括号深度
    depth = 1
    i = start
    while i < len(text) and depth > 0:
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
        i += 1
    block = text[start:i - 1]
    keys = re.findall(r"'([^']+)':\s*'([^']*)'", block)
    print(f"\n=== {lang} helpview block: {len(keys)} keys ===")
    for k, v in keys:
        print(f"  {k}: {v[:80]}")
