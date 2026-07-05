#!/usr/bin/env python3
"""对比 .vue 中 t() 引用与 i18n 文件中实际存在的 key"""
import re
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"
LOC = ROOT / "frontend" / "i18n" / "locales"

# 1. 收集 i18n 文件中所有 key (取最后一段)
i18n_keys_short = set()  # 仅最后一段 (l85_)
i18n_keys_full = set()   # 完整路径 (admin.helpview.string.l85_)

for lang in ["zh-CN", "en-US"]:
    fp = LOC / f"{lang}.ts"
    if not fp.exists():
        fp = ROOT / "frontend" / "src" / "i18n" / "locales" / f"{lang}.ts"
    if not fp.exists():
        print(f"找不到 {fp}")
        continue
    text = fp.read_text(encoding="utf-8")
    # 提取完整的 key 路径: admin.{block}.{ctx}.{key}
    # 用深度匹配: helpview: { string: { l85_: '...', ...
    # 实际上文件结构是嵌套, 路径是 admin.compareview.placeholder.l332_id
    # 用: 后跟 { 或 '...' 的 token 作 key (包括嵌套)
    # 简化: 匹配 'key' 或 key: 这种
    for m in re.finditer(r"([a-zA-Z_][a-zA-Z0-9_]+):\s*'", text):
        i18n_keys_short.add(m.group(1))

# 2. 收集 .vue 文件中 t() 引用的 key
vue_refs = defaultdict(set)  # file -> set of full keys
for vp in ADMIN.rglob("*.vue"):
    text = vp.read_text(encoding="utf-8")
    rel = str(vp.relative_to(ROOT))
    for m in re.finditer(r"t\('([^']+)'\)", text):
        full = m.group(1)
        vue_refs[rel].add(full)

# 3. 缺失 key (取最后一段判断)
missing = defaultdict(list)  # short_key -> [full keys]
for rel, refs in vue_refs.items():
    for full in refs:
        short = full.split(".")[-1]
        if short not in i18n_keys_short:
            missing[short].append((rel, full))

print(f"i18n 文件中最后一段 key 数: {len(i18n_keys_short)}")
print(f".vue 文件中 t() 引用数: {sum(len(v) for v in vue_refs.values())}")
print(f"可能缺失 key (按短名): {len(missing)}")
print()
for k, uses in sorted(missing.items())[:30]:
    print(f"  {k} ({len(uses)} 处):")
    for f, full in uses[:3]:
        print(f"    {f}: {full}")
