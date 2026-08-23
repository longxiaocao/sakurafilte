"""强制删除 feedback 段"""
from pathlib import Path
import re

for fname in ["zh-CN.ts", "en-US.ts"]:
    fp = Path(rf"d:\projects\sakurafilter\frontend\src\i18n\locales\{fname}")
    txt = fp.read_text(encoding="utf-8")
    # 找 feedback: { 开始的行
    lines = txt.split("\n")
    start = None
    depth = 0
    for i, ln in enumerate(lines):
        if start is None:
            if "feedback:" in ln and "{" in ln:
                start = i
                depth = ln.count("{") - ln.count("}")
        else:
            depth += ln.count("{") - ln.count("}")
            if depth <= 0:
                # 把 start..i 替换为 "    feedback: {},"
                new_lines = lines[:start] + ["    feedback: {},"] + lines[i+1:]
                fp.write_text("\n".join(new_lines), encoding="utf-8")
                print(f"{fname}: 删 {i-start+1} 行, 替换为 feedback: {{}},")
                break
    else:
        print(f"{fname}: 没找到 feedback 段")
