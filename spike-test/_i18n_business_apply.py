"""P1-1: 批量替换业务错误提示类硬编码中文为 i18n 调用"""
import re
from pathlib import Path
import json

ROOT = Path(r"d:\projects\sakurafilter\frontend\src")
ZH_FILE = ROOT / "i18n" / "locales" / "zh-CN.ts"
EN_FILE = ROOT / "i18n" / "locales" / "en-US.ts"

# 加载现有 zh-CN 收集已有 key
zh_text = ZH_FILE.read_text(encoding="utf-8")
# 提取到 dict 形式
zh_dict = {}
# 简单解析: 形如 key: "value" 的扁平键
# 因为 zh-CN.ts 是嵌套对象, 简单起见用正则匹配顶层 flat key
pat = re.compile(r'^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*:\s*[\'"`]([^\'"`]+)[\'"`]', re.MULTILINE)
existing_keys = set()
for m in pat.finditer(zh_text):
    k = m.group(1)
    v = m.group(2)
    if v.strip() and any('\u4e00' <= c <= '\u9fff' for c in v):
        existing_keys.add(v)

# 自动生成的 i18n key 映射
# 模式: {zh_text} → key
auto_keys = {}
# 用于最后写入的扁平 key 列表
new_zh_entries = []
new_en_entries = []
counter = 0

def make_key(zh_text: str) -> str:
    """根据中文生成语义化 key, 同中文复用同一 key"""
    global counter
    if zh_text in auto_keys:
        return auto_keys[zh_text]
    counter += 1
    # 简化: 用 'msg_' + 序号 + 描述性后缀
    # 根据常见模式给个语义化前缀
    if "成功" in zh_text or "已" in zh_text and "退出" in zh_text:
        prefix = "success"
    elif "失败" in zh_text or "错误" in zh_text:
        prefix = "error"
    elif "请" in zh_text or "至少" in zh_text or "请粘" in zh_text or "无法" in zh_text or "不能" in zh_text:
        prefix = "warn"
    else:
        prefix = "info"
    key = f"common.feedback.{prefix}_{counter:03d}"
    auto_keys[zh_text] = key
    # 英文翻译 (简陋, 后续可手动调)
    en = zh_text  # 占位, 实际英文按模式翻译
    if "成功" in zh_text:
        en = re.sub(r'(\S+)成功', r'\1 succeeded', zh_text)
        en = re.sub(r'成功', 'succeeded', en)
    elif "失败" in zh_text:
        en = re.sub(r'失败', 'failed', zh_text)
    elif "退出" in zh_text and "登录" in zh_text:
        en = "Logged out"
    elif "请输入" in zh_text:
        en = re.sub(r'请输入', 'Please enter ', zh_text)
    elif "请粘" in zh_text:
        en = re.sub(r'请粘', 'Please paste ', zh_text)
    elif "至少" in zh_text:
        en = re.sub(r'至少', 'at least ', zh_text)
    elif "再次输入" in zh_text:
        en = re.sub(r'再次输入', 'Re-enter ', zh_text)
    elif "无法" in zh_text:
        en = re.sub(r'无法', 'Cannot ', zh_text)
    elif "不能" in zh_text:
        en = re.sub(r'不能', 'Cannot ', zh_text)
    elif "暂无" in zh_text:
        en = re.sub(r'暂无(\S+)', r'No \1', zh_text)
    elif "未找到" in zh_text:
        en = re.sub(r'未找到', 'No matching', zh_text)
    elif "加载失败" in zh_text:
        en = "Load failed"
    new_zh_entries.append((key, zh_text))
    new_en_entries.append((key, en))
    return key


# 1) 替换 ElMessage.x('中文') → ElMessage.x(t('common.feedback.xxx'))
el_msg_pat = re.compile(r"(ElMessage\.(error|warning|success|info)\s*\(\s*)['\"`]([^'\"`]+)['\"`](\s*[,)])")

def replace_elmsg(m):
    prefix, kind, zh, suffix = m.group(1), m.group(2), m.group(3), m.group(4)
    key = make_key(zh)
    return f"{prefix}t('{key}'){suffix}"

# 2) 替换 ElMessageBox.confirm / alert 内部 title/message
#    形如: ElMessageBox.confirm('内容', '标题', {...})
#    较复杂, 暂不处理, 改为占位标注

# 3) 替换 placeholder="中文" → :placeholder="t('common.placeholder.xxx')"
#    注意: 属性从字符串改为绑定表达式
placeholder_pat = re.compile(r'(\s)placeholder=(["\'])([^"\']*[\u4e00-\u9fff][^"\']*)\2')

def replace_placeholder(m):
    pre, quote, zh = m.group(1), m.group(2), m.group(3)
    key = make_key(zh)
    return f'{pre}:placeholder="t(\'{key}\')"'

