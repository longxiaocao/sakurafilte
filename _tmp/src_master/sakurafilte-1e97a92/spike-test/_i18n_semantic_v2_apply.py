"""i18n 语义化重组 v2 - 完整重做 (含删除原 lXXX_)

上版问题: 删除 lXXX_ 的正则未生效
本版策略: 先 git stash 恢复, 再用更稳健的方式删除
"""
import re
import json
import subprocess
from pathlib import Path

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'
EN = LOC / 'en-US.ts'
VUE_DIR = ROOT / 'frontend/src'
REPORT = ROOT / 'spike-test/i18n_semantic_v1_plan.json'

# 1) 恢复 i18n 文件到上一次提交 (HEAD)
print('=== 步骤 0: 恢复 i18n 到上一提交 ===')
for p in [ZH, EN]:
    r = subprocess.run(['git', 'checkout', 'HEAD', '--', str(p.relative_to(ROOT))],
                       cwd=str(ROOT), capture_output=True, text=True)
    print(f'  git checkout {p.name}: {r.returncode}')

# 2) 重新加载计划
plan = json.loads(REPORT.read_text(encoding='utf-8'))
REPLACEMENTS = plan['replacements']

# 3) 修改 i18n 文件: 删除 lXXX_ 行 + 新增 common.action
def modify_i18n(ts_path: Path, is_zh: bool) -> tuple:
    text = ts_path.read_text(encoding='utf-8')
    removed = 0
    not_found = []

    for old_full, new_full, zh_v, en_v in REPLACEMENTS:
        short_key = old_full.split('.')[-1]
        target_value = zh_v if is_zh else en_v
        # 构造精确行匹配: 任意空白 + key + : + 'value' + 可选逗号 + 行尾
        # 用 \s* 匹配空白
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

    # 新增 common.action 段 (如果还没有)
    if 'action: {' not in text:
        common_match = re.search(r'^(\s+)common:\s*\{', text, re.M)
        if common_match:
            common_indent = common_match.group(1)
            action_indent = common_indent + '  '
            inner_indent = common_indent + '    '

            UNIQUE_ACTIONS = plan['unique_actions']
            action_lines = []
            action_lines.append(f'{action_indent}action: {{')
            for ua in UNIQUE_ACTIONS:
                val = ua['zh'] if is_zh else ua['en']
                val_escaped = val.replace("'", "\\'")
                action_lines.append(f"{inner_indent}{ua['key']}: '{val_escaped}',")
            action_lines.append(f'{action_indent}}},')

            action_text = '\n' + '\n'.join(action_lines) + '\n'
            text = re.sub(
                r'(^' + re.escape(common_indent) + r'common:\s*\{)\n',
                r'\1' + action_text,
                text,
                count=1,
                flags=re.M
            )

    ts_path.write_text(text, encoding='utf-8')
    return removed, len(not_found), not_found[:5]

print('\n=== 步骤 1: 修改 zh-CN.ts ===')
r_zh, nf_zh, sample_zh = modify_i18n(ZH, True)
print(f'  删除 lXXX_ 行: {r_zh} (期望 145), 未匹配: {nf_zh}')
if sample_zh:
    print(f'  未匹配示例: {sample_zh}')

print('\n=== 步骤 2: 修改 en-US.ts ===')
r_en, nf_en, sample_en = modify_i18n(EN, False)
print(f'  删除 lXXX_ 行: {r_en} (期望 145), 未匹配: {nf_en}')
if sample_en:
    print(f'  未匹配示例: {sample_en}')

# 4) 替换 .vue 文件中的 t() 引用
print('\n=== 步骤 3: 替换 .vue 引用 ===')
# 先恢复到 HEAD 以避免重复替换
for vp in VUE_DIR.rglob('*.vue'):
    r = subprocess.run(['git', 'checkout', 'HEAD', '--', str(vp.relative_to(ROOT))],
                       cwd=str(ROOT), capture_output=True, text=True)
    # 只在有修改时输出
# (静默恢复)

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
for fn, cnt in file_changes[:5]:
    print(f'    {fn}: {cnt} 处')
if len(file_changes) > 5:
    print(f'    ... 还有 {len(file_changes) - 5} 个文件')
