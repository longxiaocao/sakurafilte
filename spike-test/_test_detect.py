"""最终: 直接调 v3-fix 函数看"""
import sys
sys.stdout = open(r"d:\projects\sakurafilter\spike-test\_test_detect.log", "w", encoding="utf-8", buffering=1)

import re
sys.path.insert(0, r"d:\projects\sakurafilter\spike-test")
from _online_translate import protect_placeholders

# 完整 _is_poor_translation inline
def _is_poor_translation(en, zh):
    print(f"  输入 en: {en!r}", flush=True)
    print(f"  输入 zh: {zh!r}", flush=True)
    if not en or en.strip() == "":
        return True, "empty"
    if en == zh:
        return True, "unchanged_zh"
    if re.search(r"[\u3040-\u309f\u30a0-\u30ff]", en):
        return True, "contains_japanese_kana"
    stripped = re.sub(r"[\s\d\W_]+", "", en)
    if stripped:
        zh_count = sum(1 for c in stripped if '\u4e00' <= c <= '\u9fff' or '\u3400' <= c <= '\u4dbf')
        if (zh_count / len(stripped)) > 0.01:
            return True, "mixed_zh_en"
    if "MYMEMORY WARNING" in en.upper():
        return True, "mymemory_warning"
    if re.search(r"\$\s+\{", en):
        return True, "myemory_added_space"
    if re.search(r"\):\s*\$\{", en):
        return True, "bracket_misplaced"
    zh_safe, zh_phs, _ = protect_placeholders(zh)
    en_safe, en_phs, _ = protect_placeholders(en)
    if len(zh_phs) != len(en_phs):
        return True, f"placeholder_count_mismatch(zh={len(zh_phs)},en={len(en_phs)})"
    has_zh_in_en = any('\u4e00' <= c <= '\u9fff' for c in en)
    if has_zh_in_en:
        return True, "mixed_zh_en_in_value"
    return False, ""

# 测试
tests = [
    ("error_027", "Server is busy, please try again later (Error code:) ${status}", "服务器繁忙,请稍后重试 (错误码:${status})"),
    ("warn_025", "Up to 500 OEMs, current ${oems.length}", "最多 500 个 OEM, 当前 ${oems.length}"),
    ("warn_026", "Compare up to products ${MAX_COMPARE}", "最多对比 ${MAX_COMPARE} 个产品"),
    ("error_047", "Please enter your current password.", "请输入当前密码"),
    ("error_049", "Please, enter Username", "请输入用户名"),
]

for name, en, zh in tests:
    print(f"\n=== {name} ===", flush=True)
    is_poor, reason = _is_poor_translation(en, zh)
    print(f"  结果: is_poor={is_poor}, reason={reason}", flush=True)
