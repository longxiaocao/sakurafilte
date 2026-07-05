#!/usr/bin/env python3
"""
把 .vue 中引用了 i18n 缺失 key 的 t() 引用回滚为原文
=====================================================
策略: 提取原 .vue 文件中"该 t() 引用位置"的原文, 用作替换.
    实际做法: 从 git HEAD 读取原文, 然后找到对应位置.
    简化: 由于 .vue 文件已修改, 我们用 git show HEAD 拿到原文,
         在原文中找到对应的中文, 替换 t() 为中文.
"""
import re
import subprocess
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"
LOC = ROOT / "frontend" / "src" / "i18n" / "locales"


def parse_i18n(fp: Path) -> set:
    text = fp.read_text(encoding="utf-8")
    keys = set()
    lines = text.split("\n")
    path_stack = []
    for line in lines:
        indent = len(line) - len(line.lstrip())
        m = re.match(r"\s*([a-zA-Z_][a-zA-Z0-9_]*):\s*", line)
        if m:
            key = m.group(1)
            rest = line[m.end():]
            if rest.startswith("{"):
                while path_stack and path_stack[-1][1] >= indent:
                    path_stack.pop()
                path_stack.append((key, indent))
            elif rest.startswith("'"):
                while path_stack and path_stack[-1][1] >= indent:
                    path_stack.pop()
                full = ".".join(p[0] for p in path_stack) + "." + key
                keys.add(full)
    return keys


zh_keys = parse_i18n(LOC / "zh-CN.ts")
en_keys = parse_i18n(LOC / "en-US.ts")
all_keys = zh_keys | en_keys
print(f"i18n 文件中完整 key 数: {len(all_keys)}")

# 找 .vue 中引用但 i18n 中缺失的 key
missing_by_file = defaultdict(list)
for vp in ADMIN.rglob("*.vue"):
    text = vp.read_text(encoding="utf-8")
    rel = str(vp.relative_to(ROOT))
    for m in re.finditer(r"t\('([^']+)'\)", text):
        full = m.group(1)
        if full not in all_keys:
            missing_by_file[rel].append(full)

print(f"\n缺失 key 分布:")
total = 0
for f, ks in sorted(missing_by_file.items()):
    print(f"  {f}: {len(ks)}")
    total += len(ks)
print(f"总缺失: {total}")

# 保存
out = ROOT / "spike-test" / "missing_keys_detail.json"
out.write_text(json.dumps(
    {f: list(set(ks)) for f, ks in missing_by_file.items()},
    ensure_ascii=False, indent=2
), encoding="utf-8")
