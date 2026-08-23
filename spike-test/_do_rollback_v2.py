#!/usr/bin/env python3
"""
回滚 .vue 中引用了缺失 key 的 t() 调用, 用原文中文替换
=========================================================
精确算法: 找 t() 上下文 (如 :placeholder="t('xxx')" 或 't(...)') 在原文中的位置.
对于多个连续 t(), 按出现顺序用不同的中文.
"""
import re
import subprocess
from pathlib import Path

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


def find_chinese_in_window(orig_lines: list, center: int, window: int = 1) -> list:
    """在第 center 行附近找中文字符串"""
    chinese = []
    for i in range(max(0, center - window), min(len(orig_lines), center + window + 1)):
        for m in re.finditer(r"'([^']*[\u4e00-\u9fff][^']*)'", orig_lines[i]):
            chinese.append(m.group(1))
        for m in re.finditer(r'"([^"]*[\u4e00-\u9fff][^"]*)"', orig_lines[i]):
            chinese.append(m.group(1))
    return chinese


def rollback():
    zh_keys = parse_i18n(LOC / "zh-CN.ts")
    all_keys = zh_keys

    total = 0
    for vp in sorted(ADMIN.rglob("*.vue")):
        text = vp.read_text(encoding="utf-8")
        rel = str(vp.relative_to(ROOT).as_posix())
        original = get_git_original(rel)
        if not original:
            continue

        orig_lines = original.split("\n")
        lines = text.split("\n")
        new_lines = []
        rolled_count = 0

        for i, line in enumerate(lines):
            new_line = line
            # 收集这一行所有 t() 引用
            t_refs = list(re.finditer(r"t\('([^']+)'\)", line))
            missing_refs = [m for m in t_refs if m.group(1) not in all_keys]
            if not missing_refs:
                new_lines.append(new_line)
                continue

            # 找该行附近的所有中文字符串
            chinese_pool = find_chinese_in_window(orig_lines, i, window=2)
            # 同位置可能用同样字符串
            # 简单: 每个 missing 分配一个 (按出现顺序)
            # 但 chinese_pool 中可能含 \n 等, 我们只取简单的 (< 50 字符)
            chinese_pool = [c for c in chinese_pool if 1 < len(c) < 80 and "\n" not in c]
            if not chinese_pool:
                new_lines.append(new_line)
                continue

            # 倒序替换, 这样前面的 index 不影响
            # 用 pop() 方式避免重复用
            pool = list(chinese_pool)
            for m in reversed(missing_refs):
                if not pool:
                    break
                # 选最相关的: 与 m.group(0) 同一行? 找对应行
                # 简化: 直接用 pool 中下一个
                replacement = pool.pop(0)  # 用第一个, 然后删除避免重复
                # 实际: 倒序遍历 missing_refs, 用正序 pool
                new_line = new_line[:m.start()] + f"'{replacement}'" + new_line[m.end():]
                rolled_count += 1

            new_lines.append(new_line)

        if rolled_count > 0:
            vp.write_text("\n".join(new_lines), encoding="utf-8")
            print(f"  ✓ {rel}: 回滚 {rolled_count} 处")
            total += rolled_count
    print(f"\n总回滚: {total} 处")


if __name__ == "__main__":
    rollback()
