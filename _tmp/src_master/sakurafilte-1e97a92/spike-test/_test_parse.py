import re
from pathlib import Path
fp = Path(r"d:\projects\sakurafilter\frontend\src\i18n\locales\en-US.ts")
txt = fp.read_text(encoding="utf-8")
m = re.search(r"feedback\s*:\s*\{([^}]*)\}", txt, re.DOTALL)
if m:
    pat = re.compile(r"^\s*(\w+)\s*:\s*'(.*?)',?\s*$", re.MULTILINE)
    cnt = 0
    for km in pat.finditer(m.group(1)):
        cnt += 1
    print("total matched:", cnt)
    # 看 feedback 段前 100 字符
    print(repr(m.group(1)[:500]))
