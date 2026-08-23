#!/usr/bin/env python3
"""
扫描所有替换后 .vue 文件，找出 TS 语法错误
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"

# 错误模式
PATTERNS = [
    # 't('xxx')yyy' - 字符串嵌套
    (r"'t\('admin\.", "字符串嵌套 t()"),
    # 在 template 中直接写 t('...') 没 {{ }}
    (r">\s*t\('admin\.", "template 中 t() 缺 {{ }}"),
    # 错误的字符串拼接: 几段 't(...)t(...)'
    (r"'t\('admin\.[^']*'\)[a-zA-Z_]", "字符串拼接错误"),
]

errors_by_file = {}
for vp in ADMIN.rglob("*.vue"):
    text = vp.read_text(encoding="utf-8")
    rel = str(vp.relative_to(ROOT))
    file_errors = []
    lines = text.splitlines()
    for i, line in enumerate(lines):
        for pat, name in PATTERNS:
            m = re.search(pat, line)
            if m:
                file_errors.append((i + 1, name, line.strip()[:200]))
    if file_errors:
        errors_by_file[rel] = file_errors

print("=" * 60)
print(f"语法错误文件: {len(errors_by_file)}")
print("=" * 60)
for f, errs in errors_by_file.items():
    print(f"\n  {f} ({len(errs)} 处):")
    for ln, name, content in errs[:8]:
        print(f"    L{ln} [{name}]: {content[:160]}")
    if len(errs) > 8:
        print(f"    ... 还有 {len(errs) - 8} 处")
