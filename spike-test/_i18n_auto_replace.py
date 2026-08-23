"""
i18n 半自动替换脚本 v2
=======================
WHY v2:
  - v1 在第 269 行直接跳过所有含 [a-zA-Z0-9] 的字符串 (如 "OEM 编号", "产品名 1"),
    导致 160 处关键 label/placeholder 未被替换, 仅 6 处生效.
  - 替换语法有 bug: `title=t('key')` 应为 `:title="t('key')"`, 且
    `t` 函数需从 useI18n 显式解构, 否则 template 中无效.
  - 自动注入 useI18n 到缺失的 .vue 文件.

改进:
  1. 放宽 mixed 跳过规则: 仅跳过含 `{` `}` `$` `\` `{{` 的非标点干扰项
  2. 修复语法: 替换时自动判别属性绑定/插值, 生成正确语法
  3. 扫描所有 .vue 缺失 useI18n 的 script, 自动注入 `const { t } = useI18n()`
  4. 注入到 zh-CN.ts / en-US.ts

用法:
  python _i18n_auto_replace.py                  # 处理所有 ElMessage + 短纯字面量
  python _i18n_auto_replace.py --dry-run        # 只报告不修改
  python _i18n_auto_replace.py --type ElMessage # 只处理某类型
"""
import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Tuple, Set

# WHY: 引入过滤器专业术语词典, 让英文翻译自动化 + 行业化
sys.path.insert(0, str(Path(__file__).resolve().parent))
try:
    from _i18n_glossary import translate_zh_to_en
    HAS_GLOSSARY = True
except ImportError:
    HAS_GLOSSARY = False
    print("[WARN] 未找到 _i18n_glossary.py, 英文翻译将回退到 [EN] 占位符")

ROOT = Path(__file__).resolve().parent.parent
FRONT = ROOT / "frontend" / "src"
ZH_TS = FRONT / "i18n" / "locales" / "zh-CN.ts"
EN_TS = FRONT / "i18n" / "locales" / "en-US.ts"
AUDIT = ROOT / "spike-test" / "hardcoded_zh_audit.json"
OUT = ROOT / "spike-test" / "i18n_replace_report.json"

# 类型优先级 (用户最常看到 → 最低)
PRIORITY = ["ElMessage.success", "ElMessage.warning", "ElMessage.error",
            "placeholder", "label", "title", "button-text", "template-text"]


def safe_key(s: str) -> str:
    """生成 i18n key, 去标点"""
    return re.sub(r"[^a-z0-9]+", "_", s.lower()).strip("_")[:40]


def _escape_for_ts(s: str) -> str:
    """转义字符串字面量, 嵌入 TS 单引号字符串"""
    return s.replace("\\", "\\\\").replace("'", "\\'")


def generate_key(file_rel: str, line: int, ctx: str, text: str, used: set, en_text: str = "") -> str:
    """生成唯一 i18n key (v3 语义化)
    WHY v3:
      - 旧版 l{line}_{hash} 难以阅读 (e.g. l53_ 含义不明)
      - 新版用翻译后的英文短语作为 key 后缀, 更直观
      - 例: admin.compareview.headers.basic (而非 admin.compareview.string.l53_)
    """
    file_short = Path(file_rel).stem.replace("Admin", "").replace(".vue", "").lower()
    ctx_short = ctx.split(".")[-1] if "." in ctx else ctx.replace("-", "")

    # 用英文翻译作为 key 后缀 (更语义化)
    # 翻译失败时回退到 safe_key(原文)
    suffix_src = en_text if en_text and not en_text.startswith("[EN]") else text
    suffix = safe_key(suffix_src)
    if not suffix:
        suffix = f"l{line}"

    # 优先: admin.{file}.{ctx}.{suffix} (无 l{line} 前缀)
    base = f"admin.{file_short}.{ctx_short}.{suffix}"
    key = base
    n = 2
    while key in used:
        # 重名时加 l{line} 区分
        key = f"{base}_l{line}" if n == 2 else f"{base}_l{line}_{n}"
        n += 1
    used.add(key)
    return key


def translate_with_fallback(zh: str) -> str:
    """
    调用词典翻译, 失败则回退到 [EN] 占位.
    词典模块来源: _i18n_glossary (Donaldson/Fleetguard/Sakura 行业术语)
    """
    if HAS_GLOSSARY:
        en = translate_zh_to_en(zh)
        if en:
            return en
    return f"[EN] {zh[:30]}"


