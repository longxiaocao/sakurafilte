"""
硬编码中文扫描器
=================
WHY: 之前 i18n 完整路径扫描发现 60 个 key 无人使用, 推断很多 admin 页面
  在 template / ElMessage / 提示文字处硬编码中文. 用户切到英文会看到一半中文一半英文.

扫描:
  1. admin 下所有 .vue 文件的 template / script
  2. 找硬编码中文字符串 (在 >, '', : 等"字面量位置"附近, 非代码注释/import)
  3. 输出报告: 文件:行, 中文字符串, 建议 i18n key 名

WHY 不自动替换: key 命名是设计决策, 需人工; 本脚本只做"发现+建议".
"""
import json
import re
import sys
from pathlib import Path
from typing import Dict, List

ROOT = Path(__file__).resolve().parent.parent
FRONT = ROOT / "frontend" / "src" / "views" / "admin"
OUT = ROOT / "spike-test" / "hardcoded_zh_audit.json"

# 中文字符范围
CN_CHAR = re.compile(r"[\u4e00-\u9fff]")

# 排除: 注释 (// 或 /* 或 <!--)
# 排除: import 路径
# 排除: URL 路径
# 保留: template 中的中文文案 / ElMessage 的中文 / placeholder / label

def find_hardcoded_zh(text: str, file_rel: str) -> List[Dict]:
    """找所有硬编码中文, 返回 [{line, content, context_type, suggested_key}]"""
    findings = []
    lines = text.splitlines()
    for i, line in enumerate(lines):
        # 跳过注释行
        stripped = line.strip()
        if stripped.startswith("//") or stripped.startswith("*") or stripped.startswith("/*"):
            continue
        # 跳过 import 行
        if stripped.startswith("import "):
            continue
        # 找中文字符串
        if not CN_CHAR.search(line):
            continue
        # 提取所有"中文片段" (连续的中文)
        matches = re.findall(r"[\u4e00-\u9fff][\u4e00-\u9fff\s,.，。；;：:!?？\-_/()（）\[\]【】0-9a-zA-Z]*", line)
        for m in matches:
            m = m.strip()
            if len(m) < 2:  # 至少 2 个中文字
                continue
            # 推测类型
            ctx = "string"
            if "ElMessage" in line:
                if "success" in line: ctx = "ElMessage.success"
                elif "warning" in line: ctx = "ElMessage.warning"
                elif "error" in line: ctx = "ElMessage.error"
                else: ctx = "ElMessage.info"
            elif "placeholder=" in line: ctx = "placeholder"
            elif "label=" in line: ctx = "label"
            elif "title=" in line: ctx = "title"
            elif "{{ " in line or "}}" in line: ctx = "template-text"
            elif "confirm(" in line: ctx = "ElMessageBox.confirm"
            elif "prompt(" in line: ctx = "ElMessageBox.prompt"
            elif 'confirmButtonText' in line or 'cancelButtonText' in line: ctx = "button-text"
            # 建议 key 名 (file + line + 简化)
            file_short = Path(file_rel).stem.replace("Admin", "").replace(".vue", "").lower()
            # 转拼音不可行, 用 hash-like 名称
            key_suggestion = f"admin.{file_short}.line{i+1}"
            findings.append({
                "file": file_rel,
                "line": i + 1,
                "context": ctx,
                "text": m[:80],
                "key_suggestion": key_suggestion,
            })
    return findings


def main():
    print("=" * 60)
    print("  硬编码中文扫描 (admin 页面)")
    print("=" * 60)
    findings = []
    file_count = 0
    for f in FRONT.rglob("*.vue"):
        file_count += 1
        try:
            text = f.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        rel = str(f.relative_to(ROOT))
        findings.extend(find_hardcoded_zh(text, rel))

    # 按文件聚合
    by_file: Dict[str, int] = {}
    for x in findings:
        by_file[x["file"]] = by_file.get(x["file"], 0) + 1

    # 上下文分布
    by_ctx: Dict[str, int] = {}
    for x in findings:
        by_ctx[x["context"]] = by_ctx.get(x["context"], 0) + 1

    report = {
        "stats": {
            "files_scanned": file_count,
            "total_hardcoded": len(findings),
            "files_with_zh": len(by_file),
            "by_context": by_ctx,
        },
        "by_file_top": sorted(
            [{"file": k, "count": v} for k, v in by_file.items()],
            key=lambda x: -x["count"]
        ),
        "findings": findings,  # 全量保存, 不截断 (was: findings[:200])
    }
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"  扫描文件: {file_count}")
    print(f"  硬编码中文: {len(findings)} 处")
    print(f"  涉及文件: {len(by_file)}")
    print()
    print(f"  按文件 (top 10):")
    for x in report["by_file_top"][:10]:
        print(f"    {x['count']:>3}  {x['file']}")
    print()
    print(f"  按上下文:")
    for ctx, n in sorted(by_ctx.items(), key=lambda x: -x[1]):
        print(f"    {n:>3}  {ctx}")
    print()
    print(f"  报告: {OUT.relative_to(ROOT)}")

    if findings:
        print()
        print(f"  前 15 条样例:")
        for f in findings[:15]:
            print(f"    {f['file']}:{f['line']}  [{f['context']}]  {f['text']}")

    # 不退出非 0, 仅报告
    sys.exit(0)


if __name__ == "__main__":
    main()
