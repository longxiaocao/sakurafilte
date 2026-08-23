#!/usr/bin/env python3
"""
修复 _fix_class_t.py 错误添加的转义引号
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"

# 模式: }}{{ t(\'admin....\') }}
# 修复: }}{{ t('admin....') }}
PAT = re.compile(r"t\(\\'([^']+)\\'\)")

total = 0
for vp in sorted(ADMIN.rglob("*.vue")):
    text = vp.read_text(encoding="utf-8")
    new_text, n = PAT.subn(r"t('\1')", text)
    if n > 0:
        vp.write_text(new_text, encoding="utf-8")
        rel = vp.relative_to(ROOT)
        print(f"  ✓ {rel}: {n} 处")
        total += n
print(f"\n总计: {total}")
