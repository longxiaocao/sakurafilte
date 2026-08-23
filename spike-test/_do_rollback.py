#!/usr/bin/env python3
"""
回滚 .vue 中引用了缺失 key 的 t() 调用, 用原文中文替换
=========================================================
精确算法: 找 t() 上下文 (如 :placeholder="t('xxx')" 或 't(...)') 在原文中的位置.
更简单: 对每个 t() 引用, 用 git HEAD 中相同位置的中文替换.
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


def find_chinese_at_position(orig: str, line: int) -> list:
    """在原文第 line 行附近找中文字符串"""
    lines = orig.split("\n")
    if line >= len(lines):
        return []
    # 找前后 3 行内的中文字符串
    chinese = []
    for i in range(max(0, line - 2), min(len(lines), line + 3)):
        for m in re.finditer(r"'([^']*[\u4e00-\u9fff][^']*)'", lines[i]):
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
            # 找所有 t() 引用, 缺失的回滚
            for m in re.finditer(r"t\('([^']+)'\)", line):
                full = m.group(1)
                if full in all_keys:
                    continue
                # 缺失, 找原文对应行的中文
                # 注意: 原文和当前文件行数一致 (因为我们没有插入/删除行)
                # 找 i 行附近的原文
                chinese = []
                for j in range(max(0, i - 2), min(len(orig_lines), i + 3)):
                    for cm in re.finditer(r"'([^']*[\u4e00-\u9fff][^']*)'", orig_lines[j]):
                        chinese.append(cm.group(1))
                # 取第一个
                if not chinese:
                    continue
                # 选最短的 (避免注释)
                # 实际上: 找与 m.group(0) 上下文最匹配的
                # 简单: 取第一个
                replacement = chinese[0]
                new_line = new_line.replace(m.group(0), f"'{replacement}'", 1)
                rolled_count += 1
            new_lines.append(new_line)

        if rolled_count > 0:
            vp.write_text("\n".join(new_lines), encoding="utf-8")
            print(f"  ✓ {rel}: 回滚 {rolled_count} 处")
            total += rolled_count
    print(f"\n总回滚: {total} 处")


if __name__ == "__main__":
    rollback()
