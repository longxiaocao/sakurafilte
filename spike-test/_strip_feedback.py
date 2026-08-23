"""还原 i18n 文件: 删除 feedback 段所有内容, 留空对象"""
import re
from pathlib import Path

for fname in ["zh-CN.ts", "en-US.ts"]:
    fp = Path(rf"d:\projects\sakurafilter\frontend\src\i18n\locales\{fname}")
    txt = fp.read_text(encoding="utf-8")
    # 匹配整个 feedback: { ... } 段 (跨行, 非贪婪)
    pat = re.compile(r"feedback\s*:\s*\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", re.DOTALL)
    new_txt, n = pat.subn("feedback: {}", txt, count=1)
    if n == 0:
        print(f"{fname}: 没找到 feedback 段, 跳过")
        continue
    fp.write_text(new_txt, encoding="utf-8")
    print(f"{fname}: 替换 {n} 处, 文件大小 {len(txt)} -> {len(new_txt)}")
