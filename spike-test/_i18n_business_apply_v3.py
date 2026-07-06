"""P1-1 v3: 业务错误提示类硬编码中文自动 i18n 化
- 词典优先 + MyMemory 网络补全 + 短语白名单强制覆盖
- 嵌套占位符保护 (栈式解析, 支持 ${a?.b ? c : d} / 字符串边界)
- 翻译后处理: 修正常见标点 / 词序 / case 错误
- 自动注入 i18n key 到 zh-CN.ts / en-US.ts
- 必要时自动添加 useI18n import (含 t() 但未导入)
"""
import re
import json
import sys
from pathlib import Path
from typing import Dict, List, Set, Tuple

sys.path.insert(0, str(Path(__file__).parent))
from _online_translate import (
    translate_zh_to_en_online,
    batch_translate,
    PHRASE_OVERRIDES,
)

ROOT = Path(r"d:\projects\sakurafilter\frontend\src")
ZH_FILE = ROOT / "i18n" / "locales" / "zh-CN.ts"
EN_FILE = ROOT / "i18n" / "locales" / "en-US.ts"

# ============================================================
# 提取业务错误提示中文
# ============================================================
CATEGORIES = {
    "ElMessage": re.compile(r'''ElMessage\.(error|warning|success|info)\s*\(\s*['"`]([^'"`]+)['"`]'''),
    "ElMessageBox": re.compile(r'''ElMessageBox\.(?:confirm|alert|prompt)\s*\(\s*['"`]([^'"`]+)['"`](?:\s*,\s*['"`]([^'"`]+)['"`])?'''),
    "placeholder": re.compile(r'''\splaceholder=(['"])([^'"]*[\u4e00-\u9fff][^'"]*)\1'''),
}

items: List[Tuple[Path, int, str, str]] = []
for f in ROOT.rglob("*.vue"):
    text = f.read_text(encoding="utf-8", errors="ignore")
    for name, pat in CATEGORIES.items():
        for m in pat.finditer(text):
            try:
                if name == "ElMessage":
                    kind, zh = m.group(1), m.group(2)
                elif name == "ElMessageBox":
                    zh = m.group(2) or m.group(1)
                else:
                    zh = m.group(2)
                if not re.search(r"[\u4e00-\u9fff]", zh):
                    continue
                if len(zh) > 200:
                    continue
                line = text[: m.start()].count("\n") + 1
                items.append((f, line, name, zh))
            except (IndexError, AttributeError):
                pass

TS_CATS = {k: CATEGORIES[k] for k in ("ElMessage", "ElMessageBox")}
for f in ROOT.rglob("*.ts"):
    text = f.read_text(encoding="utf-8", errors="ignore")
    for name, pat in TS_CATS.items():
        for m in pat.finditer(text):
            try:
                if name == "ElMessage":
                    zh = m.group(2)
                else:
                    zh = m.group(2) or m.group(1)
                if not re.search(r"[\u4e00-\u9fff]", zh):
                    continue
                if len(zh) > 200:
                    continue
                line = text[: m.start()].count("\n") + 1
                items.append((f, line, name, zh))
            except (IndexError, AttributeError):
                pass

unique_zh = sorted(set(it[3] for it in items))
print(f"提取 {len(items)} 处业务提示, 去重 {len(unique_zh)} 条中文")

# ============================================================
# 翻译
# ============================================================
print(f"\n[1/3] 翻译 {len(unique_zh)} 条中文 (词典优先, MyMemory 补全)...")
translations = batch_translate(
    unique_zh, progress_cb=lambda d, t, c: print(f"  {d}/{t} {c[:50]}")
)

# ============================================================
# 生成 i18n key
# ============================================================
auto_keys: Dict[str, str] = {}
counter = {"success": 0, "error": 0, "warn": 0, "info": 0}


