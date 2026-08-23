"""
源中文批量规范化
==================
WHY: 中文质量审查 (chinese_quality_audit.json) 标记 107 处待修正,
  但其中 96 处"过长"是 placeholder 提示/帮助文档, 属于合理设计.
  真正需要修正的:
    1. 20 处全角标点 → 半角
    2. 14 处长英/数字间缺空格 → 加空格

策略: 用审查报告的 details 字段, 找到 file/line, 用 Edit 工具精确替换.
为安全起见, 每次替换前先做存在性检查, 避免误改.

NOTE: "过长"问题不处理 - 那是有意为之的说明文字, 强制截断会丢失信息.
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPORT = ROOT / "spike-test" / "chinese_quality_audit.json"


# 全角 → 半角映射 (按审查报告的 issue 出现, 不做泛化替换, 避免误伤)
FULL_WIDTH_FIXES = [
    # 来自 AdminEtlView.vue:351 - 中文句号在 ElMessageBox
    ("按钮从该点续读。\n\n", "按钮从该点续读.\n\n"),
    # AdminEtlView.vue:375
    ("行开始续读, 跳过已 COMMIT 的批次。", "行开始续读, 跳过已 COMMIT 的批次."),
    # AdminEtlView.vue:534
    ("当前无活跃任务。 等待触发或查看历史任务。", "当前无活跃任务. 等待触发或查看历史任务."),
    # AdminHelpView 多处: 全角句号
    # AdminHelpView.vue:30
    ("新增 value。typeahead 只返回字典内已存在的值 (前 20 条按 sort_order 排)。",
     "新增 value. typeahead 只返回字典内已存在的值 (前 20 条按 sort_order 排)."),
    # AdminHelpView.vue:38
    ("看是否有 SQL 错误。", "看是否有 SQL 错误."),
    # AdminHelpView.vue:42 - '前台不展示, 历史数据保留。如需物理删除, 走 SQL (慎用)。'
    ("前台不展示, 历史数据保留。如需物理删除, 走 SQL (慎用)。",
     "前台不展示, 历史数据保留. 如需物理删除, 走 SQL (慎用)."),
    # AdminHelpView.vue:94
    ("后台可维护的标准值集合。前台 typeahead / 后台表单 / 公开搜索均从字典取, 保证全站一致。",
     "后台可维护的标准值集合. 前台 typeahead / 后台表单 / 公开搜索均从字典取, 保证全站一致."),
    # AdminHelpView.vue:115
    ("按钮, sort_order 持久化, 前台展示按 sort_order 升序。",
     "按钮, sort_order 持久化, 前台展示按 sort_order 升序."),
    # AdminHelpView.vue:138
    ("命中 H1\n\n的所有产品。\n\n", "命中 H1\n\n的所有产品.\n\n"),
    # AdminHelpView.vue:139
    ("前端不暴露切换。", "前端不暴露切换."),
    # AdminHelpView.vue:142
    ("多字段组合走 AND 关系 (收窄), 单字段命中即返回 (公开搜索 8 字段同时支持)。",
     "多字段组合走 AND 关系 (收窄), 单字段命中即返回 (公开搜索 8 字段同时支持)."),
    # AdminHelpView.vue:165
    ("图标 (鼠标悬停)。", "图标 (鼠标悬停)."),
    # AdminProductFormView.vue:171
    ("产品已存在，请检查 OEM 号", "产品已存在, 请检查 OEM 号"),
    # AdminTypesView.vue:63
    ("吗? 建议保留 (作为 P2.3 兜底), 但仍支持软删恢复。",
     "吗? 建议保留 (作为 P2.3 兜底), 但仍支持软删恢复."),
]


# 数字/英文间缺空格修复
SPACE_FIXES = [
    # AdminEnginesView.vue:159 - 4.5L → 4.5 L
    ("例: ISB 4.5L (可空)", "例: ISB 4.5 L (可空)"),
    # AdminHelpView.vue:38 - 1M 行 → 1 M 行 (但这样更怪), 改保留
    # "5 分钟" → 保留
    # AdminHelpView.vue:46 - slot 1-6 范围 - 已经是 1-6 范围, 没缺空格
    # AdminHelpView.vue:130 - 1M products / 5M xrefs / 1M apps - 这是简称
    # AdminHelpView.vue:138 - H1 (字段名) - H1 本身不需要加空格
    # AdminProductFormView.vue:507 - P2.2 - 已是 P2.2 不是 P22
    # AdminTypesView.vue:63 - P2.3 - 同上
]


def main():
    print("=== 源中文规范化 ===\n")

    # 1. 全角 → 半角
    print(f"[1] 全角标点修正: {len(FULL_WIDTH_FIXES)} 处")
    fw_fixed = 0
    for old, new in FULL_WIDTH_FIXES:
        # 查找哪个文件
        for vp in (ROOT / "frontend" / "src" / "views" / "admin").rglob("*.vue"):
            text = vp.read_text(encoding="utf-8")
            if old in text:
                text = text.replace(old, new, 1)
                vp.write_text(text, encoding="utf-8")
                rel = vp.relative_to(ROOT)
                print(f"  ✓ {rel}: {old[:30]!r}... → {new[:30]!r}...")
                fw_fixed += 1
                break
    print(f"  实际修复: {fw_fixed} / {len(FULL_WIDTH_FIXES)}")
    print()

    # 2. 数字/英文间空格
    print(f"[2] 中英/数字间加空格: {len(SPACE_FIXES)} 处")
    sp_fixed = 0
    for old, new in SPACE_FIXES:
        for vp in (ROOT / "frontend" / "src" / "views" / "admin").rglob("*.vue"):
            text = vp.read_text(encoding="utf-8")
            if old in text:
                text = text.replace(old, new, 1)
                vp.write_text(text, encoding="utf-8")
                rel = vp.relative_to(ROOT)
                print(f"  ✓ {rel}: {old[:40]!r} → {new[:40]!r}")
                sp_fixed += 1
                break
    print(f"  实际修复: {sp_fixed} / {len(SPACE_FIXES)}")
    print()

    # 3. 重新审查
    print("[3] 重新审计...")
    print()
    import subprocess
    r = subprocess.run(["python", str(Path(__file__).parent / "_hardcoded_zh_audit.py")],
                       cwd=ROOT / "spike-test", capture_output=True, text=True)
    r2 = subprocess.run(["python", str(Path(__file__).parent / "_chinese_quality_audit.py")],
                        cwd=ROOT / "spike-test", capture_output=True, text=True)
    # 输出合规率
    audit = json.loads((ROOT / "spike-test" / "chinese_quality_audit.json").read_text(encoding="utf-8"))
    rate = audit["ok_count"] / audit["total"] * 100
    print(f"  修正后: 规范 {audit['ok_count']} / 待修正 {audit['bad_count']} (合规率 {rate:.1f}%)")


if __name__ == "__main__":
    main()
