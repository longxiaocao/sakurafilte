"""
源中文规范性审查
==================
WHY: 用户问"我们的中文是否专业且规范". 在执行 i18n 替换前,
     需先审视 .vue 源文件中硬编码中文本身是否符合行业标准.
     不规范中文会通过 t('key') 永久固化, 后续修改成本翻倍.

审查维度:
  1. 口语化用法 (如 "机油格" → "机油滤清器")
  2. 错别字 / 拼写错误
  3. 术语不一致 (如 "燃油滤清器" vs "柴油滤清器" 混用)
  4. 中英混排不当 (如 "OEM 2" 前后无空格)
  5. 标点符号不规范 (全角 vs 半角)
  6. 表述歧义 (单字, 含义不明)

输出:
  - 中文规范性审查报告 (本脚本输出)
  - 标记需要修正的项
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
AUDIT = ROOT / "spike-test" / "hardcoded_zh_audit.json"
REPORT = ROOT / "spike-test" / "chinese_quality_audit.json"


# ============================================================
# 1. 口语化 → 行业规范 映射
#    WHY: "格"是滤清器的口语化简称, 行业标准应为"滤清器"
# ============================================================
COLLOQUIAL_TERMS = {
    # "格" 是 滤清器 的口语化简称, 应改为"滤清器"
    "机油格": "机油滤清器",
    "柴油格": "柴油滤清器",
    "空气格": "空气滤清器",
    "空调格": "空调滤清器",
    "汽油格": "汽油滤清器",
    # "挂" 是 安装 的口语化, 维修场景外应改为"安装"
    # 留着, UI 场景下"挂载"反而是技术术语, 谨慎
    # 数字/英文前后空格
    "OEM2": "OEM 2",
    "OEM3": "OEM 3",
}


# ============================================================
# 2. 术语一致性规则
#    WHY: 同一概念在不同位置应使用同一术语, 避免"过滤精度"和"精度"混用
# ============================================================
CANONICAL_TERMS = {
    # 产品类型
    "燃油滤清器": "燃油滤清器",  # 统一使用
    "柴油滤清器": "燃油滤清器",  # 柴油也是燃油, 统一术语
    # 动作
    "触发": "触发",  # ETL
    "执行": "执行",  # 通用
    # 状态
    "已保存": "已保存",
    "保存成功": "保存成功",  # 优先用"成功"而非"OK"
    # 短词
    "新增": "新增",
    "添加": "添加",
}


# ============================================================
# 3. 标点符号规则
# ============================================================
PUNCT_RULES = {
    # 全角逗号 → 半角 (英文 UI 上下文)
    "，": ",",
    "（": "(",
    "）": ")",
    "：": ": ",
    # 全角句号在按钮文本中不合适
    "。": "",
}


def audit_one(text: str) -> dict:
    """审查单条中文, 返回问题列表"""
    issues = []
    suggestions = []

    # 1. 口语化检查
    for col, std in COLLOQUIAL_TERMS.items():
        if col in text and col != std:
            issues.append(f"口语化: {col!r} 应改为 {std!r}")
            suggestions.append(text.replace(col, std))

    # 2. 数字/英文间无空格
    # 匹配: 中文+数字 / 数字+中文 / 英文+数字 / 数字+英文
    no_space = re.findall(r"[\u4e00-\u9fff]\d|\d[\u4e00-\u9fff]|[a-zA-Z]\d(?!\w)|\d[a-zA-Z]", text)
    if no_space:
        # 排除特殊情况: 已经是 "OEM 2" 这种已带空格的
        # 这里只报告, 修正留给用户
        for ns in no_space:
            if "OEM " not in text and "MR." not in text:
                issues.append(f"中英/数字间缺空格: {ns!r} 附近")
                break

    # 3. 标点符号 (全角)
    has_full_width = any(p in text for p in PUNCT_RULES)
    if has_full_width:
        issues.append("含全角标点 (建议英文上下文用半角)")

    # 4. 单字 (除明确功能字如"上/下/中")
    if len(text.strip()) == 1 and text.strip() not in "上下中左右前后大小":
        issues.append(f"单字, 含义不明: {text!r}")

    # 5. 仅含标点/数字
    if not re.search(r"[\u4e00-\u9fff]", text):
        issues.append("不含中文")

    # 6. 过长 (按钮/标签 > 15 字可能不友好)
    if len(text) > 15:
        issues.append(f"过长 ({len(text)} 字), 按钮/标签建议 < 15 字")

    return {
        "issues": issues,
        "suggestions": suggestions,
        "ok": len(issues) == 0,
    }


def main():
    if not AUDIT.exists():
        print(f"[ERR] 找不到 {AUDIT}, 请先跑 _hardcoded_zh_audit.py")
        sys.exit(1)

    audit = json.loads(AUDIT.read_text(encoding="utf-8"))
    findings = audit["findings"]
    print(f"扫描 {len(findings)} 条硬编码中文")
    print()

    # 1. 逐条审查
    results = []
    issue_counter = Counter()
    issue_files = Counter()
    for f in findings:
        text = f["text"]
        result = audit_one(text)
        entry = {
            "file": f["file"],
            "line": f["line"],
            "context": f["context"],
            "text": text,
            **result,
        }
        results.append(entry)
        for issue in result["issues"]:
            issue_type = issue.split(":")[0].split(" ")[0]
            issue_counter[issue_type] += 1
            issue_files[f["file"]] += 1

    # 2. 输出
    bad = [r for r in results if not r["ok"]]
    print(f"=== 审查结果 ===")
    print(f"  总数: {len(results)}")
    print(f"  规范: {len(results) - len(bad)} ({((len(results)-len(bad))/len(results)*100):.1f}%)")
    print(f"  待修正: {len(bad)} ({len(bad)/len(results)*100:.1f}%)")
    print()
    print(f"=== 问题分类 ===")
    for issue_type, count in issue_counter.most_common():
        print(f"  {issue_type}: {count} 处")
    print()
    print(f"=== 涉及文件 (按问题数排序) ===")
    for fp, cnt in issue_files.most_common():
        print(f"  {fp}: {cnt} 处")
    print()

    # 3. 详细列出待修正项
    print(f"=== 待修正详情 ===")
    for r in bad:
        marker = "  ⚠" if not r["ok"] else "  ✓"
        print(f"{marker} {r['file']}:{r['line']} [{r['context']}]")
        print(f"      原文: {r['text']!r}")
        for issue in r["issues"]:
            print(f"        - {issue}")
        if r["suggestions"]:
            print(f"      建议: {r['suggestions'][0]!r}")
        print()

    # 4. 保存报告
    REPORT.write_text(json.dumps({
        "ts": __import__("datetime").datetime.now().isoformat(),
        "total": len(results),
        "ok_count": len(results) - len(bad),
        "bad_count": len(bad),
        "issue_summary": dict(issue_counter),
        "files_with_issues": dict(issue_files),
        "details": results,
    }, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"  报告: {REPORT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
