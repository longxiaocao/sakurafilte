"""应用 v3 报告中的翻译到 zh-CN.ts / en-US.ts 的 common.feedback 块

策略:
- 只追加 value 到空块 feedback: {}
- 过滤 from_override=false 的低质量翻译
- 占位符错位的 key 改用更稳定的占位符或跳过
"""
import json
import re
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\frontend\src\i18n\locales")
V3_REPORT = Path(r"d:\projects\sakurafilter\spike-test\i18n_business_v3_report.json")

# 加载 v3 报告
data = json.loads(V3_REPORT.read_text(encoding="utf-8"))
items = data["new_keys"]

# 过滤 + 修正
# from_override=false 的都是占位符错位的低质量翻译, 跳过
# 但我们重新整理占位符, 写出可用的版本

# 手工修正: 占位符错位的 key, 用更合理的占位符
manual_fix = {
    "common.feedback.info_002": {
        "zh": "即将触发 ${entity} ETL (${mode}${dryRun ? ', 试运行' : ''}), 是否继续?",
        "en": "About to trigger ${entity} ETL (${mode}${dryRun ? ', dry run' : ''}), continue?",
    },
    "common.feedback.info_006": {
        "zh": "网络异常: ${err.message || '请稍后重试'}",
        "en": "Network exception: ${err.message || 'please try again later'}",
    },
    "common.feedback.success_002": {
        "zh": "已发送暂停信号 (码: ${code}), 任务即将终止",
        "en": "Pause signal sent (code: ${code}), task will terminate soon",
    },
    "common.feedback.info_005": {
        "zh": "粘贴 OEM 编号, 每行一个 (支持 tab/换行/逗号/分号分隔)&#10;例如:&#10;OEN-123&#10;AB/CD/456&#10;滤清器 1142",
        "en": "Paste OEM numbers, one per line (tab/line break/comma/semicolon delimited)&#10;Example:&#10;OEN-123&#10;AB/CD/456&#10;Filter 1142",
    },
}

# 分类
to_apply = []  # [(key, zh, en)]
for item in items:
    key = item["key"]
    if key in manual_fix:
        to_apply.append((key, manual_fix[key]["zh"], manual_fix[key]["en"]))
    elif item.get("from_override"):
        to_apply.append((key, item["zh"], item["en"]))
    # from_override=false 且不在 manual_fix 的, 跳过

print(f"准备应用 {len(to_apply)} 个 key:")
for k, zh, en in to_apply:
    print(f"  {k}")

# 写入文件
for lang, fp in [("zh", ROOT / "zh-CN.ts"), ("en", ROOT / "en-US.ts")]:
    txt = fp.read_text(encoding="utf-8")
    # 找 feedback: {} 替换
    pat = re.compile(r"(\s+)feedback:\s*\{\s*\},?\n", re.DOTALL)
    m = pat.search(txt)
    if not m:
        print(f"[WARN] {fp.name} 未找到 feedback: {{}} 块")
        continue
    # 构造新内容
    new_inner = "\n"
    for k, zh, en in to_apply:
        val = zh if lang == "zh" else en
        # 转义单引号
        val = val.replace("\\", "\\\\").replace("'", "\\'")
        short = k.split(".")[-1]  # info_007
        new_inner += f"      {short}: '{val}',\n"
    new_inner += "    "
    new_txt = txt[:m.start()] + new_inner + txt[m.end():]
    fp.write_text(new_txt, encoding="utf-8")
    print(f"[OK] {fp.name} 已写入 {len(to_apply)} 个 key")

print("\n=== 完成 ===")
