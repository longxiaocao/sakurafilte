#!/usr/bin/env python3
"""
找出 .vue 文件中 t() 引用但 i18n 文件中缺失的 key
生成补充报告
"""
import re
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"
LOC = ROOT / "frontend" / "src" / "i18n" / "locales"

# 1. 解析 i18n 文件, 完整路径
def parse_i18n(fp: Path) -> dict:
    """解析 i18n ts 文件, 返回 {full_key: value}"""
    text = fp.read_text(encoding="utf-8")
    # 简化: 通过正则匹配 'key.path' 形式 (在左侧)
    # 但 zh-CN.ts 是嵌套结构, 需要解析层级
    # 用最简方案: 提取所有 'key' 形式作 path, 但需要知道嵌套
    # 实际: 文件结构是 helpview: { string: { l85_: '...' } }
    # 路径是 admin.helpview.string.l85_
    # 我们可以从 'admin' 顶层开始, 跟踪层级
    result = {}
    # 解析嵌套
    lines = text.split("\n")
    path_stack = []
    for line in lines:
        # 计算缩进
        indent = len(line) - len(line.lstrip())
        # 找到 key: 或 key: { 形式
        m = re.match(r"(\s*)([a-zA-Z_][a-zA-Z0-9_]*):\s*(.*)", line)
        if m:
            _indent, key, rest = m.groups()
            if rest.startswith("{"):
                # 进入新层级
                # 弹出 stack 中缩进 >= 当前缩进的
                while path_stack and path_stack[-1][1] >= indent:
                    path_stack.pop()
                path_stack.append((key, indent))
            elif rest.startswith("'"):
                # 叶子 key
                # 弹出 stack 中缩进 >= 当前缩进的
                while path_stack and path_stack[-1][1] >= indent:
                    path_stack.pop()
                full_path = ".".join(p[0] for p in path_stack) + "." + key
                # 提取 value
                vm = re.search(r"'([^']*)'", rest)
                if vm:
                    result[full_path] = vm.group(1)
    return result


zh_keys = parse_i18n(LOC / "zh-CN.ts")
en_keys = parse_i18n(LOC / "en-US.ts")
print(f"zh-CN.ts 解析出 {len(zh_keys)} 个完整 key")
print(f"en-US.ts 解析出 {len(en_keys)} 个完整 key")

# 2. 收集 .vue 中 t() 引用
vue_refs = set()
for vp in ADMIN.rglob("*.vue"):
    text = vp.read_text(encoding="utf-8")
    for m in re.finditer(r"t\('([^']+)'\)", text):
        vue_refs.add(m.group(1))
print(f".vue 中 t() 引用: {len(vue_refs)} 个")

# 3. 缺失 key
missing = sorted(vue_refs - set(zh_keys.keys()))
print(f"\n缺失 key ({len(missing)}):")
for k in missing[:50]:
    print(f"  {k}")
print()
# 保存
out = ROOT / "spike-test" / "missing_i18n_keys.json"
out.write_text(json.dumps(missing, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"保存到: {out}")
