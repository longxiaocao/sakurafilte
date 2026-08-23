"""i18n 语义化 v5 - 第二轮 2x+ 提取"""
import re
import json
from pathlib import Path
from collections import Counter

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'
EN = LOC / 'en-US.ts'

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

# 解析双语
zh_dict = parse_i18n(ZH)
en_dict = parse_i18n(EN)

# 提取所有 lXXX_ key + 完整路径
all_lxxx = []
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
print(f'剩余 lXXX_: {len(all_lxxx)}')

# 统计 2x+ value
vcount = Counter(item[2] for item in all_lxxx)
high_freq_2x = [(v, cnt) for v, cnt in vcount.items() if cnt >= 2]
high_freq_2x.sort(key=lambda x: (-x[1], x[0]))
print(f'\\n2x+ value: {len(high_freq_2x)}')

# 设计语义化 key (按 v 后缀和上下文)
SEMANTIC_MAP_V2 = {
    # 产品字段 (compare & productform 共用)
    '箱长 (mm)': 'carton_length_mm',
    '箱宽 (mm)': 'carton_width_mm',
    '箱高 (mm)': 'carton_height_mm',
    '箱体积 (m³)': 'carton_volume_m3',
    '性能': 'performance',
    'D7 螺纹': 'd7_thread',
    'D8 螺纹': 'd8_thread',
    '单向阀数': 'check_valve_count',
    '旁通阀数': 'bypass_valve_count',
    '效率 1': 'efficiency_1',
    '效率 2': 'efficiency_2',
    '旁通压力': 'bypass_pressure',
    '温度范围': 'temperature_range',
    '包装': 'packaging',
    '重量 (kg)': 'weight_kg',
    # 通用
    '搜索任一字段': 'search_any_field',
    '不取消': 'no_cancel',
    '无活跃任务可取消': 'no_active_task_to_cancel',
    '模式': 'mode',
    '用户主动取消': 'user_cancelled',
    # 视图
    '外箱': 'outer_carton',
    '外箱/件': 'outer_carton_per_pcs',
    '外箱重 (kg)': 'outer_carton_weight_kg',
    '外箱长 (mm)': 'outer_carton_length_mm',
    '外箱宽 (mm)': 'outer_carton_width_mm',
    '外箱高 (mm)': 'outer_carton_height_mm',
}

# 自动为未在 map 中的高频 value 生成 key 名
def auto_name(zh_v):
    # 简单: 转拼音 or 保留中英对照
    # 这里用 pinyin 简化太复杂, 用 hash 化简
    safe = re.sub(r'[^a-zA-Z0-9_\u4e00-\u9fff]+', '_', zh_v)
    safe = safe.strip('_')[:30]
    return f'v_{abs(hash(zh_v)) % 99999}'

# 收集所有需要替换的
replacements_v2 = []
for v, cnt in high_freq_2x:
    sem = SEMANTIC_MAP_V2.get(v, auto_name(v))
    for full_path, key, zh_v, en_v in all_lxxx:
        if zh_v == v:
            new_path = f'common.field.{sem}'
            replacements_v2.append({
                'old': full_path,
                'new': new_path,
                'zh': zh_v,
                'en': en_v,
                'semantic': sem
            })

print(f'\\nv2 替换数: {len(replacements_v2)}')

# 写入 plan
plan_v2 = {
    'version': 'v2',
    'unique_action_count_v2': len(high_freq_2x),
    'replacement_count': len(replacements_v2),
    'semantic_map': SEMANTIC_MAP_V2,
    'high_freq_2x': high_freq_2x,
    'replacements': replacements_v2,
    'auto_generated': [(v, SEMANTIC_MAP_V2.get(v, auto_name(v))) for v, _ in high_freq_2x],
}
out = ROOT / 'spike-test/i18n_semantic_v2_plan.json'
out.write_text(json.dumps(plan_v2, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'\\n报告: {out}')

# 打印 auto-generated 列表
print('\\nAuto-generated keys (未在手动 map):')
for v, sem in plan_v2['auto_generated']:
    if v not in SEMANTIC_MAP_V2:
        print(f'  {sem} = {v!r}')