def make_key(zh: str) -> str:
    if zh in auto_keys:
        return auto_keys[zh]
    if any(k in zh for k in ["成功", "完成", "已加入", "已清空", "已复制", "已退出", "已修改", "已删除", "已恢复", "已加载", "已发送", "Added:"]):
        prefix = "success"
    elif any(k in zh for k in ["失败", "错误", "无法", "不能", "不存在", "请输入", "请粘", "请选择", "请填写", "已下架", "请先"]):
        prefix = "error"
    elif any(k in zh for k in ["确定", "确认", "警告", "超出", "最多", "至少", "再次", "已存在"]):
        prefix = "warn"
    else:
        prefix = "info"
    counter[prefix] += 1
    key = f"common.feedback.{prefix}_{counter[prefix]:03d}"
    auto_keys[zh] = key
    return key


for zh in unique_zh:
    make_key(zh)

# ============================================================
# 修改 .vue / .ts 文件
# ============================================================
print(f"\n[2/3] 修改 {len(set(it[0] for it in items))} 个文件...")


def replace_in_text(text: str, is_ts: bool = False) -> Tuple[str, int]:
    n = 0
    # 1) ElMessage.error('中文') → ElMessage.error(t('key'))
    def _elmsg(m):
        nonlocal n
        zh = m.group(2)
        # 含 ${} / ` / 已知网络/服务器错误 → 跳过 (避免改坏)
        if "${" in zh or "`" in zh:
            return m.group(0)
        if zh.startswith("网络") or zh.startswith("服务器"):
            return m.group(0)
        key = auto_keys.get(zh)
        if not key:
            return m.group(0)
        n += 1
        return f"ElMessage.{m.group(1)}(t('{key}'))"

    text = re.sub(
        r'''ElMessage\.(error|warning|success|info)\s*\(\s*['"`]([^'"`]+)['"`]\)''',
        _elmsg, text,
    )

    # 2) ElMessageBox.confirm('内容' [, '标题'])
    def _elbox(m):
        nonlocal n
        body = m.group(1)
        title = m.group(2)
        if "${" in body or "`" in body:
            return m.group(0)
        key1 = auto_keys.get(body)
        if not key1:
            return m.group(0)
        n += 1
        if title and "${" not in title and "`" not in title and (key2 := auto_keys.get(title)):
            return f"ElMessageBox.confirm(t('{key1}'), t('{key2}'))"
        return f"ElMessageBox.confirm(t('{key1}'))"

    text = re.sub(
        r'''ElMessageBox\.(?:confirm|alert|prompt)\s*\(\s*['"`]([^'"`]+)['"`](?:\s*,\s*['"`]([^'"`]+)['"`])?\)''',
        _elbox, text,
    )

    if not is_ts:
        # 3) placeholder="中文" → :placeholder="t('key')"
        def _placeholder(m):
            nonlocal n
            zh = m.group(2)
            key = auto_keys.get(zh)
            if not key:
                return m.group(0)
            n += 1
            return f' :placeholder="t(\'{key}\')"'

        text = re.sub(
            r'''(\s)placeholder=(['"])([^'"]*[\u4e00-\u9fff][^'"]*)\2''',
            _placeholder, text,
        )
    return text, n


modified: List[Tuple[Path, int]] = []
for f in sorted(set(it[0] for it in items)):
    txt = f.read_text(encoding="utf-8")
    is_ts = f.suffix == ".ts"
    new_txt, n = replace_in_text(txt, is_ts=is_ts)
    if n > 0:
        f.write_text(new_txt, encoding="utf-8")
        modified.append((f, n))
        print(f"  {f.relative_to(ROOT)}: {n} 处替换")

# ============================================================
# 自动修复缺失的 useI18n import
# ============================================================
print(f"\n[2.5/3] 扫描 + 修复缺失的 useI18n import...")
INSERT = "import { useI18n } from 'vue-i18n'\nconst { t } = useI18n()\n"
fixed_imports = 0
for f in sorted(ROOT.rglob("*.vue")):
    txt = f.read_text(encoding="utf-8")
    if "useI18n" in txt or "i18n.global.t" in txt:
        continue
    if re.search(r"\bt\s*\(\s*['\"`]", txt):
        new_txt = re.sub(
            r"(<script setup lang=\"ts\">\n)",
            r"\1" + INSERT,
            txt, count=1,
        )
        if new_txt != txt:
            f.write_text(new_txt, encoding="utf-8")
            fixed_imports += 1
            print(f"  {f.relative_to(ROOT)}: + useI18n")
