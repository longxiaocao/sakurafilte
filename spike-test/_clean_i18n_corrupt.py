"""清理 zh-CN.ts / en-US.ts 中 feedback 段内的损坏 entry
- 检测规则:
  - 行不以 ',' 结尾, 不是 '}' (跨行值)
  - 含 ${} 但未配对
  - 含 } 在 value 中
"""
import re
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\frontend\src\i18n\locales")

def clean(fp: Path) -> int:
    txt = fp.read_text(encoding="utf-8")
    lines = txt.splitlines()
    out = []
    in_feedback = False
    brace_depth = 0
    i = 0
    removed = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        if not in_feedback:
            if re.match(r"feedback\s*:\s*\{", stripped):
                in_feedback = True
                brace_depth = 1
            out.append(line)
            i += 1
            continue
        # 在 feedback 段内
        if stripped == "}":
            in_feedback = False
            out.append(line)
            i += 1
            continue
        # 跳过空行
        if not stripped:
            out.append(line)
            i += 1
            continue
        # 检查 value 是否跨行 (有 ${ 但不在同一行闭合)
        m = re.match(r"^(\s*)(\w+)\s*:\s*'(.*)',?\s*$", line)
        if not m:
            # 可能是跨行值的一部分 - 删除
            print(f"  {fp.name} 删行 {i+1}: {line[:80]}")
            removed += 1
            i += 1
            continue
        key, val = m.group(2), m.group(3)
        # 检查 value 是否完整: 必须以 ' 结尾
        if "'" not in line and "${" in val:
            # 跨行 - 删除
            print(f"  {fp.name} 删行 {i+1} (跨行): {key} = {val[:60]}")
            removed += 1
            i += 1
            continue
        # value 中含 } 但行不以 } 开头 (混入垃圾)
        if re.search(r"\}\s*[A-Z]", val) and not val.startswith("}"):
            # 含 "} Xxx" 模式
            print(f"  {fp.name} 删行 {i+1} (脏): {key} = {val[:60]}")
            removed += 1
            i += 1
            continue
        out.append(line)
        i += 1
    fp.write_text("\n".join(out) + "\n", encoding="utf-8")
    return removed

total = 0
for fp in [ROOT / "zh-CN.ts", ROOT / "en-US.ts"]:
    print(f"\n=== 清理 {fp.name} ===")
    n = clean(fp)
    print(f"  共删除 {n} 行")
    total += n
print(f"\n总计: {total} 行")
