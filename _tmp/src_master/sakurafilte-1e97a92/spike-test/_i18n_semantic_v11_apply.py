"""i18n 语义化 v11 - 96 个 lXXX_ (无 suffix) 重命名 (保留缩进)"""
import re
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'
EN = LOC / 'en-US.ts'
VUE_DIR = ROOT / 'frontend/src'
REPORT = ROOT / 'spike-test/i18n_semantic_v8_plan.json'

plan = json.loads(REPORT.read_text(encoding='utf-8'))
REPLACEMENTS = plan['replacements']  # 96 个

# 1. 在 i18n 文件中重命名 (保留缩进) - 同样的 v10 逻辑
def rename_in_i18n(ts_path: Path, is_zh: bool) -> tuple:
    text = ts_path.read_text(encoding='utf-8')
    lines = text.split('\n')
    renamed = 0

    val_to_renames = defaultdict(list)
    for r in REPLACEMENTS:
        val = r['zh'] if is_zh else r['en']
        val_to_renames[val].append((r['old_short'], r['new_short']))

    new_lines = []
    for line in lines:
        stripped = line.strip()
        m = re.match(r"^(\s*)(l\d+_[a-zA-Z0-9_]*):\s*['\"](.+?)['\"][,]?\s*$", stripped)
        if m:
            key, val = m.group(2), m.group(3)
            if val in val_to_renames:
                renames = val_to_renames[val]
                match = next(((old, new) for old, new in renames if old == key), None)
                if match:
                    old_short, new_short = match
                    new_line = line.replace(old_short + ":", new_short + ":", 1)
                    new_lines.append(new_line)
                    renamed += 1
                    continue
        new_lines.append(line)

    text = '\n'.join(new_lines)
    ts_path.write_text(text, encoding='utf-8')
    return renamed

print('=== 步骤 1: 修改 zh-CN.ts (保留缩进) ===')
r_zh = rename_in_i18n(ZH, True)
print(f'  重命名: {r_zh} (期望 96)')

print('\n=== 步骤 2: 修改 en-US.ts (保留缩进) ===')
r_en = rename_in_i18n(EN, False)
print(f'  重命名: {r_en} (期望 96)')

# 验证语法
print('\n=== 步骤 3: 验证语法 ===')
import subprocess
for p in [ZH, EN]:
    r = subprocess.run(
        ['node', '-e', f'const t = require("fs").readFileSync("{p.as_posix()}", "utf-8"); const c = t.replace(/^export\\s+default\\s+/m, "").replace(/;\\s*$/, ""); try {{ eval("(" + c + ")"); console.log("{p.name}: OK"); }} catch (e) {{ console.log("{p.name}: FAIL -", e.message); }}'],
        capture_output=True, text=True
    )
    print('  ' + r.stdout.strip())

# 替换 .vue 引用
print('\n=== 步骤 4: 替换 .vue 引用 ===')
short_map = {r['old_short']: r['new_short'] for r in REPLACEMENTS}
vue_files = list(VUE_DIR.rglob('*.vue'))
total_replaced = 0
file_changes = []
for vp in vue_files:
    text = vp.read_text(encoding='utf-8')
    orig = text
    file_count = 0
    for old_short, new_short in short_map.items():
        for quote in ["'", '"', '`']:
            old_pat = f'.{old_short}{quote}'
            new_pat = f'.{new_short}{quote}'
            cnt = text.count(old_pat)
            if cnt > 0:
                text = text.replace(old_pat, new_pat)
                file_count += cnt
    if text != orig:
        vp.write_text(text, encoding='utf-8')
        file_changes.append((vp.name, file_count))
        total_replaced += 1

print(f'  替换文件数: {total_replaced}, 总替换处: {sum(c for _, c in file_changes)}')
for fn, cnt in file_changes[:10]:
    print('    ' + fn + ': ' + str(cnt) + ' 处')
