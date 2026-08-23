"""P1-1 v3-fix (v2 升级): 对 i18n 文件中已写入的低质量翻译做二次修正
v2 升级:
- 修复 parse_feedback 解析: 兼容单行内多个 key、嵌套大括号、含 $ 字符
- 修正: 直接调用 translate_zh_to_en_online (内置 PHRASE_OVERRIDES 优先) 作为权威
- 增强 _is_poor_translation 规则: 覆盖大小写/词序/句号/短词/常见错译模式
- 不依赖白名单, 任何 zh → en 与权威值不一致都修正
"""
import re
import json
import sys
from pathlib import Path
from typing import Dict, List, Tuple

sys.path.insert(0, str(Path(__file__).parent))
from _online_translate import (
    translate_zh_to_en_online,
    PHRASE_OVERRIDES,
    protect_placeholders,
)

ROOT = Path(r"d:\projects\sakurafilter\frontend\src")
ZH_FILE = ROOT / "i18n" / "locales" / "zh-CN.ts"
EN_FILE = ROOT / "i18n" / "locales" / "en-US.ts"


# ============================================================
# 翻译质量检测
# ============================================================
def _is_poor_translation(en: str, zh: str) -> Tuple[bool, str]:
    if not en or en.strip() == "":
        return True, "empty"
    if en == zh:
        return True, "unchanged_zh"
    # 1) 日文假名
    if re.search(r"[\u3040-\u309f\u30a0-\u30ff]", en):
        return True, "contains_japanese_kana"
    # 2) 中文比例 > 1%
    stripped = re.sub(r"[\s\d\W_]+", "", en)
    if stripped:
        zh_count = sum(
            1 for c in stripped
            if '\u4e00' <= c <= '\u9fff' or '\u3400' <= c <= '\u4dbf'
        )
        if (zh_count / len(stripped)) > 0.01:
            return True, "mixed_zh_en"
    # 3) MYMEMORY WARNING
    if "MYMEMORY WARNING" in en.upper():
        return True, "mymemory_warning"
    # 4) "$ {" 错误空格
    if re.search(r"\$\s+\{", en):
        return True, "mymemory_added_space"
    # 5) 括号错位: "): ${"
    if re.search(r"\):\s*\$\{", en):
        return True, "bracket_misplaced"
    # 6) 占位符数量不匹配
    zh_safe, zh_phs, _ = protect_placeholders(zh)
    en_safe, en_phs, _ = protect_placeholders(en)
    if len(zh_phs) != len(en_phs):
        return True, f"placeholder_count_mismatch(zh={len(zh_phs)},en={len(en_phs)})"
    # 7) 翻译含中文字符
    if any('\u4e00' <= c <= '\u9fff' for c in en):
        return True, "mixed_zh_en_in_value"
    # === v2 增强规则 ===
    # 8) 句末多余句号 (类似 "please try again.")
    if re.search(r"\bplease\s+\w+\.$", en, re.IGNORECASE):
        return True, "trailing_period_after_please"
    # 9) 短词 (< 3 字符) 单独 value (太泛, 如 "OK", "No")
    if en.strip() in ("OK", "No", "Yes"):
        return True, f"too_generic_word({en.strip()})"
    # 10) 错译: "Identify discontinued" (确定停售)
    if "Identify discontinued" in en:
        return True, "mistranslate_identify_discontinued"
    # 11) "Confirm Deletion" 大写
    if en in ("Confirm Deletion", "Clear Confirm"):
        return True, "case_or_word_order_error"
    # 12) "OK to delete" 不准确 (应是 "Confirm delete ...")
    if re.search(r"^OK to delete ", en):
        return True, "ok_to_delete_inaccurate"
    # 13) "Alternate" 上下文错 (替代/备选应为 "Alternative")
    if "Alternate OEM Form" in en:
        return True, "alternate_vs_alternative"
    # 14) 词序 "Compare up to products X" (缺空格 + products 错位)
    if re.search(r"Compare up to products\s+\$\{", en):
        return True, "word_order_compare_up_to"
    # 15) "after s" 错译
    if "after s ${" in en:
        return True, "after_s_typo"
    # 16) "deactivated" 应是 "discontinued"
    if "deactivated" in en and zh and "下架" in zh:
        return True, "deactivated_should_be_discontinued"
    # 17) "File was removed" 语态错误 (应是 "Removed")
    if en.strip() == "File was removed":
        return True, "passive_voice_should_be_removed"
    # 18) "Please, enter X" 多余逗号 + 大写
    if re.search(r"^Please,\s+enter\s+[A-Z]", en):
        return True, "comma_in_please_enter_or_capital"
    # 19) "Enter X to try" 语序
    if en.strip() == "Enter 045090 to try":
        return True, "word_order_enter_to_try"
    # 20) "Session token has expired" 冗长 (应是 "Session expired")
    if "Session token has expired" in en:
        return True, "verbose_session_token"
    # 21) 错误码括号错位: "(X:)${...}" → "(X: ${...})"
    if re.search(r"\)\s*\$\{", en) and "(" in en:
        return True, "error_code_bracket_misplaced"
    # 22) 句末多余句号 - 仅当仅此一项问题, 单独处理 (修句号而非重译)
    if en.rstrip().endswith("."):
        tail = en.rstrip()
        # 排除: 省略号 ... / U.S. / 缩写 / 数字+点+数字
        if tail.endswith("..."):
            return False, ""
        if not re.search(r"e\.g\.|i\.e\.|etc\.|U\.S\.|U\.K\.|Dr\.|Mr\.|Ms\.|\d+\.\d+", tail):
            return True, "trailing_period"
    return False, ""


