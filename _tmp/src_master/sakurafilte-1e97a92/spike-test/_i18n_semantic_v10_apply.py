"""i18n 语义化 v10 - 162 个有 suffix key 重命名 (保留缩进)

修复 v9 bug: 必须保留原缩进
策略: 不直接修改行, 而是先收集所有需要改的 (line_num, old_key, new_key, value, indent),
       然后逐行修改
"""
import re
import json
from pathlib import Path
from collections import defaultdict

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'
EN = LOC / 'en-US.ts'
VUE_DIR = ROOT / 'frontend/src'
REPORT = ROOT / 'spike-test/i18n_semantic_v9_plan.json'

plan = json.loads(REPORT.read_text(encoding='utf-8'))
REPLACEMENTS = plan['replacements']  # 162 个

# 1. 在 i18n 文件中重命名 (保留缩进)
# 策略: 找到每个 lXXX_xxx 行的位置, 替换 key 部分, 保留缩进
def rename_in_i18n(ts_path: Path, is_zh: bool) -> tuple:
    text = ts_path.read_text(encoding='utf-8')
    lines = text.split('\n')
    renamed = 0
    not_found = []

    # 构造 value -> [(old_short, new_short)] 映射 (一个 value 可能对应多个 lXXX_)
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
                # 找到对应的 rename
                renames = val_to_renames[val]
                # 取第一个匹配的 (old_short == key)
                match = next(((old, new) for old, new in renames if old == key), None)
                if match:
                    old_short, new_short = match
                    # 替换 key 部分, 保留缩进
                    new_line = line.replace(old_short + ":", new_short + ":", 1)
                    new_lines.append(new_line)
                    renamed += 1
                    continue
        new_lines.append(line)

    text = '\n'.join(new_lines)
    ts_path.write_text(text, encoding='utf-8')
    return renamed, not_found

print('=== 步骤 1: 修改 zh-CN.ts (保留缩进) ===')
r_zh, nf_zh = rename_in_i18n(ZH, True)
print(f'  重命名: {r_zh} (期望 162)')

print('\n=== 步骤 2: 修改 en-US.ts (保留缩进) ===')
r_en, nf_en = rename_in_i18n(EN, False)
print(f'  重命名: {r_en} (期望 162)')

# 验证
print('\n=== 步骤 3: 验证语法 ===')
import subprocess
for p in [ZH, EN]:
    r = subprocess.run(
        ['node', '-e', f'const t = require("fs").readFileSync("{p.as_posix()}", "utf-8"); const c = t.replace(/^export\\s+default\\s+/m, "").replace(/;\\s*$/, ""); try {{ eval("(" + c + ")"); console.log("{p.name}: OK"); }} catch (e) {{ console.log("{p.name}: FAIL -", e.message); }}'],
        capture_output=True, text=True
    )
    print(f'  {r.stdout.strip()}')

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
    print(f'    {fn}: {cnt} 处')
