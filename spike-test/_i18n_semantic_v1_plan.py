"""i18n 语义化重组 v1 - 高频公共 key 提取

策略:
  1. 提取 24 个高频 value (出现 >= 3 次) 为 common.action.* 公共 key
  2. 在 zh-CN.ts/en-US.ts 中:
     - 新增 common.action.{name} = value
     - 删除原 lXXX_ key (因重复)
  3. 在 .vue 文件中: 把 t('admin.{view}.{ctx}.lXXX_') 替换为 t('common.action.{name}')
  4. 提供回滚脚本

WHY: 489 个 lXXX_ key 中 24 个高频 value 重复 3-11 次, 提取后:
  - i18n 文件减少 ~150 行重复
  - 未来修改文案只需改一处
  - 提升可维护性

风险: 替换失败会导致 t() 找不到 key → Vue 警告
  兜底: 替换前先全量备份, 替换后跑 i18n 完整路径审计, 任何 key 找不到都回滚
"""
import re
import json
from pathlib import Path
from collections import Counter, defaultdict

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'
EN = LOC / 'en-US.ts'
VUE_DIR = ROOT / 'frontend/src'

# 1. 解析 i18n 嵌套结构
def parse_i18n(path: Path) -> dict:
    text = path.read_text(encoding='utf-8')
    text = re.sub(r'export\s+default\s+', '', text)
    text = re.sub(r';\s*$', '', text)

    lines = text.split('\n')
    root = {}
    stack = []  # [(indent, name, dict)]
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
            continue

    return root

# 2. 提取所有 lXXX_ key, 统计 value 频率
zh_dict = parse_i18n(ZH)
en_dict = parse_i18n(EN)

all_lxxx = []  # [(full_path, key, zh_value, en_value)]
def walk(d_zh, d_en, prefix=''):
    for k, v in d_zh.items():
        full = f'{prefix}.{k}' if prefix else k
        if isinstance(v, dict):
            walk(v, d_en.get(k, {}), full)
        else:
            if re.match(r'^l\d+_', k):
                en_v = d_en.get(k, '') if isinstance(d_en.get(k), str) else ''
                all_lxxx.append((full, k, v, en_v))

walk(zh_dict, en_dict)

# 3. 找出高频 value (>= 3 次)
value_counter = Counter(item[2] for item in all_lxxx)
high_freq = [(v, cnt) for v, cnt in value_counter.items() if cnt >= 3]
high_freq.sort(key=lambda x: -x[1])
print(f'高频 value (>=3 次): {len(high_freq)}')
for v, cnt in high_freq:
    print(f'  [{cnt:2d}x] {v!r}')

# 4. 为每个高频 value 设计语义化 key 名
# 规则: 英文 (基于 en value) / 中文 (基于 zh value)
SEMANTIC_MAP = {
    '确认': 'confirm',
    '恢复失败': 'restore_failed',
    '已删除': 'deleted',
    '已恢复': 'restored',
    '加载失败: ': 'load_failed',
    '操作失败': 'operation_failed',
    '删除失败': 'delete_failed',
    '排序失败': 'sort_failed',
    '排序': 'sort_order',
    '已新增': 'created',
    '已更新': 'updated',
    '>暂无数据, 点击右上': 'no_data_click_top_right',
    '排序已保存': 'sort_order_saved',
    '产品名 1': 'product_name_1',
    '产品名 2': 'product_name_2',
    '类型': 'type',
    '品牌': 'brand',
    '可选': 'optional',
    '密封材料': 'seal_material',
    '箱/件': 'carton_per_pcs',
    '型号': 'model',
    '恢复': 'resume',
    '名称': 'name',
}

# 5. 找出所有原 lXXX_ key 的完整路径 + 对应的语义化 key 名
# 反向: zh_value -> semantic name
zh_to_semantic = {zh: SEMANTIC_MAP[zh] for zh in SEMANTIC_MAP}

# 收集需要替换的 (old_full_path, new_full_path)
replacements = []  # [(old_path, new_path), ...]
for full_path, key, zh_v, en_v in all_lxxx:
    if zh_v in zh_to_semantic:
        sem = zh_to_semantic[zh_v]
        new_path = f'common.action.{sem}'
        replacements.append((full_path, new_path, zh_v, en_v))

print(f'\n需要替换的 (lXXX_ -> common.action.*): {len(replacements)}')

# 6. 在 zh-CN.ts / en-US.ts 中:
#    - 新增 common.action.* 段
#    - 删除原 lXXX_ key (仅在 common.action.* 第一次出现时保留为 alias? 不, 简化直接删除)

# 7. 收集去重后的 common.action.* 列表 (去重按 value, 取首次出现)
seen_values = set()
unique_actions = []
for old_path, new_path, zh_v, en_v in replacements:
    if zh_v in seen_values:
        continue
    seen_values.add(zh_v)
    sem = zh_to_semantic[zh_v]
    unique_actions.append((sem, zh_v, en_v))

print(f'\n去重后的公共 action 列表 ({len(unique_actions)}):')
for sem, zh_v, en_v in unique_actions:
    print(f'  common.action.{sem} = {zh_v!r} / {en_v!r}')

# 8. 写入 JSON 报告
report = {
    'high_freq_count': len(high_freq),
    'replacement_count': len(replacements),
    'unique_action_count': len(unique_actions),
    'semantic_map': SEMANTIC_MAP,
    'unique_actions': [{'key': sem, 'zh': zh_v, 'en': en_v} for sem, zh_v, en_v in unique_actions],
    'replacements': [{'old': old, 'new': new, 'zh': zh_v, 'en': en_v} for old, new, zh_v, en_v in replacements],
}

report_path = ROOT / 'spike-test/i18n_semantic_v1_plan.json'
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'\n报告: {report_path}')