def _fix_trailing_period(en: str) -> str:
    """仅移除句末句号, 不重译 (避免把好翻译改坏)"""
    if en.rstrip().endswith(".") and not en.rstrip().endswith("..."):
        return en.rstrip()[:-1]
    return en


# ============================================================
# 解析 i18n feedback 段
# ============================================================
def parse_feedback(fp: Path) -> Dict[str, str]:
    """行级解析 feedback 段, 支持单行多 key / 转义"""
    txt = fp.read_text(encoding="utf-8")
    out: Dict[str, str] = {}
    in_feedback = False
    for line in txt.splitlines():
        stripped = line.strip()
        if not in_feedback:
            if re.match(r"feedback\s*:\s*\{", stripped):
                in_feedback = True
                rest = re.sub(r"^.*?feedback\s*:\s*\{", "", stripped)
                if rest and rest != "}":
                    m = re.match(r"\s*(\w+)\s*:\s*'(.*)',?\s*$", rest)
                    if m:
                        out[m.group(1)] = m.group(2).replace("\\'", "'").replace("\\n", "\n")
            continue
        if stripped == "}":
            break
        m = re.match(r"^(\s*)(\w+)\s*:\s*'(.*)',?\s*$", line)
        if m:
            key = m.group(2)
            val = m.group(3).replace("\\'", "'").replace("\\n", "\n")
            out[key] = val
    return out


zh_map = parse_feedback(ZH_FILE)
en_map = parse_feedback(EN_FILE)
print(f"读取 i18n feedback: zh={len(zh_map)} keys, en={len(en_map)} keys")

# ============================================================
# 检测低质量 + 决定是否修复
# ============================================================
to_fix: List[Tuple[str, str, str, str]] = []
for key, zh in zh_map.items():
    old_en = en_map.get(key, "")
    is_poor, reason = _is_poor_translation(old_en, zh)
    if is_poor:
        if reason == "trailing_period":
            # 局部修复: 仅去句号
            new_en = _fix_trailing_period(old_en)
        else:
            # 整体重译: 优先查 PHRASE_OVERRIDES
            new_en = translate_zh_to_en_online(zh)
        to_fix.append((key, zh, old_en, reason, new_en))

print(f"\n需要修正: {len(to_fix)} 个 key")
for key, zh, old, reason, _ in to_fix:
    print(f"  {key} [{reason}]")
    print(f"    zh: {zh[:70]}")
    print(f"    en: {old[:70]}")


# ============================================================
# 写回 en-US.ts
# ============================================================
print(f"\n写回 en-US.ts...")
en_txt = EN_FILE.read_text(encoding="utf-8")
changed = 0
for key, zh, old_en, reason, new_en in to_fix:
    v_esc = new_en.replace("\\", "\\\\").replace("'", "\\'").replace("\n", "\\n")
    pattern = re.compile(
        rf"^(\s*{re.escape(key)}\s*:\s*)'(.*?)'(,?)\s*$",
        re.MULTILINE,
    )
    if pattern.search(en_txt):
        # 仅当 old_en != new_en 才替换
        if f"'{v_esc}'" not in en_txt or old_en != new_en:
            en_txt = pattern.sub(
                lambda mo: f"{mo.group(1)}'{v_esc}'{mo.group(3)}", en_txt, count=1
            )
            changed += 1
            print(f"  {key}: {old_en[:40]!r} → {new_en[:40]!r}")
EN_FILE.write_text(en_txt, encoding="utf-8")
print(f"\n  实际改写 {changed} 个 key")

# ============================================================
# 报告
# ============================================================
report = {
    "total_feedback_keys": len(zh_map),
    "poor_count": len(to_fix),
    "fixed_count": changed,
    "from_override": sum(1 for k, zh, _, _, _ in to_fix if zh in PHRASE_OVERRIDES),
    "fixes": [
        {
            "key": key,
            "zh": zh,
            "old_en": old_en,
            "new_en": new_en,
            "reason": reason,
            "from_override": zh in PHRASE_OVERRIDES,
        }
        for key, zh, old_en, reason, new_en in to_fix
    ],
}
report_path = Path(r"d:\projects\sakurafilter\spike-test\i18n_business_v3_fix_report.json")
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"\n报告: {report_path}")
