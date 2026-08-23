"""i18n 语义化 v4 - 处理剩余 16 个未匹配的 lXXX_"""
import re
import json
from pathlib import Path
from collections import Counter

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'

# 解析 zh-CN.ts
def parse_i18n(path: Path) -> dict:
    text = path.read_text(encoding='utf-8')
    text = re.sub(r'export\s+default\s+', '', text)
    text = re.sub(r';\s*$', '', text)
    lines = text.split('\n')
    root = {}
    stack = []
    current = root
    for line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith('//') or stripped.startswith('*') or stripped.startswith('/*') or stripped.startswith('export'):
            continue
        if stripped in ('}', '},', '};', '},;', '})'):
            if stack:
                stack.pop()
                if stack:
                    current = stack[-1][2]
            continue
        indent = len(line) - len(line.lstrip())
        m_obj = re.match(r"^(\w+):\s*\{", stripped)
        if m_obj:
            key = m_obj.group(1)
            new_obj = {}
            while stack and stack[-1][0] >= indent:
                stack.pop()
            if stack:
                current = stack[-1][2]
            current[key] = new_obj
            stack.append((indent, key, new_obj))
            current = new_obj
            continue
        m_val = re.match(r"^(\w+):\s*['\"](.+?)['\"][,;]?$", stripped)
        if m_val:
            key, val = m_val.group(1), m_val.group(2)
            while stack and stack[-1][0] >= indent:
                stack.pop()
            if stack:
                current = stack[-1][2]
            current[key] = val
    return root

zh_dict = parse_i18n(ZH)
# 找所有 lXXX_ key
lxxx = []
def walk(d, prefix=''):
    for k, v in d.items():
        full = f'{prefix}.{k}' if prefix else k
        if isinstance(v, dict):
            walk(v, full)
        else:
            if re.match(r'^l\d+_', k):
                lxxx.append((full, k, v))

walk(zh_dict)
print(f'当前 zh-CN.ts 中剩余 lXXX_ key: {len(lxxx)}')

# 统计 value 频率
vcount = Counter(v for _, _, v in lxxx)
print('\\n高频 value (>= 2 次):')
for v, cnt in vcount.most_common(20):
    if cnt >= 2:
        keys = [full for full, _, val in lxxx if val == v]
        print(f'  [{cnt}x] {v!r}: {len(keys)} keys')
        for k in keys[:3]:
            print(f'      {k}')
