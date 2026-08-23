#!/usr/bin/env python3
"""
补充 i18n 文件中缺失的 key
==========================
策略: 找出 .vue 中 t() 引用但 i18n 中不存在的 key, 从 git HEAD 拿到原文,
      用原文作为 zh-CN 值, 用 glossary 翻译作为 en-US 值.
"""
import re
import sys
import json
import subprocess
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "spike-test"))

from _i18n_glossary import translate_zh_to_en  # noqa: E402

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


def get_git_original(rel_path: str) -> str:
    """从 git HEAD 拿到原文件内容"""
    try:
        result = subprocess.run(
            ["git", "show", f"HEAD:{rel_path}"],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8"
        )
        return result.stdout if result.returncode == 0 else ""
    except Exception:
        return ""


def extract_chinese_context(text: str, target_key_short: str) -> str:
    """从原文中提取该 key 对应的中文"""
    # 简单方法: 找包含中文的字符串, 返回第一个
    matches = re.findall(r"'([^']*[\u4e00-\u9fff][^']*)'", text)
    return matches[0] if matches else ""


# 主流程
zh_keys = parse_i18n(LOC / "zh-CN.ts")
all_keys = zh_keys
print(f"i18n 中已有 {len(zh_keys)} 个 key")

# 收集缺失
missing_by_file = defaultdict(set)
for vp in ADMIN.rglob("*.vue"):
    text = vp.read_text(encoding="utf-8")
    rel = str(vp.relative_to(ROOT).as_posix())
    for m in re.finditer(r"t\('([^']+)'\)", text):
        full = m.group(1)
        if full not in all_keys:
            missing_by_file[rel].add(full)

print(f"缺失: {sum(len(v) for v in missing_by_file.values())} 个, 跨 {len(missing_by_file)} 文件")

# 从 git HEAD 拿原文, 提取中文作 zh 值
supplements_zh = {}  # full_key -> zh value
supplements_en = {}  # full_key -> en value

for rel, keys in missing_by_file.items():
    original = get_git_original(rel)
    if not original:
        continue
    # 提取原文所有中文
    chinese_strings = re.findall(r"'([^']*[\u4e00-\u9fff][^']*)'", original)
    if not chinese_strings:
        continue
    # 简单映射: 按出现顺序, 第一个 key 用第一个中文, 第二个用第二个...
    for i, k in enumerate(sorted(keys)):
        if i < len(chinese_strings):
            zh_val = chinese_strings[i]
            supplements_zh[k] = zh_val
            en_val = translate_zh_to_en(zh_val) or f"[EN] {zh_val[:30]}"
            supplements_en[k] = en_val

print(f"\n准备补充 {len(supplements_zh)} 个 key")
print("前 10 个样例:")
for i, (k, v) in enumerate(supplements_zh.items()):
    if i >= 10: break
    print(f"  {k}: {v[:30]!r} → {supplements_en.get(k, '')[:30]!r}")

# 保存到文件, 由人工审核后注入
out = ROOT / "spike-test" / "i18n_supplement.json"
out.write_text(json.dumps(
    {"zh": supplements_zh, "en": supplements_en},
    ensure_ascii=False, indent=2
), encoding="utf-8")
print(f"\n保存: {out}")