def has_template_or_special(text: str) -> bool:
    """
    检查是否含不可处理的内容 (模板字符串/插值)
    WHY v2: 原版同时跳过 ASCII 字母/数字, 导致 160+ 处无法替换.
             现在只跳过含 `${...}` `{{...}}` 反引号 换行 的内容.
    """
    if "${" in text or "`" in text or "{{" in text:
        return True
    if "\n" in text:
        return True
    return False


def inject_to_locale(ts_path: Path, entries: Dict[str, str], locale_name: str) -> None:
    """
    把 key→value 注入到 locale TS 文件的 export default { ... } 中.
    分组: admin.{file_short}.{ctx_short} = value
    """
    text = ts_path.read_text(encoding="utf-8")

    # 按 key 拆分: admin.compareview.string.l1_xxx -> { compareview: { string: { l1_xxx: ... } } }
    grouped: Dict[str, Dict[str, Dict[str, str]]] = {}
    for key, value in entries.items():
        parts = key.split(".")
        if len(parts) < 3 or parts[0] != "admin":
            continue
        file_short = parts[1]
        ctx_short = parts[2]
        sub = parts[3] if len(parts) > 3 else "value"
        grouped.setdefault(file_short, {}).setdefault(ctx_short, {})[sub] = value

    # 生成 admin 块 TS 字符串 (4 层嵌套)
    def render_grouped() -> str:
        lines = ["  admin: {"]
        for file_short, ctxs in sorted(grouped.items()):
            lines.append(f"    {file_short}: {{")
            for ctx, subs in sorted(ctxs.items()):
                lines.append(f"      {ctx}: {{")
                for sub, val in sorted(subs.items()):
                    safe_val = _escape_for_ts(val)
                    lines.append(f"        {sub}: '{safe_val}',")
                lines.append(f"      }},")
            lines.append(f"    }},")
        lines.append("  },")
        return "\n".join(lines)

    admin_block = render_grouped()

    # 找 export default { 的开始位置
    ed_match = re.search(r"^export default \{\s*\n", text, re.MULTILINE)
    if not ed_match:
        print(f"  [WARN] {locale_name} 找不到 export default {{ }}, 跳过注入")
        return

    ed_start = ed_match.end()  # 在 { 之后
    depth = 1
    i = ed_start
    while i < len(text) and depth > 0:
        c = text[i]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    if depth != 0:
        print(f"  [WARN] {locale_name} export default 块未闭合")
        return
    ed_end = i  # 在 } 位置

    body = text[ed_start:ed_end]

    admin_match = re.search(r"^(\s*)admin:\s*\{", body, re.MULTILINE)
    if admin_match:
        admin_inner_start = admin_match.end()
        depth = 1
        j = admin_inner_start
        while j < len(body) and depth > 0:
            c = body[j]
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    break
            j += 1
        if depth != 0:
            print(f"  [WARN] {locale_name} admin 块未闭合")
            return
        new_body = body[:admin_match.start()] + admin_block.rstrip(",") + "\n" + body[j+1:]
    else:
        new_body = admin_block + "\n" + body

    new_text = text[:ed_start] + new_body + text[ed_end:]
    ts_path.write_text(new_text, encoding="utf-8")


def ensure_usei18n(vue_path: Path) -> bool:
    """
    确保 .vue 文件 script setup 中有 `import { useI18n }` + `const { t } = useI18n()`
    WHY v2: v1 替换时假设文件已有 useI18n, 实际大部分 admin 视图未导入, 导致 t() 未定义.

    Returns: True if injection happened, False if already had it
    """
    text = vue_path.read_text(encoding="utf-8")

    if "useI18n" in text and "const { t }" in text:
        return False

    # 1. 注入 import
    if "useI18n" not in text:
        # 在 'vue' 或 'vue-router' 导入后插入 vue-i18n
        if "from 'vue'" in text:
            text = text.replace(
                "from 'vue'",
                "from 'vue'\nimport { useI18n } from 'vue-i18n'",
                1
            )
        elif "from \"vue\"" in text:
            text = text.replace(
                "from \"vue\"",
                "from \"vue\"\nimport { useI18n } from 'vue-i18n'",
                1
            )
        else:
            # 兜底: 第一个 import 行后插入
            text = re.sub(
                r"(import .+\n)",
                r"\1import { useI18n } from 'vue-i18n'\n",
                text,
                count=1
            )

    # 2. 注入 const { t } = useI18n()
    if "const { t }" not in text:
        # 找到最后一个 import 行, 在它之后插入 const { t } = useI18n()
        # 关键: 必须在所有 import 之后, 否则 useI18n 未定义
        import_lines = list(re.finditer(r"^import .+?$", text, re.MULTILINE))
        if import_lines:
            last_import = import_lines[-1]
            insert_pos = last_import.end() + 1  # +1 是换行
            text = text[:insert_pos] + "\nconst { t } = useI18n()\n" + text[insert_pos:]
        else:
            # 兜底: <script setup> 后
            m = re.search(r"(<script setup[^>]*>\n)", text)
            if m:
                insert_pos = m.end()
                text = text[:insert_pos] + "\nconst { t } = useI18n()\n" + text[insert_pos:]

    vue_path.write_text(text, encoding="utf-8")
    return True


