"""i18n 语义化 v6 - 第二轮 2x+ 提取 (完整手工 key)"""
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

zh_dict = parse_i18n(ZH)
en_dict = parse_i18n(EN)

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

vcount = Counter(item[2] for item in all_lxxx)
high_freq_2x = [(v, cnt) for v, cnt in vcount.items() if cnt >= 2]
high_freq_2x.sort(key=lambda x: (-x[1], x[0]))

# 完整手工语义化 map
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
    '外箱': 'outer_carton',
    '外箱/件': 'outer_carton_per_pcs',
    '外箱重 (kg)': 'outer_carton_weight_kg',
    '外箱长 (mm)': 'outer_carton_length_mm',
    '外箱宽 (mm)': 'outer_carton_width_mm',
    '外箱高 (mm)': 'outer_carton_height_mm',
    # 剩余手工
    ' 吗? (软删除, 可在': 'soft_delete_confirm',
    ', 必须在 1-6 之间': 'slot_must_be_1_to_6',
    'OEM 品牌': 'oem_brand',
    'Slot 非法: ': 'invalid_slot',
    '不限': 'unlimited',
    '产品名': 'product_name',
    '例: BOSCH': 'e_g_bosch',
    '全名': 'full_name',
    '全部': 'all',
    '其他原因': 'other_reason',
    '发动机品牌': 'engine_brand',
    '发布': 'publish',
    '取消': 'cancel',
    '拖动以排序': 'drag_to_sort',
    '故障': 'fault',
    '检测中': 'detecting',
    '用户名': 'username',
    '管理员强制取消': 'admin_force_cancel',
    '自动计算': 'auto_calculated',
    '至少 8 个字符': 'at_least_8_chars',
    '角色': 'role',
    '输入自动补全': 'input_autocomplete',
    '邮箱': 'email',
}

# 检查缺失
missed = [v for v, _ in high_freq_2x if v not in SEMANTIC_MAP_V2]
if missed:
    print(f'WARNING: {len(missed)} values missing semantic key:')
    for v in missed:
        print(f'  {v!r}')
    raise SystemExit(1)

# 收集替换
replacements_v2 = []
for v, cnt in high_freq_2x:
    sem = SEMANTIC_MAP_V2[v]
    for full_path, key, zh_v, en_v in all_lxxx:
        if zh_v == v:
            new_path = f'common.field.{sem}'
            replacements_v2.append({
                'old': full_path,
                'new': new_path,
                'zh': zh_v,
                'en': en_v,
            })

print(f'v2 替换数: {len(replacements_v2)}')

# 保存 plan
plan_v2 = {
    'version': 'v2',
    'unique_value_count': len(high_freq_2x),
    'replacement_count': len(replacements_v2),
    'semantic_map': SEMANTIC_MAP_V2,
    'replacements': replacements_v2,
}
out = ROOT / 'spike-test/i18n_semantic_v2_plan.json'
out.write_text(json.dumps(plan_v2, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'报告: {out}')
