"""i18n 语义化分析 v6 - 提取所有 lXXX_ key 的 value 分布"""
import re
from pathlib import Path
from collections import Counter, defaultdict

zh = Path('frontend/src/i18n/locales/zh-CN.ts').read_text(encoding='utf-8')

# 1. 提取所有 lXXX_ key -> value
# 完整 key 路径: admin.{view}.{ctx}.{lXXX_} 或者 common.{ctx}.{lXXX_}
# 先按 'admin' / 'common' / 'public' 顶级块切分
top_level_pattern = re.compile(
    r'^\s{2}(\w+):\s*\{(.*?)(?=^\s{2}\w+:\s*\{|\Z)',
    re.M | re.S
)
top_blocks = top_level_pattern.findall(zh)
print(f'顶级块: {[b[0] for b in top_blocks]}')

# 在每个块内递归找 lXXX_ key + value
# 简化为: 在整个文件中, 提取 lXXX_xxx: 'value' 模式
# 但是要避免误匹配 (e.g. 'l' 不是 key)
flat_pattern = re.compile(
    r"^(\s+)(l\d+_[a-zA-Z0-9_]*):\s*['\"]([^'\"]+)['\"]",
    re.M
)
flat_matches = flat_pattern.findall(zh)
print(f'\n扁平 lXXX_ key 匹配: {len(flat_matches)}')

# 2. 统计 value 出现次数
value_counter = Counter()
key_to_value = {}
for indent, key, value in flat_matches:
    value_counter[value] += 1
    key_to_value[key] = value

# 3. 找重复出现的 value (高频 key 优先语义化)
print(f'\n出现多次的 value (Top 20):')
for value, cnt in value_counter.most_common(20):
    if cnt > 1:
        # 找出对应所有 key
        keys = [k for k, v in key_to_value.items() if v == value]
        print(f'  [{cnt}x] {value!r}: {keys}')
