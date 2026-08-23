#!/usr/bin/env python3
"""
回滚 .vue 中引用了 i18n 缺失 key 的 t() 调用
===========================================
策略: 从 git HEAD 拿到原文, 通过对比原文和当前文件,
      找出每个 t() 引用对应的原文, 替换回中文.
"""
import re
import sys
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


def get_git_original(rel: str) -> str:
    try:
        r = subprocess.run(
            ["git", "show", f"HEAD:{rel}"],
            cwd=ROOT, capture_output=True, text=True, encoding="utf-8"
        )
        return r.stdout if r.returncode == 0 else ""
    except Exception:
        return ""


def rollback_missing_keys():
    zh_keys = parse_i18n(LOC / "zh-CN.ts")
    all_keys = zh_keys

    total_rolled = 0
    for vp in sorted(ADMIN.rglob("*.vue")):
        text = vp.read_text(encoding="utf-8")
        rel = str(vp.relative_to(ROOT).as_posix())
        original = get_git_original(rel)
        if not original:
            continue

        # 找所有 t() 引用, 缺失的回滚
        new_text = text
        rolled = 0
        for m in re.finditer(r"t\('([^']+)'\)", text):
            full = m.group(1)
            if full not in all_keys:
                # 缺失 key, 不替换 (跳过)
                pass
        # 另一种方案: 我们不去自动回滚, 而是列出缺失的 key
        # 让用户决定
    return total_rolled


def smart_rollback():
    """
    智能回滚: 找出 .vue 中引用了缺失 key 的 t() 调用, 用原文替换.
    由于 .vue 文件已被修改, 我们需要从 git HEAD 拿到原文, 然后:
    1. 在原文中找相同位置的中文
    2. 在当前文件中找 t() 引用
    3. 按出现顺序对应替换
    """
    zh_keys = parse_i18n(LOC / "zh-CN.ts")
    all_keys = zh_keys

    # 对每个文件, 收集缺失 key
    file_plan = {}  # rel -> [(t_key, original_chinese)]

    for vp in sorted(ADMIN.rglob("*.vue")):
        text = vp.read_text(encoding="utf-8")
        rel = str(vp.relative_to(ROOT).as_posix())
        original = get_git_original(rel)
        if not original:
            continue

        # 提取原文中的所有中文字符串
        orig_chinese = re.findall(r"'([^']*[\u4e00-\u9fff][^']*)'", original)
        orig_chinese += re.findall(r"\"([^\"]*[\u4e00-\u9fff][^\"]*)\"", original)

        # 提取当前文件中的所有 t() 引用, 标记缺失
        current_refs = list(re.finditer(r"t\('([^']+)'\)", text))
        missing_refs = [m for m in current_refs if m.group(1) not in all_keys]

        # 简单分配: 按出现顺序对应
        plan = []
        for i, m in enumerate(missing_refs):
            if i < len(orig_chinese):
                plan.append((m.group(0), orig_chinese[i]))
            else:
                plan.append((m.group(0), None))
        file_plan[rel] = plan

    # 输出 plan
    total = 0
    can_rollback = 0
    for rel, plan in file_plan.items():
        if plan:
            total += len(plan)
            can_rollback += sum(1 for _, v in plan if v is not None)
            print(f"\n{rel}: {len(plan)} 处缺失, 可回滚 {sum(1 for _, v in plan if v is not None)}")
            for t_call, orig in plan[:5]:
                if orig:
                    print(f"  {t_call} ← {orig[:30]!r}")

    print(f"\n总缺失: {total}, 可自动回滚: {can_rollback}")

    # 保存 plan
    out = ROOT / "spike-test" / "rollback_plan.json"
    serializable = {rel: [{"t": t, "orig": o} for t, o in plan] for rel, plan in file_plan.items()}
    out.write_text(json.dumps(serializable, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Plan 保存到: {out}")


if __name__ == "__main__":
    smart_rollback()
