"""P1-1 扫描: 业务错误提示类硬编码中文 (优先级最高)"""
import re
from pathlib import Path
from collections import Counter

ROOT = Path(r"d:\projects\sakurafilter\frontend\src")
ZH = re.compile(r'[\u4e00-\u9fff]')
EL_MESSAGE = re.compile(r'(ElMessage\.[a-zA-Z]+\s*\(\s*[\'"`])', re.MULTILINE)
PROMPT = re.compile(r'[\'"`][^\'"`]*[\u4e00-\u9fff][^\'"`]*[\'"`]')

# 分类: 业务错误提示
# - ElMessage.error / warning / success / info 的中文
# - form rule 的 message
# - confirm 按钮的 title/message
# - placeholder (业务性强)
# - aria-label / title 中可被翻译的
# - 业务枚举值 (字典的中文名)

CATEGORIES = {
    "ElMessage": re.compile(r'ElMessage\.(error|warning|success|info)\s*\(\s*[\'"\`]([^\'"\`]+)[\'"\`]'),
    "ElNotification": re.compile(r'ElNotification\.(error|warning|success|info)\s*\(\s*\{[^}]*?message\s*:\s*[\'"\`]([^\'"\`]+)'),
    "placeholder": re.compile(r'placeholder=[\'"\`]([^\'"\`]*[\u4e00-\u9fff][^\'"\`]*)[\'"\`]'),
    "form_rule": re.compile(r'message\s*:\s*[\'"\`]([^\'"\`]*[\u4e00-\u9fff][^\'"\`]*)[\'"\`]'),
    "ElMessageBox": re.compile(r'(?:title|message)\s*:\s*[\'"\`]([^\'"\`]*[\u4e00-\u9fff][^\'"\`]*)[\'"\`]'),
    "alert": re.compile(r'<(el-alert|el-notification)[^>]*>([^<]*[\u4e00-\u9fff][^<]*)</'),
    "raw_template": None,  # 后续用 i18n 标记做反向扫描
}

results = {k: [] for k in CATEGORIES}
total_files = 0
for vue in ROOT.rglob("*.vue"):
    total_files += 1
    text = vue.read_text(encoding="utf-8", errors="ignore")
    for name, pat in CATEGORIES.items():
        if pat is None: continue
        for m in pat.finditer(text):
            try:
                # ElMessage/ElNotification 有 2 个组, 其余 1 个
                if name in ("ElMessage", "ElNotification"):
                    grp = m.group(2)
                else:
                    grp = m.group(1)
                if ZH.search(grp):
                    # 找到所在行号
                    line = text[:m.start()].count("\n") + 1
                    results[name].append((vue, line, grp))
            except IndexError:
                pass

print(f"扫描 {total_files} 个 .vue 文件")
print("=" * 70)
total = 0
for name, items in results.items():
    n = len(items)
    total += n
    print(f"\n[{name}] {n} 处")
    # 按文件聚合
    by_file = Counter(p[0] for p in items)
    for f, c in by_file.most_common(5):
        print(f"  {f.relative_to(ROOT)}: {c} 处")
    # 列出前 3 个示例
    for f, ln, txt in items[:3]:
        short = txt[:60] + ("..." if len(txt) > 60 else "")
        print(f"    L{ln}: {short}")
print(f"\n{'='*70}\n总业务错误提示类硬编码中文: {total} 处")
