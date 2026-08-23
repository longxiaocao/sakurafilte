"""i18n 语义化分析 v7 - 完整 lXXX_ 分布 + 公共 key 提取"""
import re
from pathlib import Path
from collections import Counter, defaultdict, OrderedDict

zh = Path('frontend/src/i18n/locales/zh-CN.ts').read_text(encoding='utf-8')

# 1. 解析 i18n 为嵌套结构
def parse_i18n_to_dict(text):
    """把 ts 文本解析为嵌套 dict (只支持 4 层 + 字符串值)"""
    result = {}
    # 先去掉 export default 和结尾分号
    text = re.sub(r'export\s+default\s+', '', text)
    text = re.sub(r';\s*$', '', text)

    # 简单状态机解析
    lines = text.split('\n')
    stack = [(result, 0)]
    current_obj = result
    current_key = None
    current_indent = 0

    for line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith('//') or stripped.startswith('*'):
            continue
        indent = len(line) - len(line.lstrip())

        # 推断层级: 我们靠缩进 + 'key: {' 或 'key: "value"' 来判断
        # 由于手写解析容易出错, 改用正则扫描每行
        pass

    return result

# 简化: 用正则直接提取完整 key 路径
# 形如:  admin: { ... compareview: { ... error: { l273_: '加载产品失败' } } }
# 多行模式下扫描每个 k: v 对, 维护栈
lines = zh.split('\n')
stack = []  # [(indent, name, dict)]
root = {}
current = root
current_indent = -1

for i, line in enumerate(lines):
    stripped = line.strip()
    if not stripped or stripped.startswith('//') or stripped.startswith('*') or stripped.startswith('/*') or stripped.startswith('export'):
        continue
    # 排除 '}'  '};' 等
    if stripped in ('}', '},', '};', '},;', '})'):
        if stack:
            stack.pop()
            if stack:
                current = stack[-1][2]
        continue
    if stripped.startswith('//'):
        continue

    indent = len(line) - len(line.lstrip())

    # 尝试匹配 'key: {' (对象开始)
    m_obj = re.match(r"^(\w+):\s*\{", stripped)
    if m_obj:
        key = m_obj.group(1)
        new_obj = {}
        # 弹栈到 indent < 当前
        while stack and stack[-1][0] >= indent:
            stack.pop()
        if stack:
            current = stack[-1][2]
        current[key] = new_obj
        stack.append((indent, key, new_obj))
        current = new_obj
        current_indent = indent
        continue

    # 尝试匹配 'key: value,'
    m_val = re.match(r"^(\w+):\s*['\"](.+?)['\"][,;]?$", stripped)
    if m_val:
        key, val = m_val.group(1), m_val.group(2)
        # 弹栈
        while stack and stack[-1][0] >= indent:
            stack.pop()
        if stack:
            current = stack[-1][2]
        current[key] = val
        continue

# 现在 root 应该是完整的嵌套 dict
# 打印结构概览
def count_keys(d, prefix=''):
    cnt = 0
    lxxx = 0
    for k, v in d.items():
        full = f'{prefix}.{k}' if prefix else k
        if isinstance(v, dict):
            sub_cnt, sub_lxxx = count_keys(v, full)
            cnt += sub_cnt
            lxxx += sub_lxxx
        else:
            cnt += 1
            if re.match(r'^l\d+_', k):
                lxxx += 1
    return cnt, lxxx

total, total_lxxx = count_keys(root)
print(f'总 key: {total}, 其中 lXXX_: {total_lxxx}')

# 2. 提取所有 lXXX_ key + value, 同时记录完整路径
all_lxxx = []
def walk(d, prefix=''):
    for k, v in d.items():
        full = f'{prefix}.{k}' if prefix else k
        if isinstance(v, dict):
            walk(v, full)
        else:
            if re.match(r'^l\d+_', k):
                all_lxxx.append((full, k, v))

walk(root)
print(f'扁平化 lXXX_: {len(all_lxxx)}')

# 3. 统计 value 频率
value_counter = Counter(item[2] for item in all_lxxx)
print('\n高频 value (出现 >= 3 次):')
for v, cnt in value_counter.most_common(40):
    if cnt >= 3:
        keys = [item[0] for item in all_lxxx if item[2] == v]
        print(f'  [{cnt}x] {v!r}: {len(keys)} keys (e.g. {keys[0]})')
