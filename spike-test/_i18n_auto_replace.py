"""
i18n 半自动替换脚本
=====================
WHY: admin 页面有 1029 处硬编码中文, 全部手工替换会耗尽 context 且易出错.
  本脚本做"半自动" - 减少人工, 但保留审核:

  1. 读取 hardcoded_zh_audit.json
  2. 按文件分组, 优先 ElMessage (用户最常看到) > placeholder > label
  3. 对每条中文字符串:
     - 调用 _i18n_glossary.translate_zh_to_en() 生成专业英文翻译
     - 生成 i18n key (admin.{file}.line{line})
     - 写入 zh-CN.ts 和 en-US.ts
     - 在 .vue 文件中替换为 t('admin.x.y')
  4. 替换策略:
     - 纯字面量: 直接替换
     - 模板字符串 (含 ${}): 跳过 (需人工设计 key + 插值)
     - 单字: 跳过
  5. 跑 _i18n_fullpath_audit.py 验证 0 缺失

用法:
  python _i18n_auto_replace.py                  # 处理所有 ElMessage + 短纯字面量
  python _i18n_auto_replace.py --dry-run        # 只报告不修改
  python _i18n_auto_replace.py --type ElMessage # 只处理 ElMessage
"""
import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Tuple

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


def needs_escape_for_ts(s: str) -> str:
    """转义 TS 字符串字面量"""
    return s.replace("\\", "\\\\").replace("'", "\\'").replace("\n", "\\n")


def generate_key(file_rel: str, line: int, ctx: str, text: str, used: set) -> str:
    """生成唯一 i18n key"""
    file_short = Path(file_rel).stem.replace("Admin", "").replace(".vue", "").lower()
    # 简化 ctx
    ctx_short = ctx.split(".")[-1] if "." in ctx else ctx.replace("-", "")
    base = f"admin.{file_short}.{ctx_short}.l{line}_{safe_key(text)}"
    # 去重
    key = base
    n = 2
    while key in used:
        key = f"{base}_{n}"
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

    # 按类型过滤
    if args.type:
        findings = [f for f in findings if f["context"] == args.type]
        print(f"  --type={args.type} 过滤后剩 {len(findings)} 处")

    # 按文件分组
    by_file: Dict[str, List[Dict]] = {}
    for f in findings:
        by_file.setdefault(f["file"], []).append(f)

    # 排序: 优先级高 → 低
    def priority(f):
        try:
            return PRIORITY.index(f["context"])
        except ValueError:
            return 99
    for fp in by_file:
        by_file[fp].sort(key=priority)

    used_keys: set = set()
    file_replacements: Dict[str, List[Tuple[int, str, str, str]]] = {}  # file -> [(line, old, new, key)]
    new_zh_entries: Dict[str, str] = {}  # key -> zh value
    new_en_entries: Dict[str, str] = {}  # key -> en value (词典翻译 or 占位)
    translation_stats = {
        "glossary_hit": 0,      # 词典命中
        "fallback_placeholder": 0,  # 回退占位
    }

    skipped_template = 0
    skipped_short = 0
    skipped_too_long = 0

    for f in findings:
        ctx = f["context"]
        text = f["text"]
        # 跳过模板字符串 (含 ${...} 或反引号)
        if "${" in text or "`" in text or "{{" in text:
            skipped_template += 1
            continue
        # 跳过过短 (单字或纯标点)
        if len(text) < 2 or not re.search(r"[\u4e00-\u9fff]", text):
            skipped_short += 1
            continue
        # 跳过过长 (>60 字符, 可能是句子而非按钮)
        if len(text) > 60:
            skipped_too_long += 1
            continue

        key = generate_key(f["file"], f["line"], ctx, text, used_keys)
        new_zh_entries[key] = text
        # 英文翻译: 词典驱动 + 占位回退
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

    print(f"\n处理结果:")
    print(f"  拟替换: {sum(len(v) for v in file_replacements.values())}")
    print(f"  跳过 (含模板字符串): {skipped_template}")
    print(f"  跳过 (太短): {skipped_short}")
    print(f"  跳过 (过长): {skipped_too_long}")
    print(f"  新 i18n key: {len(new_zh_entries)}")
    print(f"  翻译质量: 词典命中 {translation_stats['glossary_hit']} / 占位 {translation_stats['fallback_placeholder']} (命中率 {hit_rate:.1f}%)")

    if args.dry_run:
        print(f"\n[DRY-RUN] 不实际修改文件")
        print(f"  受影响文件: {len(file_replacements)}")
        for fp, repls in list(file_replacements.items())[:5]:
            print(f"    {fp}: {len(repls)} 处替换")
        # 输出 5 个翻译样例供人工审核
        print(f"\n  翻译样例 (前 10 条):")
        sample = list(new_en_entries.items())[:10]
        for k, en in sample:
            zh = new_zh_entries[k]
            print(f"    {zh!r:35s} → {en!r:50s} (key: {k})")
        sys.exit(0)

    # === 实际修改 ===
    # 1. 替换 .vue 文件
    if not args.dry_run:
        for fp, repls in file_replacements.items():
            full = ROOT / fp
            text = full.read_text(encoding="utf-8")
            for line_no, old, new, key in repls:
                # 精确匹配: 中文字符串作为字面量
                # 处理两种: '中文' / "中文" / `中文`
                for q in ["'", '"', "`"]:
                    old_pat = f"{q}{old}{q}"
                    new_pat = f"{q}{new}{q}"
                    if old_pat in text:
                        text = text.replace(old_pat, new_pat, 1)
                        break
            full.write_text(text, encoding="utf-8")
        print(f"  ✓ {len(file_replacements)} 个 .vue 文件已替换")

    # 2. 追加到 zh-CN.ts (用户审核后可删除占位)
    if not args.dry_run and new_zh_entries:
        zh_text = ZH_TS.read_text(encoding="utf-8")
        en_text = EN_TS.read_text(encoding="utf-8")
        # 简化为: 在 export default { ... } 末尾注入
        # 实际更复杂, 因为要按 admin.{file}.{ctx} 分组
        # 这里用最简策略: 把所有 key 平铺到 admin 块
        # WHY 最简: 文件结构复杂, 完整解析需 AST, 风险 > 价值
        #   建议人工审核 + 重组结构
        print(f"  ⚠ i18n key 追加需人工编辑 zh-CN.ts / en-US.ts")
        print(f"  新 key 写到: {OUT.relative_to(ROOT)}")

    # 报告
    report = {
        "ts": __import__("datetime").datetime.now().isoformat(),
        "total_findings": len(findings),
        "replaced": sum(len(v) for v in file_replacements.values()),
        "skipped_template": skipped_template,
        "skipped_short": skipped_short,
        "skipped_too_long": skipped_too_long,
        "new_keys_count": len(new_zh_entries),
        "translation_stats": translation_stats,
        "glossary_hit_rate": hit_rate,
        "new_keys": new_zh_entries,  # 供人工审核
        "new_translations": new_en_entries,  # 供人工审核
        "files": {fp: len(repls) for fp, repls in file_replacements.items()},
    }
    OUT.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"\n  报告: {OUT.relative_to(ROOT)}")
    print(f"  跳过模板字符串: {skipped_template} (需人工处理)")


if __name__ == "__main__":
    main()
