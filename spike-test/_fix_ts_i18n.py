"""把 .ts 文件中 t() 改为 i18n.global.t() + 加 import"""
import re
from pathlib import Path
ROOT = Path(r"d:\projects\sakurafilter\frontend\src")

INSERT = "import i18n from '@/i18n'\n"

for f in ROOT.rglob("*.ts"):
    if f.name in ("i18n.ts", "index.ts") and "i18n" in f.parts:
        continue  # 跳过 i18n 自身
    txt = f.read_text(encoding="utf-8")
    if "i18n.global.t" in txt or "import i18n" in txt:
        continue
    # 检测是否含 t('common.feedback.x')
    if "t('common.feedback." not in txt:
        continue
    # 加 import
    new = re.sub(r"(import [^\n]+\n)", r"\1" + INSERT, txt, count=1)
    if new == txt:
        new = INSERT + txt
    # 替换 t('common.feedback.x') → i18n.global.t('common.feedback.x')
    new = new.replace("t('common.feedback.", "i18n.global.t('common.feedback.")
    f.write_text(new, encoding="utf-8")
    print(f"  [FIX] {f.relative_to(ROOT)}")