def replace_in_vue(text: str, old: str, key: str) -> Tuple[str, int]:
    """
    智能替换 .vue 中的中文字符串.

    关键修复 v2:
      - `title="中文"` → `:title="t('key')"` (属性绑定)
      - `title=t('key')` → `:title="t('key')"` (语法补全)
      - `:title="中文"` → `:title="t('key')"` (插值)
      - `'中文'` → `t('key')` (普通字符串)
      - `"中文"` → `t('key')` (双引号)
      - `name: '中文'` → `name: t('key')` (object 属性)

    Returns: (new_text, replaced_count)
    """
    replaced = 0

    # 优先匹配 attribute 形式
    for attr in ["title", "label", "placeholder", "value", "name"]:
        # :title="中文"  →  :title="t('key')"
        pat1 = f':{attr}="{old}"'
        if pat1 in text:
            text = text.replace(pat1, f':{attr}="t(\'{key}\')"', 1)
            replaced += 1
            continue
        # title="中文"  →  :title="t('key')"  (缺冒号)
        pat2 = f'{attr}="{old}"'
        if pat2 in text:
            text = text.replace(pat2, f':{attr}="t(\'{key}\')"', 1)
            replaced += 1
            continue
        # title='中文'  →  :title="t('key')"  (单引号)
        pat3 = f"{attr}='{old}'"
        if pat3 in text:
            text = text.replace(pat3, f":{attr}=\"t('{key}')\"", 1)
            replaced += 1
            continue
        # name: '中文'  →  name: t('key')  (object 属性形式)
        pat_obj = f"{attr}: '{old}'"
        if pat_obj in text:
            text = text.replace(pat_obj, f"{attr}: t('{key}')", 1)
            replaced += 1
            continue
        # name: "中文"  →  name: t('key')
        pat_obj2 = f'{attr}: "{old}"'
        if pat_obj2 in text:
            text = text.replace(pat_obj2, f'{attr}: t("{key}")', 1)
            replaced += 1
            continue
        # {{ '中文' }}  (template literal)
        pat4 = f"{{{{ '{old}' }}}}"
        if pat4 in text:
            text = text.replace(pat4, f"{{{{ t('{key}') }}}}", 1)
            replaced += 1
            continue
        pat5 = f'{{{{ "{old}" }}}}'
        if pat5 in text:
            text = text.replace(pat5, f'{{{{ t("{key}") }}}}', 1)
            replaced += 1
            continue

    # '中文' → t('key')  (普通字符串)
    if replaced == 0:
        for q in ["'", '"', "`"]:
            old_pat = f"{q}{old}{q}"
            if old_pat in text:
                text = text.replace(old_pat, f"t('{key}')", 1)
                replaced += 1
                break

    # 兜底: 直接替换
    if replaced == 0 and old in text:
        text = text.replace(old, f"t('{key}')", 1)
        replaced += 1

    return text, replaced


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--type", help="只处理某类型, 如 ElMessage.success")
    args = parser.parse_args()

    if not AUDIT.exists():
        print(f"[ERR] 找不到 {AUDIT}, 请先跑 _hardcoded_zh_audit.py")
        sys.exit(1)

    audit = json.loads(AUDIT.read_text(encoding="utf-8"))
    findings = audit["findings"]
    print(f"扫描到 {len(findings)} 处硬编码中文")

    if args.type:
        findings = [f for f in findings if f["context"] == args.type]
        print(f"  --type={args.type} 过滤后剩 {len(findings)} 处")

    # 按文件分组
    by_file: Dict[str, List[Dict]] = {}
    for f in findings:
        by_file.setdefault(f["file"], []).append(f)

    def priority(f):
        try:
            return PRIORITY.index(f["context"])
        except ValueError:
            return 99
    for fp in by_file:
        by_file[fp].sort(key=priority)

    used_keys: set = set()
    file_replacements: Dict[str, List[Tuple[int, str, str, str]]] = {}
    new_zh_entries: Dict[str, str] = {}
    new_en_entries: Dict[str, str] = {}
    translation_stats = {
        "glossary_hit": 0,
        "fallback_placeholder": 0,
    }

    skipped_template = 0
    skipped_short = 0
    skipped_too_long = 0

    for f in findings:
        ctx = f["context"]
        text = f["text"]
        if has_template_or_special(text):
            skipped_template += 1
            continue
        if len(text) < 2 or not re.search(r"[\u4e00-\u9fff]", text):
            skipped_short += 1
            continue
        if len(text) > 80:
            skipped_too_long += 1
            continue

        en = translate_with_fallback(text)
        # v3: 把翻译结果传给 generate_key, 让 key 反映英文语义
        key = generate_key(f["file"], f["line"], ctx, text, used_keys, en_text=en)
        new_zh_entries[key] = text
        en = translate_with_fallback(text)
        if en.startswith("[EN] "):
            translation_stats["fallback_placeholder"] += 1
        else:
            translation_stats["glossary_hit"] += 1
        new_en_entries[key] = en

        file_replacements.setdefault(f["file"], []).append(
            (f["line"], text, f"t('{key}')", key)
        )

    total = translation_stats["glossary_hit"] + translation_stats["fallback_placeholder"]
    hit_rate = (translation_stats["glossary_hit"] / total * 100) if total else 0

    print(f"\n处理结果 (v2 - 含中英/数字混合):")
    print(f"  拟替换: {sum(len(v) for v in file_replacements.values())}")
    print(f"  跳过 (含模板字符串/换行): {skipped_template}")
    print(f"  跳过 (太短/无中文): {skipped_short}")
    print(f"  跳过 (过长): {skipped_too_long}")
    print(f"  新 i18n key: {len(new_zh_entries)}")
    print(f"  翻译质量: 词典命中 {translation_stats['glossary_hit']} / 占位 {translation_stats['fallback_placeholder']} (命中率 {hit_rate:.1f}%)")

    if args.dry_run:
        print(f"\n[DRY-RUN] 不实际修改文件")
        print(f"  受影响文件: {len(file_replacements)}")
        for fp, repls in list(file_replacements.items())[:5]:
            print(f"    {fp}: {len(repls)} 处替换")
        print(f"\n  翻译样例 (前 10 条):")
        sample = list(new_en_entries.items())[:10]
        for k, en in sample:
            zh = new_zh_entries[k]
            print(f"    {zh!r:35s} → {en!r:50s} (key: {k})")
        sys.exit(0)

    if not args.dry_run:
        # 1. 先给所有受影响的 .vue 注入 useI18n
        injected_files: List[str] = []
        for fp in file_replacements.keys():
            full = ROOT / fp
            if not full.exists():
                continue
            if ensure_usei18n(full):
                injected_files.append(fp)
        if injected_files:
            print(f"  ✓ {len(injected_files)} 个 .vue 注入 useI18n: {injected_files}")

        # 2. 实际替换 .vue 文件
        for fp, repls in file_replacements.items():
            full = ROOT / fp
            if not full.exists():
                continue
            text = full.read_text(encoding="utf-8")
            for line_no, old, new, key in repls:
                text, n = replace_in_vue(text, old, key)
                if n == 0:
                    print(f"  [WARN] {fp}:{line_no} 替换失败: {old!r}")
            full.write_text(text, encoding="utf-8")
        print(f"  ✓ {len(file_replacements)} 个 .vue 文件已替换")

    if not args.dry_run and new_zh_entries:
        inject_to_locale(ZH_TS, new_zh_entries, "zh-CN")
        inject_to_locale(EN_TS, new_en_entries, "en-US")
        print(f"  ✓ {len(new_zh_entries)} 个 key 已注入 zh-CN.ts / en-US.ts")

    report = {
        "ts": __import__("datetime").datetime.now().isoformat(),
        "version": "v2",
        "total_findings": len(findings),
        "replaced": sum(len(v) for v in file_replacements.values()),
        "skipped_template": skipped_template,
        "skipped_short": skipped_short,
        "skipped_too_long": skipped_too_long,
        "new_keys_count": len(new_zh_entries),
        "translation_stats": translation_stats,
        "glossary_hit_rate": hit_rate,
        "injected_usei18n": injected_files if not args.dry_run else [],
        "new_keys": new_zh_entries,
        "new_translations": new_en_entries,
        "files": {fp: len(repls) for fp, repls in file_replacements.items()},
    }
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\n  报告: {OUT.relative_to(ROOT)}")
    print(f"  跳过模板字符串/换行: {skipped_template} (需人工处理)")


if __name__ == "__main__":
    main()
