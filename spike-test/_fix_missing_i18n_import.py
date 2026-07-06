"""为缺 useI18n 的 .vue 文件补 import + const { t } = useI18n()"""
import re
from pathlib import Path

ROOT = Path(r"D:\projects\sakurafilter\frontend\src")

# 已知缺 useI18n 的文件
TARGETS = [
    "views/public/PublicProductView.vue",
    "views/public/PublicCompareView.vue",
]

# 在 <script setup lang="ts"> 后插入
INSERT = """<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
"""

fixed = 0
for rel in TARGETS:
    fp = ROOT / rel
    if not fp.exists():
        print(f"  [SKIP] {rel} 不存在")
        continue
    txt = fp.read_text(encoding="utf-8")
    if "useI18n" in txt:
        print(f"  [SKIP] {rel} 已有 useI18n")
        continue
    new_txt = txt.replace("<script setup lang=\"ts\">\n", INSERT, 1)
    if new_txt != txt:
        fp.write_text(new_txt, encoding="utf-8")
        fixed += 1
        print(f"  [FIX] {rel}")

# 通用扫描: 检查所有 .vue 文件是否使用了 t() 但没 import useI18n
extra_fixed = 0
for f in ROOT.rglob("*.vue"):
    txt = f.read_text(encoding="utf-8")
    if "useI18n" in txt:
        continue
    if re.search(r"\bt\s*\(\s*['\"`]", txt):
        # 有 t() 调用, 但没 useI18n
        new_txt = txt.replace("<script setup lang=\"ts\">\n", INSERT, 1)
        if new_txt != txt:
            f.write_text(new_txt, encoding="utf-8")
            extra_fixed += 1
            print(f"  [AUTO-FIX] {f.relative_to(ROOT)}: 含 t() 但无 useI18n")
print(f"\n目标修复: {fixed}, 额外自动修复: {extra_fixed}")
