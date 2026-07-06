"""检查 i18n 文件大括号配对"""
from pathlib import Path
for fname in ["zh-CN.ts", "en-US.ts"]:
    fp = Path(rf"d:\projects\sakurafilter\frontend\src\i18n\locales\{fname}")
    txt = fp.read_text(encoding="utf-8")
    o = txt.count("{")
    c = txt.count("}")
    print(f"{fname}: {{ = {o}, }} = {c}, diff = {o - c}, lines = {len(txt.splitlines())}")
