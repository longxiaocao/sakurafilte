"""P1-1 v2: 业务错误提示类硬编码中文自动 i18n 化
- 在线翻译 (MyMemory) + 词典 fallback + PUA 占位符保护
- 不再用 Python 字符串拼接的弱翻译
- 占位符 ${} / {{}} 通过 Unicode PUA 字符 100% 安全保留
"""
import re
import json
import sys
from pathlib import Path

# 引入在线翻译模块
sys.path.insert(0, str(Path(__file__).parent))
from _online_translate import translate_zh_to_en_online, batch_translate

ROOT = Path(r"d:\projects\sakurafilter\frontend\src")
ZH_FILE = ROOT / "i18n" / "locales" / "zh-CN.ts"
EN_FILE = ROOT / "i18n" / "locales" / "en-US.ts"

# ============================================================
# 提取业务错误提示中文 (从所有 .vue 文件)
# ============================================================
CATEGORIES = {
    "ElMessage": re.compile(r"ElMessage\.(error|warning|success|info)\s*\(\s*['\"`]([^'\"`]+)['\"`]"),
    "ElMessageBox": re.compile(r"ElMessageBox\.(?:confirm|alert|prompt)\s*\(\s*['\"`]([^'\"`]+)['\"`](?:\s*,\s*['\"`]([^'\"`]+)['\"`])?"),
    "placeholder": re.compile(r'\splaceholder=(["\'])([^"\']*[\u4e00-\u9fff][^"\']*)\1'),
}

# 收集 (file, line, category, text)
items = []
# WHY: .vue 模板 + .ts 业务代码都可能含 ElMessage / ElMessageBox / placeholder
#  - http.ts 集中处理所有 HTTP 错误, 含 8+ 处网络错误提示
#  - router/index.ts 路由守卫有 '请先登录' / '仅管理员可访问'
for f in ROOT.rglob("*.vue"):
    text = f.read_text(encoding="utf-8", errors="ignore")
    for name, pat in CATEGORIES.items():
        for m in pat.finditer(text):
            try:
                if name == "ElMessage":
                    kind, zh = m.group(1), m.group(2)
                elif name == "ElMessageBox":
                    zh = m.group(2) or m.group(1)
                else:  # placeholder (仅 vue 有效)
                    zh = m.group(2)
                if not re.search(r'[\u4e00-\u9fff]', zh):
                    continue
                if len(zh) > 200:
                    continue
                line = text[:m.start()].count("\n") + 1
                items.append((f, line, name, zh))
            except (IndexError, AttributeError):
                pass

# .ts 文件: 只搜 ElMessage / ElMessageBox (无 placeholder)
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
                if not re.search(r'[\u4e00-\u9fff]', zh):
                    continue
                if len(zh) > 200:
                    continue
                line = text[:m.start()].count("\n") + 1
                items.append((f, line, name, zh))
            except (IndexError, AttributeError):
                pass

# 去重 + 按中文聚合
unique_zh = sorted(set(it[3] for it in items))
print(f"提取 {len(items)} 处业务提示, 去重 {len(unique_zh)} 条中文")

# ============================================================
# 翻译: MyMemory 在线 + 词典 fallback
# ============================================================
print(f"\n[1/3] 在线翻译 {len(unique_zh)} 条中文 (预计 {len(unique_zh) * 0.15:.0f}s)...")
translations = batch_translate(unique_zh, progress_cb=lambda d, t, c: print(f"  {d}/{t} {c[:40]}"))

# ============================================================
# 生成 i18n key: 简短语义化命名
# ============================================================
auto_keys = {}
counter = 0


def make_key(zh: str) -> str:
    global counter
    if zh in auto_keys:
        return auto_keys[zh]
    counter += 1
    # 按内容分类命名
    if any(k in zh for k in ["成功", "完成", "已加入", "已清空", "已复制", "已退出", "已修改", "已删除", "已恢复", "已加载"]):
        prefix = "success"
    elif any(k in zh for k in ["失败", "错误", "无法", "不能", "不存在", "请输入", "请粘", "请选择", "请填写"]):
        prefix = "error"
    elif any(k in zh for k in ["确定", "确认", "警告", "超出", "最多", "至少", "再次", "已存在", "已下架"]):
        prefix = "warn"
    else:
        prefix = "info"
    key = f"common.feedback.{prefix}_{counter:03d}"
    auto_keys[zh] = key
    return key


for zh in unique_zh:
    k = make_key(zh)
    auto_keys[zh] = k  # 必须显式存储!

# ============================================================
# 修改 .vue 文件
# ============================================================
print(f"\n[2/3] 修改 {len(set(it[0] for it in items))} 个 .vue 文件...")