# 4) 替换 ElMessageBox.confirm 标题/内容
#    简单模式: ElMessageBox.confirm('内容', '标题', ...)
#    → ElMessageBox.confirm(t('k1'), t('k2'), ...)
el_box_pat = re.compile(r"(ElMessageBox\.(?:confirm|alert|prompt)\s*\(\s*)['\"`]([^'\"`]+)['\"`](\s*,\s*)['\"`]([^'\"`]+)['\"`]")

def replace_elbox(m):
    pre, body_zh, mid, title_zh = m.group(1), m.group(2), m.group(3), m.group(4)
    k1 = make_key(body_zh)
    k2 = make_key(title_zh)
    return f"{pre}t('{k1}'){mid}t('{k2}')"

# 5) 替换单参数 ElMessageBox (如 alert/confirm 单个字符串)
el_box_single_pat = re.compile(r"(ElMessageBox\.(?:confirm|alert|prompt)\s*\(\s*)['\"`]([^'\"`]+)['\"`]")

def replace_elbox_single(m):
    pre, zh = m.group(1), m.group(2)
    key = make_key(zh)
    return f"{pre}t('{key}')"


# 处理所有 .vue 文件
modified = []
for vue in ROOT.rglob("*.vue"):
    text = vue.read_text(encoding="utf-8")
    orig = text
    # 注意顺序: 复合模式先, 单参数模式后
    text = el_box_pat.sub(replace_elbox, text)
    text = el_box_single_pat.sub(replace_elbox_single, text)
    text = el_msg_pat.sub(replace_elmsg, text)
    text = placeholder_pat.sub(replace_placeholder, text)
    if text != orig:
        vue.write_text(text, encoding="utf-8")
        modified.append((vue, orig, text))

print(f"修改 {len(modified)} 个 .vue 文件:")
for f, _, _ in modified:
    print(f"  {f.relative_to(ROOT)}")

# 把新 key 写入 zh-CN.ts 和 en-US.ts
# 找到 common.feedback 命名空间, 或在 common 下新建
# 安全做法: 追加到 zh-CN.ts 顶部导出对象, 假定有 common 段
def insert_keys(filepath: Path, entries, marker: str = "feedback:"):
    """在 'common' 对象的 'feedback' 字段下插入新 key"""
    txt = filepath.read_text(encoding="utf-8")
    # 找到 'feedback:' 位置
    pat_fb = re.compile(r'(\s*feedback\s*:\s*\{)([^\}]*?)(\})', re.DOTALL)
    m = pat_fb.search(txt)
    if not m:
        # 没找到 feedback 段, 在 common 末尾插入
        # 找 'common:' 段
        pat_common = re.compile(r"(\s*common\s*:\s*\{)([^\}]*?)(\})", re.DOTALL)
        mc = pat_common.search(txt)
        if not mc:
            print(f"  [WARN] {filepath.name}: 找不到 'common:' 段, 跳过")
            return False
        new_inner = mc.group(2).rstrip() + "\n    feedback: {},\n  }"
        new_txt = txt[:mc.start(2)] + new_inner + txt[mc.end(2):]
        # 重新搜索
        m = pat_fb.search(new_txt)
        if not m:
            print(f"  [WARN] {filepath.name}: 插入 feedback 段后仍找不到")
            return False
        txt = new_txt

    # 在 feedback 内部追加
    new_inner_keys = ""
    for k, v in entries:
        # 转义单引号
        v_esc = v.replace("'", "\\'")
        new_inner_keys += f"\n      {k.split('.')[-1]}: '{v_esc}',"
    # 检查重复
    inner = m.group(2)
    if inner.strip():
        new_inner = inner.rstrip() + new_inner_keys + "\n    "
    else:
        new_inner = "\n    " + new_inner_keys.strip(",\n") + ",\n    "

    new_txt = txt[:m.start(2)] + new_inner + txt[m.end(2):]
    filepath.write_text(new_txt, encoding="utf-8")
    return True

print(f"\n写入 {len(new_zh_entries)} 个新 key 到 zh-CN.ts / en-US.ts")
if new_zh_entries:
    ok_zh = insert_keys(ZH_FILE, new_zh_entries)
    ok_en = insert_keys(EN_FILE, new_en_entries)
    print(f"  zh-CN.ts: {'OK' if ok_zh else 'FAIL'}")
    print(f"  en-US.ts: {'OK' if ok_en else 'FAIL'}")

# 输出 i18n_replace_report.json
report = {
    "modified_files": [str(f.relative_to(ROOT)) for f, _, _ in modified],
    "new_keys": [
        {"key": k, "zh": z, "en": e}
        for (k, z), (_, e) in zip(new_zh_entries, new_en_entries)
    ],
    "total_keys_generated": counter,
}
Path(r"d:\projects\sakurafilter\spike-test\i18n_business_replace.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(f"\n报告: spike-test/i18n_business_replace.json")
print(f"总生成 key: {counter}")
