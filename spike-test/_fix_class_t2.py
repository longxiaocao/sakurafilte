"""批量修复 class="xxx中文" 被错误替换为 class="xxxt('...')... 的情况
模式: class="xxxxxt('admin....')yyyy" 开始 -- 不合法属性名
"""
import re
from pathlib import Path

ROOT = Path(r'D:\projects\sakurafilter\frontend\src\views\admin')

# 匹配 class="xxxxt('admin....')yyy 出现意外引号或 < 等非法字符
# 实际我们要找的是: class="abc't('admin...)XxxYyy  (class 内被插入 t() 但引号未匹配)

total = 0
for vp in sorted(ROOT.rglob("*.vue")):
    text = vp.read_text(encoding="utf-8")
    new_text = text

    # 模式 1: class="xxxxt('admin.xx.xx_')中文残留" → class="xxx" + {{ t('admin.xx.xx_') }} + 中文残留"
    # 例如: class="dict-emptyt('admin.productname1sview.string.l221_')新增产品名 1"开始
    # 修复: 提取 t() 部分, 移出 class
    pattern1 = re.compile(r'class="([^"]*?)t\(\'(admin\.[^\']+)\'\)([^"]*?)"')
    def fix1(m):
        prefix = m.group(1)
        key = m.group(2)
        suffix = m.group(3)
        return 'class="' + prefix + '" > ' + '{{ t(\'' + key + '\') }}' + suffix

    new_text, n1 = pattern1.subn(fix1, new_text)
    if n1 > 0:
        rel = vp.relative_to(ROOT.parent.parent.parent)
        print(f"  ✓ {rel}: 修复 {n1} 处")
        total += n1
        vp.write_text(new_text, encoding="utf-8")

print(f"\n总修复: {total} 处")
