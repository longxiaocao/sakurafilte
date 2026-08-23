"""
修复 ensure_usei18n 错误注入位置 (v2):
先 re.sub 找到 const { t } 行号, 然后再 splice.

实际策略: 把 const { t } 行移到最后一个 import 行后.
正确做法: 用一次 re.sub 替换整段.
"""
import re
from pathlib import Path

FRONT = Path(r"d:\projects\sakurafilter\frontend\src\views\admin")

def fix_usei18n_position(vue_path: Path) -> bool:
    text = vue_path.read_text(encoding="utf-8")

    if "const { t } = useI18n()" not in text:
        return False

    # 找到 const { t } 行
    t_line_match = re.search(r"^const \{ t \} = useI18n\(\)\s*$", text, re.MULTILINE)
    if not t_line_match:
        return False

    # 找到所有 import 行
    import_matches = list(re.finditer(r"^import .+?$", text, re.MULTILINE))
    if not import_matches:
        return False

    # 计算 const { t } 行的位置范围
    t_start = t_line_match.start()
    t_end = t_line_match.end()
    # 包含后面的换行符
    if t_end < len(text) and text[t_end] == "\n":
        t_end += 1

    # 如果 t_start 在最后一个 import 之后, 说明已经正确, 跳过
    last_import = import_matches[-1]
    if t_start > last_import.end():
        return False

    # 删除 const { t } 行
    new_text = text[:t_start] + text[t_end:]

    # 在新的 text 中, 重新找 import 位置
    import_matches_new = list(re.finditer(r"^import .+?$", new_text, re.MULTILINE))
    if not import_matches_new:
        return False
    last_import_new = import_matches_new[-1]
    insert_pos = last_import_new.end() + 1  # +1 是换行

    new_text = new_text[:insert_pos] + "\nconst { t } = useI18n()\n" + new_text[insert_pos:]

    vue_path.write_text(new_text, encoding="utf-8")
    return True


for f in FRONT.glob("*.vue"):
    if fix_usei18n_position(f):
        print(f"  ✓ {f.name}")
    else:
        print(f"  - {f.name} (no fix needed)")
