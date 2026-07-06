"""查找 i18n 文件中所有损坏 entry (value 不是完整字符串的行)"""
import re
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\frontend\src\i18n\locales")

# 匹配 "key: 'value'" (value 必须以 ' 收尾)
# 允许嵌套 ${...} 但整体必须以 ' 收尾
KEY_VAL = re.compile(r"^\s*(\w+)\s*:\s*'(.*)',?\s*$")

def find(fp: Path):
    txt = fp.read_text(encoding="utf-8")
    bad = []
    for i, line in enumerate(txt.splitlines(), start=1):
        if not line.strip() or line.strip().startswith("//"):
            continue
        if line.strip() in ("{", "},", "}"):
            continue
        if "'" not in line:
            continue
        # 完整 key: 'value' 模式
        if re.match(r"^\s*\w+\s*:\s*'[^']*'\s*,?\s*$", line):
            continue
        if re.match(r"^\s*\w+\s*:\s*'", line) and not re.search(r"'\s*,?\s*$", line):
            # 字符串未关闭
            bad.append((i, line))
    return bad

for fp in [ROOT / "zh-CN.ts", ROOT / "en-US.ts"]:
    print(f"\n=== {fp.name} ===")
    bad = find(fp)
    for i, line in bad:
        print(f"  L{i}: {line[:100]}")
    print(f"  共 {len(bad)} 条")
