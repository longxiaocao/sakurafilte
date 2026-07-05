"""i18n 语义化 v2 - 应用替换 (与 v3 类似, 但读取 v2 plan)"""
import re
import json
import subprocess
from pathlib import Path

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'
EN = LOC / 'en-US.ts'
VUE_DIR = ROOT / 'frontend/src'
REPORT = ROOT / 'spike-test/i18n_semantic_v2_plan.json'

plan = json.loads(REPORT.read_text(encoding='utf-8'))
REPLACEMENTS = plan['replacements']
UNIQUE_ACTIONS = [
    {'key': r['new'].split('.')[-1], 'zh': r['zh'], 'en': r['en']}
    for r in REPLACEMENTS
]
# 去重
seen = set()
unique = []
for ua in UNIQUE_ACTIONS:
    if ua['key'] not in seen:
        seen.add(ua['key'])
        unique.append(ua)
UNIQUE_ACTIONS = unique
print(f'v2 unique field keys: {len(UNIQUE_ACTIONS)}')

def modify_i18n(ts_path: Path, is_zh: bool) -> tuple:
    text = ts_path.read_text(encoding='utf-8')
    removed = 0
    not_found = []

    for r in REPLACEMENTS:
        old_full = r['old']
        new_full = r['new']
        zh_v = r['zh']
        en_v = r['en']
        short_key = old_full.split('.')[-1]
        target_value = zh_v if is_zh else en_v

        line_pattern = re.compile(
            r'^[ \t]*' + re.escape(short_key) + r"\s*:\s*['\"]" + re.escape(target_value) + r"['\"][,]?\s*$",
            re.M
        )
        new_text, count = line_pattern.subn('', text)
        if count > 0:
            text = new_text
            removed += count
        else:
            not_found.append((short_key, target_value))

    # 新增 common.field 段
    if 'field: {' not in text:
        common_match = re.search(r'^(\s+)common:\s*\{', text, re.M)
        if common_match:
            common_indent = common_match.group(1)
            field_indent = common_indent + '  '
            inner_indent = common_indent + '    '

            field_lines = [f'{field_indent}field: {{']
            for ua in UNIQUE_ACTIONS:
                val = ua['zh'] if is_zh else ua['en']
                val_escaped = val.replace("'", "\\'")
                field_lines.append(f"{inner_indent}{ua['key']}: '{val_escaped}',")
            field_lines.append(f'{field_indent}}},')
            field_text = '\n' + '\n'.join(field_lines) + '\n'

            # 在 common: {  后插入 (在 action 段后)
            # 找 action 块的结尾 }, 然后插入
            # 简化: 在 common: {  后, 紧跟着 action 段 + field 段
            text = re.sub(
                r'(^' + re.escape(common_indent) + r'common:\s*\{)\n',
                r'\1' + field_text,
                text,
                count=1,
                flags=re.M
            )

    ts_path.write_text(text, encoding='utf-8')
    return removed, len(not_found), not_found[:5]

print('=== 步骤 1: 修改 zh-CN.ts ===')
r_zh, nf_zh, sample_zh = modify_i18n(ZH, True)
print(f'  删除 lXXX_ 行: {r_zh} (期望 86), 未匹配: {nf_zh}')
if sample_zh:
    print(f'  未匹配示例: {sample_zh}')

print('\n=== 步骤 2: 修改 en-US.ts ===')
r_en, nf_en, sample_en = modify_i18n(EN, False)
print(f'  删除 lXXX_ 行: {r_en} (期望 86), 未匹配: {nf_en}')
if sample_en:
    print(f'  未匹配示例: {sample_en}')

print('\n=== 步骤 3: 替换 .vue 引用 ===')
short_map = {r['old']: r['new'] for r in REPLACEMENTS}
vue_files = list(VUE_DIR.rglob('*.vue'))
total_replaced = 0
file_changes = []
for vp in vue_files:
    text = vp.read_text(encoding='utf-8')
    orig = text
    file_count = 0
    for old, new in short_map.items():
        for quote in ["'", '"', '`']:
            for prefix in ['t(', '$t(', 'i18n.t(']:
                old_full = f"{prefix}{quote}{old}{quote}"
                new_full = f"{prefix}{quote}{new}{quote}"
                if old_full in text:
                    cnt = text.count(old_full)
                    text = text.replace(old_full, new_full)
                    file_count += cnt
    if text != orig:
        vp.write_text(text, encoding='utf-8')
        file_changes.append((vp.name, file_count))
        total_replaced += 1

print(f'  替换文件数: {total_replaced}, 总替换处: {sum(c for _, c in file_changes)}')