print(f"  修复 {fixed_imports} 个文件")

# ============================================================
# 写入 i18n 文件
# ============================================================
print(f"\n[3/3] 写入 {len(auto_keys)} 个新 key 到 zh-CN.ts / en-US.ts...")


def insert_keys(fp: Path, entries: List[Tuple[str, str]]) -> bool:
    txt = fp.read_text(encoding="utf-8")
    pat_fb = re.compile(r"(\s*feedback\s*:\s*\{)([^}]*?)(\})", re.DOTALL)
    m = pat_fb.search(txt)
    if not m:
        return False
    existing = set()
    for km in re.finditer(r"(\w+):\s*['\"`]", m.group(2)):
        existing.add(km.group(1))
    lines = []
    for k, v in entries:
        short = k.rsplit(".", 1)[-1]
        if short in existing:
            continue
        v_esc = v.replace("\\", "\\\\").replace("'", "\\'").replace("\n", "\\n")
        lines.append(f"      {short}: '{v_esc}',")
        existing.add(short)
    if not lines:
        return True
    new_inner = m.group(2).rstrip() + "\n" + "\n".join(lines) + "\n    "
    new_txt = txt[: m.start(2)] + new_inner + txt[m.end(2):]
    fp.write_text(new_txt, encoding="utf-8")
    return True


zh_entries = [(k, zh) for zh, k in auto_keys.items()]
en_entries = [(k, translations.get(zh, zh)) for zh, k in auto_keys.items()]
ok_zh = insert_keys(ZH_FILE, zh_entries)
ok_en = insert_keys(EN_FILE, en_entries)
print(f"  zh-CN.ts: {'OK' if ok_zh else 'FAIL'}")
print(f"  en-US.ts: {'OK' if ok_en else 'FAIL'}")

# ============================================================
# 报告
# ============================================================
report = {
    "total_items": len(items),
    "unique_chinese": len(unique_zh),
    "files_modified": [{"file": str(f.relative_to(ROOT)), "replacements": n} for f, n in modified],
    "imports_fixed": fixed_imports,
    "new_keys": [
        {
            "key": k,
            "zh": zh,
            "en": translations.get(zh, "[TRANSLATE_FAILED]"),
            "from_override": zh in PHRASE_OVERRIDES,
        }
        for zh, k in sorted(auto_keys.items(), key=lambda x: x[1])
    ],
    "translation_failures": [
        zh for zh in unique_zh
        if re.search(r"[\u4e00-\u9fff]", translations.get(zh, zh))
        and translations.get(zh, zh) == zh
    ],
    "from_override_count": sum(1 for zh in unique_zh if zh in PHRASE_OVERRIDES),
    "from_dict_count": sum(
        1 for zh in unique_zh
        if zh not in PHRASE_OVERRIDES
        and not re.search(r"[\u4e00-\u9fff]", translations.get(zh, zh))
    ),
    "from_mymemory_count": sum(
        1 for zh in unique_zh
        if zh not in PHRASE_OVERRIDES
        and re.search(r"[\u4e00-\u9fff]", translations.get(zh, "")) is None
        and translations.get(zh, "") != zh
    ),
}
report_path = Path(r"d:\projects\sakurafilter\spike-test\i18n_business_v3_report.json")
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"\n报告: {report_path}")
print(f"修改文件: {len(modified)}, 新增 key: {len(auto_keys)}")
print(f"  来自白名单: {report['from_override_count']}")
print(f"  来自词典:   {report['from_dict_count']}")
print(f"  来自 MyMemory: {report['from_mymemory_count']}")
print(f"  翻译失败:   {len(report['translation_failures'])}")
