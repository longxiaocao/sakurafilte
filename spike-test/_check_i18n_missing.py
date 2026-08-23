#!/usr/bin/env python3
"""
检查 .vue 文件中用到的 t() key 是否在 i18n 文件中都有定义
"""
import re
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"
LOC = ROOT / "frontend" / "src" / "i18n" / "locales"

# 1. 收集 i18n 文件中所有 key
i18n_keys = set()
for lang in ["zh-CN", "en-US"]:
    fp = LOC / f"{lang}.ts"
    text = fp.read_text(encoding="utf-8")
    # 提取所有 'xxx': 'yyy' 形式 (但 key 在左侧)
    for m in re.finditer(r"'([a-zA-Z][a-zA-Z0-9_]*)':", text):
        i18n_keys.add(m.group(1))

# 2. 收集 .vue 文件中所有 t('...') 引用的 key
used_keys = set()
used_full = set()  # 完整路径 admin.xxx.yyy.zzz
for vp in ADMIN.rglob("*.vue"):
    text = vp.read_text(encoding="utf-8")
    # 匹配 t('admin.xxx.yyy.zzz')
    for m in re.finditer(r"t\('([^']+)'\)", text):
        used_full.add(m.group(1))
        # 取最后一段
        parts = m.group(1).split(".")
        if parts:
            used_keys.add(parts[-1])

# 3. 找缺失的 key (完整路径)
missing = set()
for full in used_full:
    parts = full.split(".")
    if not parts[-1] in i18n_keys:
        missing.add(full)

print(f"i18n 文件中共有 {len(i18n_keys)} 个 key")
print(f".vue 文件中共引用 {len(used_full)} 个完整路径")
print(f"缺失的完整路径: {len(missing)}")

if missing:
    print("\n缺失 key:")
    for k in sorted(missing)[:30]:
        print(f"  {k}")
