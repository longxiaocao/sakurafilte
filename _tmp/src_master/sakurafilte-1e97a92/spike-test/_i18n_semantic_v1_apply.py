"""i18n 语义化重组 v1 - 执行替换

步骤:
  1. 解析 zh-CN.ts / en-US.ts
  2. 删除 lXXX_ key (高频公共 value 的 145 个)
  3. 新增 common.action.* 段
  4. 在所有 .vue 文件中替换 t('admin.{view}.{ctx}.lXXX_') -> t('common.action.{sem}')
  5. 写回文件
  6. 跑 i18n 完整路径审计验证
"""
import re
import json
from pathlib import Path
from collections import OrderedDict

ROOT = Path('d:/projects/sakurafilter')
LOC = ROOT / 'frontend/src/i18n/locales'
ZH = LOC / 'zh-CN.ts'
EN = LOC / 'en-US.ts'
VUE_DIR = ROOT / 'frontend/src'
REPORT = ROOT / 'spike-test/i18n_semantic_v1_plan.json'

# 1. 加载计划
plan = json.loads(REPORT.read_text(encoding='utf-8'))
SEMANTIC_MAP = plan['semantic_map']
UNIQUE_ACTIONS = plan['unique_actions']
REPLACEMENTS = plan['replacements']  # 145 条

# 2. 修改 i18n 文件
#    策略: 用正则, 找形如 '   l273_: \"value\"' 的行, 删除
#    简单的字符串 replace 即可

def remove_lxxx_and_add_common_action(ts_path: Path, value_to_action: dict):
    """删除高频 value 对应的 lXXX_ 行, 并在 common 下新增 action 段"""
    text = ts_path.read_text(encoding='utf-8')

    # 1) 删除 lXXX_ 行: 注意是行级替换, 需要保留缩进
    removed = 0
    for old_full, new_full, zh_v, en_v in REPLACEMENTS:
        # old_full 形如 'admin.compareview.string.l58_1'
        # 提取最后一段 key
        short_key = old_full.split('.')[-1]
        # 在 text 中找 形如 '    l58_1: 'value',' 的行
        # 用正则匹配行: 缩进 + key + : + 值
        # 必须精确匹配 value, 避免误删同名 key
        # value 是 en 或 zh, 我们用 zh_v 即可 (zh 一定在 zh-CN.ts, en 一定在 en-US.ts)
        target_value = zh_v if 'zh-CN' in ts_path.name else en_v
        # 转义特殊字符
        escaped_val = re.escape(target_value)
        # 行匹配: 缩进 + 短 key + : + 'value' + (逗号或分号)
        line_pattern = re.compile(
            r'^[ \t]+' + re.escape(short_key) + r":\s*['\"]" + escaped_val + r"['\"][,]?\s*$",
            re.M
        )
        new_text, count = line_pattern.subn('', text)
        if count > 0:
            text = new_text
            removed += count

    # 2) 在 common 下新增 action 段
    #    找 common: { dictviewcommon: { ... } }  形如
    #    简化: 直接在 common: {  后插入 action: { ... }
    #    先确定 common 块的缩进
    common_match = re.search(r'^(\s+)common:\s*\{', text, re.M)
    if not common_match:
        print(f'  [WARN] 未找到 common 块 in {ts_path.name}')
        return removed, False
    common_indent = common_match.group(1)
    action_indent = common_indent + '  '
    inner_indent = common_indent + '    '

    # 构造 action 段
    action_lines = []
    action_lines.append(f'{action_indent}action: {{')
    for ua in UNIQUE_ACTIONS:
        val = ua['zh'] if 'zh-CN' in ts_path.name else ua['en']
        # 转义单引号
        val_escaped = val.replace("'", "\\'")
        action_lines.append(f"{inner_indent}{ua['key']}: '{val_escaped}',")
    action_lines.append(f'{action_indent}}},')

    action_text = '\n' + '\n'.join(action_lines) + '\n'

    # 在 common: {  之后插入
    text = re.sub(
        r'(^' + re.escape(common_indent) + r'common:\s*\{)\n',
        r'\1' + action_text,
        text,
        count=1,
        flags=re.M
    )

    # 写回
    ts_path.write_text(text, encoding='utf-8')
    return removed, True

# 3. 替换 .vue 文件中的 t() 引用
#    形如: t('admin.compareview.string.l58_1', ...)
#    替换为: t('common.action.product_name_1', ...)
#    注意: 也可能是 "t("...")" 双引号形式
def replace_vue_refs():
    # 构造 (old_short, new_short) 映射
    # old_short = 'admin.{view}.{ctx}.lXXX_'
    # new_short = 'common.action.{sem}'
    short_map = {}
    for r in REPLACEMENTS:
        old = r['old']
        new = r['new']
        short_map[old] = new

    # 找所有 .vue
    vue_files = list(VUE_DIR.rglob('*.vue'))
    total_replaced = 0
    file_changes = []
    for vp in vue_files:
        text = vp.read_text(encoding='utf-8')
        orig = text
        file_count = 0
        for old, new in short_map.items():
            # 匹配 t('old', ...) / t("old", ...) / $t('old', ...) / i18n.t('old', ...)
            # 简单: 用字符串替换, 注意要保留引号
            for quote in ["'", '"', '`']:
                # 形式 1: t('old', ...) - 含单引号
                old1 = f"t({quote}{old}{quote}"
                new1 = f"t({quote}{new}{quote}"
                if old1 in text:
                    text = text.replace(old1, new1)
                    file_count += text.count(new1) - orig.count(new1)
                # 形式 2: $t('old', ...)
                old2 = f"$t({quote}{old}{quote}"
                new2 = f"$t({quote}{new}{quote}"
                if old2 in text:
                    text = text.replace(old2, new2)
                # 形式 3: i18n.t('old', ...)
                old3 = f"i18n.t({quote}{old}{quote}"
                new3 = f"i18n.t({quote}{new}{quote}"
                if old3 in text:
                    text = text.replace(old3, new3)
        if text != orig:
            vp.write_text(text, encoding='utf-8')
            file_changes.append((vp.name, file_count))
            total_replaced += 1
    return total_replaced, file_changes

# 主流程
print('=== 步骤 1: 修改 zh-CN.ts ===')
removed_zh, ok_zh = remove_lxxx_and_add_common_action(ZH, SEMANTIC_MAP)
print(f'  删除 lXXX_ 行: {removed_zh}, 新增 common.action: {ok_zh}')

print('\n=== 步骤 2: 修改 en-US.ts ===')
removed_en, ok_en = remove_lxxx_and_add_common_action(EN, SEMANTIC_MAP)
print(f'  删除 lXXX_ 行: {removed_en}, 新增 common.action: {ok_en}')

print('\n=== 步骤 3: 替换 .vue 引用 ===')
n_files, changes = replace_vue_refs()
print(f'  替换文件数: {n_files}')
for fn, cnt in changes[:10]:
    print(f'    {fn}: ~{cnt} 处')
if len(changes) > 10:
    print(f'    ... 还有 {len(changes) - 10} 个文件')

print('\n=== 步骤 4: 验证 ===')
print('  请运行 python spike-test/_i18n_fullpath_audit.py 检查')
