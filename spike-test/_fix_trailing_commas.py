"""修复 zh-CN.ts / en-US.ts 中所有缺尾随逗号的行
规则: 行以 ',  // 注释/或裸文本 收尾表示正常, 否则补 ','
只处理引号 'value' 形式 (不含嵌套模板字符串)
"""
import re
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\frontend\src\i18n\locales")

KEY_VAL = re.compile(r"""^(\s+)(\w+)\s*:\s*('[^']*(?:\\.[^']*)*'|"[^"]*"),?\s*$""")

def fix(fp: Path) -> int:
    txt = fp.read_text(encoding="utf-8")
    lines = txt.splitlines()
    out = []
    fixed = 0
    for line in lines:
        m = KEY_VAL.match(line)
        if m:
            indent, key, val, *rest = m.groups()
            suffix = line[m.end(2):]
            # 已经是 "key: 'val'," 或 "key: 'val'" 后面是 } 或 , 的情况
            stripped = line.strip()
            if stripped.endswith(","):
                out.append(line)
                continue
            if stripped.endswith("}") or stripped.endswith("},"):
                out.append(line)
                continue
            if stripped.endswith("'") or stripped.endswith('"'):
                # 缺尾随逗号
                new_line = f"{indent}{key}: {val},"
                out.append(new_line)
                fixed += 1
                print(f"  {fp.name} L{len(out)}: {line[:70]} → +','")
                continue
        out.append(line)
    fp.write_text("\n".join(out) + "\n", encoding="utf-8")
    return fixed

total = 0
for fp in [ROOT / "zh-CN.ts", ROOT / "en-US.ts"]:
    print(f"\n=== {fp.name} ===")
    n = fix(fp)
    print(f"  共修复 {n} 行")
    total += n
print(f"\n总计: {total} 行")
