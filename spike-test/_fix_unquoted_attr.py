#!/usr/bin/env python3
"""
批量修复模板中 unquoted attribute 错误
====================================
修复模式: `attr=t('xxx')` → `:attr="t('xxx')"`
Vue 模板里 attr 后面直接 =t() 没有引号, 编译报错.
正确写法是 `:attr="t('xxx')"`.
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"

# 匹配 <el-xxx ... attr=t('...') ...
# attr 是单词, 后面紧跟 =t( 但没有引号
PAT = re.compile(r'\b([a-zA-Z][a-zA-Z0-9-]*)=' + r"t\('([^']+)'\)")

# 排除已经在引号里 (前面有 ") 的: 即已经是 :attr="..." 或 attr="..."
# 用 negative lookbehind 排除 ="( 和 ='( 的引号情况
PAT_FULL = re.compile(r'(?<![\"\'])\b([a-zA-Z][a-zA-Z0-9-]*)=t\(\'([^\']+)\'\)')

total_fixed = 0
for vp in sorted(ADMIN.rglob("*.vue")):
    text = vp.read_text(encoding="utf-8")
    new_text, n = PAT_FULL.subn(r':\1="t(\'\2\')"', text)
    if n > 0:
        vp.write_text(new_text, encoding="utf-8")
        rel = vp.relative_to(ROOT)
        print(f"  ✓ {rel}: 修复 {n} 处")
        total_fixed += n

print(f"\n总计修复: {total_fixed} 处")