def replace_in_text(text: str, is_ts: bool = False) -> tuple[str, int]:
    """返回 (新文本, 替换数)
    is_ts: 是否处理 .ts 文件 (无 placeholder 模式)
    """
    n = 0
    # 1) ElMessage.error('中文') → ElMessage.error(t('key'))
    #    必须匹配完整 `ElMessage.error('xxx')` 包括闭合 `)`, 否则会多一个括号
    def _elmsg(m):
        nonlocal n
        zh = m.group(2)
        if "${" in zh or "`" in zh or zh.startswith("网络") or zh.startswith("服务器"):
            return m.group(0)
        key = auto_keys.get(zh)
        if not key:
            return m.group(0)
        n += 1
        return f"ElMessage.{m.group(1)}(t('{key}'))"

    text = re.sub(r"ElMessage\.(error|warning|success|info)\(['\"`]([^'\"`]+)['\"`]\)", _elmsg, text)

    # ElMessageBox.confirm('内容' [, '标题']) → ElMessageBox.confirm(t('k1') [, t('k2')])
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
        r"ElMessageBox\.(?:confirm|alert|prompt)\(['\"`]([^'\"`]+)['\"`](?:,\s*['\"`]([^'\"`]+)['\"`])?\)",
        _elbox, text
    )

    if not is_ts:
        # placeholder="中文" → :placeholder="t('key')"
        def _placeholder(m):
            nonlocal n
            zh = m.group(2)
            key = auto_keys.get(zh)
            if not key:
                return m.group(0)
            n += 1
            return f' :placeholder="t(\'{key}\')"'

        text = re.sub(r'(\s)placeholder=(["\'])([^"\']*[\u4e00-\u9fff][^"\']*)\2', _placeholder, text)
    return text, n


modified = []
for f in sorted(set(it[0] for it in items)):
    txt = f.read_text(encoding="utf-8")
    is_ts = f.suffix == ".ts"
    new_txt, n = replace_in_text(txt, is_ts=is_ts)
    if n > 0:
        f.write_text(new_txt, encoding="utf-8")
        modified.append((f, n))
        print(f"  {f.relative_to(ROOT)}: {n} 处替换")

# ============================================================
# 写入 i18n 文件 (zh-CN.ts / en-US.ts)
# ============================================================
print(f"\n[3/3] 写入 {len(auto_keys)} 个新 key 到 zh-CN.ts / en-US.ts...")


def insert_keys(fp: Path, entries: list[tuple[str, str]]) -> bool:
    """把 [key, value] 列表插入 common.feedback 段"""
    txt = fp.read_text(encoding="utf-8")
    pat_fb = re.compile(r"(\s*feedback\s*:\s*\{)([^\}]*?)(\})", re.DOTALL)
    m = pat_fb.search(txt)
    if not m:
        return False
    # 收集已有 key, 去重
    existing = set()
    for km in re.finditer(r"(\w+):\s*['\"`]", m.group(2)):
        existing.add(km.group(1))

    lines = []
    for k, v in entries:
        short = k.rsplit(".", 1)[-1]
        if short in existing:
            continue
        # 转义单引号
        v_esc = v.replace("\\", "\\\\").replace("'", "\\'")
        # 替换换行
        v_esc = v_esc.replace("\n", "\\n")
        lines.append(f"      {short}: '{v_esc}',")
        existing.add(short)
    if not lines:
        return True
    new_inner = m.group(2).rstrip() + "\n" + "\n".join(lines) + "\n    "
    new_txt = txt[:m.start(2)] + new_inner + txt[m.end(2):]
    fp.write_text(new_txt, encoding="utf-8")
    return True


zh_entries = [(k, zh) for zh, k in auto_keys.items()]
en_entries = [(k, translations.get(zh, zh)) for zh, k in auto_keys.items()]
ok_zh = insert_keys(ZH_FILE, zh_entries)
ok_en = insert_keys(EN_FILE, en_entries)
print(f"  zh-CN.ts: {'OK' if ok_zh else 'FAIL'}")
print(f"  en-US.ts: {'OK' if ok_en else 'FAIL'}")

# 输出报告
report = {
    "total_items": len(items),
    "unique_chinese": len(unique_zh),
    "files_modified": [{"file": str(f.relative_to(ROOT)), "replacements": n} for f, n in modified],
    "new_keys": [
        {"key": k, "zh": zh, "en": translations.get(zh, "[TRANSLATE_FAILED]")}
        for zh, k in sorted(auto_keys.items(), key=lambda x: x[1])
    ],
    "translation_failures": [zh for zh in unique_zh if translations.get(zh, zh) == zh and re.search(r'[\u4e00-\u9fff]', translations.get(zh, zh))],
}
report_path = Path(r"d:\projects\sakurafilter\spike-test\i18n_business_v2_report.json")
report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"\n报告: {report_path}")
print(f"修改文件: {len(modified)}, 新增 key: {len(auto_keys)}, 翻译失败: {len(report['translation_failures'])}")
