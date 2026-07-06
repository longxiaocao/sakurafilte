"""把 http.ts 中 t() 改为 i18n.global.t()"""
from pathlib import Path
fp = Path(r"d:\projects\sakurafilter\frontend\src\utils\http.ts")
txt = fp.read_text(encoding="utf-8")
new = txt.replace("t('common.feedback.", "i18n.global.t('common.feedback.")
fp.write_text(new, encoding="utf-8")
print("done")
