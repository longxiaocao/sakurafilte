"""检查 zh-CN.ts / en-US.ts 大括号配对"""
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\frontend\src\i18n\locales")

def check(fp: Path):
    content = fp.read_text(encoding="utf-8")
    lines = content.splitlines()
    brace_count = 0
    in_str = False
    quote = None
    last_open_line = None
    for line_num, line in enumerate(lines, start=1):
        i = 0
        while i < len(line):
            c = line[i]
            if in_str:
                if c == '\\':
                    i += 2
                    continue
                if c == quote:
                    in_str = False
                i += 1
                continue
            if c in ("'", '"', '`'):
                in_str = True
                quote = c
            elif c == '{':
                brace_count += 1
                last_open_line = line_num
            elif c == '}':
                brace_count -= 1
                if brace_count < 0:
                    print(f"  {fp.name} 多了 }} L{line_num}: {line[:80]}")
                    brace_count = 0
            i += 1
    print(f"  {fp.name}: 最终 brace_count = {brace_count}")

for fp in [ROOT / "zh-CN.ts", ROOT / "en-US.ts"]:
    check(fp)
