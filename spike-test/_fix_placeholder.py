"""修复 placeholder=:placeholder= 重复"""
from pathlib import Path
ROOT = Path(r"D:\projects\sakurafilter\frontend\src")
fixed = 0
for f in ROOT.rglob("*.vue"):
    txt = f.read_text(encoding="utf-8")
    new = txt.replace("placeholder=:placeholder=", ":placeholder=")
    if new != txt:
        f.write_text(new, encoding="utf-8")
        fixed += 1
print(f"fixed {fixed} files")
