#!/usr/bin/env python3
"""
扫描并批量修复 class="" 中嵌入 t() 导致的 vue 模板解析错误
==========================================================
模式: class="xxx t('admin...')yyy"开始"  →  拆开
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ADMIN = ROOT / "frontend" / "src" / "views" / "admin"

# 匹配 class="...t('admin...')..." 这里 t() 嵌入在 class 值里
# 修复: 把 t('...') 单独抽出来作为文本
PAT = re.compile(r'(class="[^"]*?)t\(\'([^\']+)\'\)')

def fix_class_with_t(text: str) -> str:
    """修复 class= 中嵌入的 t() 调用"""
    fixed = []
    last_end = 0
    for m in PAT.finditer(text):
        before = m.group(1)
        key = m.group(2)
        # 找到 class=" 的开引号位置
        # m.start() 是 class=" 开头, m.end() 是 t('...') 结尾
        # 把 m.group(1) 后面的 t() 替换为占位
        # 实际修复策略: 在模板中这是空 div class="dict-empty text"
        # 把 t() 提取出来, 关闭 class, 加 <span>{{ t() }}</span>
        # 简单做法: 替换为 <div v-if class="...">{{ t() }}</div>
        # 模板用法通常是 <div class="xxx">t('yyy')文字</div>
        # 修复: 改为 <div :class="...">{{ t('yyy') }} 文字</div>
        # 但这需要看上下文, 简单粗暴的方法: 把 class 后面 t() 整段移除, 后续 text 内容保留
        fixed.append(text[last_end:m.start()])
        # m.group(0) = "class=\"...t('admin.xxx')\""
        # 替换为 class="..." + 把 t() 作为文本
        # 取 class=" 之后, t() 之前的部分
        attr_end = text.find('"', m.start() + 7)  # 7 = len('class="')
        if attr_end < 0 or attr_end > m.end():
            # 简单情况, 直接把 t() 提取到模板后面
            before_attr = text[last_end:m.start()]
            attr_value = m.group(1)[7:]  # 去掉 'class="'
            fixed.append(before_attr)
            fixed.append(f'class="{attr_value}">{{{{ t(\'{key}\') }}}}')
            last_end = m.end()
        else:
            last_end = m.start()
    if not fixed:
        return text
    fixed.append(text[last_end:])
    return ''.join(fixed)


# 实际更简单的方案: 找出所有 class="...t('...')..." 模式
# 简单替换: 把 t() 后面紧跟的 " 替换为 }}">{{ t() }}
# 复杂情况, 我们直接定位常见的 dict-empty 模式

total = 0
for vp in sorted(ADMIN.rglob("*.vue")):
    text = vp.read_text(encoding="utf-8")
    new_text = text
    # 模式: class="dict-emptyXXXt('admin...')新yyy"开始
    # 简单处理: 把 "dict-emptyt('admin.xxx')新yyy" 改为 "dict-empty">{{ t('admin.xxx') }} 新yyy"
    # 用 lookahead 找 > 之后的内容
    new_text2 = re.sub(
        r'class="dict-emptyt\(\'([^\']+)\'\)',
        r'class="dict-empty">{{ t(\'\1\') }}',
        new_text
    )
    if new_text2 != new_text:
        vp.write_text(new_text2, encoding="utf-8")
        rel = vp.relative_to(ROOT)
        print(f"  ✓ {rel}")
        total += 1
print(f"\n修复文件数: {total}")
