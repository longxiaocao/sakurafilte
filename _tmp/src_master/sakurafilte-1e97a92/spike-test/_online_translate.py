"""在线翻译客户端 v3 (最终版)
- 主用 MyMemory (free, 5000 chars/day per IP, 1000 word/day anonymous)
- 备用 Google Translate unofficial endpoint (gtx)
- 离线 fallback: 词典 (优先于 MyMemory, 因为词典翻译精准)

v3 改进 (基于 v2 报告的问题):
  1. 词典优先: 词典翻译精准 + 离线, 优于 MyMemory 的多语言混乱
  2. 日文假名过滤: Hiragana 0x3040-0x309F + Katakana 0x30A0-0x30FF 视为"非英文"
  3. 嵌套占位符保护: 栈式解析, 支持 ${a?.b ? c : d} / 字符串边界
  4. 翻译后处理: 修复括号错位 / 缺空格 / 词序错误 / case 错误
  5. 短语白名单: MyMemory/词典都搞不定的短语, 直接 hardcode 翻译
"""
import urllib.request
import urllib.parse
import json
import time
import re
from typing import Optional, List, Tuple, Dict

# ============================================================
# 占位符保护 (栈式解析, 支持嵌套)
# ============================================================
PH_BASE = 0xE100  # 避开 0xE000 (常被字体用作 glyph)
PH_DELIM = 0xE1FF
TOKEN_LEN = 2


def _ph_token(idx: int) -> str:
    return chr(PH_BASE + idx) + chr(PH_DELIM)


def _ph_re_pattern() -> re.Pattern:
    return re.compile(f"[{chr(PH_BASE)}-{chr(PH_BASE + 0xFFF)}]")


def protect_placeholders(text: str) -> Tuple[str, List[str], List[str]]:
    placeholders: List[str] = []
    tokens: List[str] = []
    safe: List[str] = []
    n = len(text)
    i = 0
    while i < n:
        if i + 1 < n and text[i] == '$' and text[i + 1] == '{':
            placeholder, end = _scan_placeholder(text, i)
            if end > 0:
                placeholders.append(placeholder)
                idx = len(placeholders) - 1
                token = _ph_token(idx)
                tokens.append(token)
                safe.append(token)
                i = end
                continue
        if i + 1 < n and text[i] == '{' and text[i + 1] == '{':
            placeholder, end = _scan_placeholder(text, i)
            if end > 0:
                placeholders.append(placeholder)
                idx = len(placeholders) - 1
                token = _ph_token(idx)
                tokens.append(token)
                safe.append(token)
                i = end
                continue
        safe.append(text[i])
        i += 1
    return "".join(safe), placeholders, tokens


def _scan_placeholder(text: str, start: int) -> Tuple[str, int]:
    """从 text[start] 开始, 扫描匹配的 { ... }"""
    n = len(text)
    i = start + 2  # 跳过 ${ 或 {{
    depth = 1
    in_str: Optional[str] = None
    while i < n:
        c = text[i]
        if in_str:
            if c == '\\':
                i += 2
                continue
            if c == in_str:
                in_str = None
            i += 1
            continue
        if c in ('"', "'", '`'):
            in_str = c
        elif c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return text[start:i + 1], i + 1
        i += 1
    return "", -1


def restore_placeholders(safe_translated: str, placeholders: List[str], tokens: List[str]) -> str:
    pat = _ph_re_pattern()
    out_chars: List[str] = []
    i = 0
    ph_idx = 0
    while i < len(safe_translated):
        if ord(safe_translated[i]) >= PH_BASE and ord(safe_translated[i]) <= PH_BASE + 0xFFF:
            j = i
            while j < len(safe_translated) and ord(safe_translated[j]) >= PH_BASE and ord(safe_translated[j]) <= PH_BASE + 0xFFF:
                j += 1
            if ph_idx < len(placeholders):
                out_chars.append(placeholders[ph_idx])
                ph_idx += 1
            i = j
        else:
            out_chars.append(safe_translated[i])
            i += 1
    while ph_idx < len(placeholders):
        out_chars.append(placeholders[ph_idx])
        ph_idx += 1
    return "".join(out_chars)


# ============================================================
# MyMemory API (强化)
# ============================================================
def _is_english_enough(text: str) -> bool:
    """翻译结果必须含英文为主
    - 中文/日文比例 < 5% (更严格, 杜绝"半中半英"翻译)
    - 词典/MyMemory 都可能产生 "Please enter 完整 OEM Number" 这种糟糕结果, 必须拒绝
    """
    if not text:
        return False
    stripped = re.sub(r"[\s\d\W_]+", "", text)
    if not stripped:
        return True
    non_english = sum(
        1 for c in stripped
        if '\u4e00' <= c <= '\u9fff'
        or '\u3040' <= c <= '\u309f'
        or '\u30a0' <= c <= '\u30ff'
        or '\u3400' <= c <= '\u4dbf'
    )
    return (non_english / len(stripped)) < 0.05


def translate_mymemory(text: str, source: str = "zh-CN", target: str = "en") -> Optional[str]:
    if not text or not text.strip():
        return ""
    try:
        url = (
            f"https://api.mymemory.translated.net/get?"
            f"q={urllib.parse.quote(text)}&langpair={source}|{target}&de=admin@sakurafilter.dev"
        )
        req = urllib.request.Request(url, headers={"User-Agent": "SakuraFilter-i18n/3.0"})
        with urllib.request.urlopen(req, timeout=10) as r:
            data = json.loads(r.read())
        rd = data.get("responseData", {})
        translated = rd.get("translatedText", "")
        status = data.get("responseStatus", 0)
        if status != 200 or not translated:
            return None
        up = translated.upper()
        if "MYMEMORY WARNING" in up:
            return None
        if not _is_english_enough(translated):
            return None
        return translated
    except Exception:
        return None


# ============================================================
# 翻译后处理
# ============================================================
def _post_process(en: str) -> str:
    if not en:
        return en
    # 1) "Please enter your xxx." 句末多余句号
    en = re.sub(r"\bPlease enter your ([a-z\s]+?)\.\s*$", r"Please enter your \1", en, flags=re.IGNORECASE)
    # 2) "Please, enter" → "Please enter"
    en = re.sub(r"\bPlease,\s+enter\b", "Please enter", en, flags=re.IGNORECASE)
    # 3) "Compare up to products" (无空格接占位符) → "Compare up to"
    en = re.sub(r"\bCompare up to products(?=\W|\$|\b)", "Compare up to", en)
    # 4) "current${xxx}" → "current ${xxx}" (补空格)
    en = re.sub(r"([a-z])(\$\{)", r"\1 \2", en)
    # 5) "products${xxx}" → "${xxx} products" 或 "products ${xxx}"
    en = re.sub(r"\bproducts(\$\{)", r"products \1", en)
    # 6) "(X:)${...}" → "(X: ${...})" 括号错位
    en = re.sub(r"(\w[\w\s]*?):\)\s*\$\{", r"\1: ${", en)
    en = re.sub(r"\(\)\s*\$\{", "${", en)  # "()${" → "${"
    # 7) "$ {xxx}" → "${xxx}"
    en = re.sub(r"\$\s+\{", "${", en)
    # 8) "Please enter Username/Password" → 小写 (一般语境 username/password 是公共名词小写)
    en = re.sub(r"\bPlease enter Username\b", "Please enter username", en)
    en = re.sub(r"\bPlease enter Password\b", "Please enter password", en)
    # 9) 末位空格清理
    en = en.rstrip()
    return en


# ============================================================
# 短语白名单 (MyMemory/词典都翻不好的)
#   优先级: 最高, 强制覆盖
# ============================================================
PHRASE_OVERRIDES: Dict[str, str] = {
    "请输入密码": "Please enter password",
    "请输入用户名": "Please enter username",
    "请输入当前密码": "Please enter your current password",
    "请输入新密码": "Please enter the new password",
    "再次输入新密码": "Re-enter the new password",
    "用户名或密码错误": "Invalid username or password",
    "登录已过期, 请重新登录": "Session expired, please log in again",
    "登录已过期，请重新登录": "Session expired, please log in again",
    "已退出登录": "Logged out",
    "密码修改成功": "Password changed successfully",
    "cURL 已复制": "cURL copied",
    "已复制到剪贴板": "Copied to clipboard",
    "已清空": "Cleared",
    "已清空搜索条件": "Search criteria cleared",
    "复制失败": "Copy failed",
    "复制失败, 请手动选择": "Copy failed, please select manually",
    "复制失败，请手动选择": "Copy failed, please select manually",
    "至少 8 个字符": "8 characters or more",
    "OEM 查询": "OEM Lookup",
    "产品数据未加载": "Product data not loaded",
    "仅管理员可访问用户管理页": "Only administrators can access the user management page",
    "产品不存在或已下架": "Product does not exist or has been discontinued",
    "已触发测试错误, 请查看下方列表": "Test error triggered, see list below",
    "已触发测试错误，请查看下方列表": "Test error triggered, see list below",
    "无法加载 API 文档": "Unable to load API documentation",
    "无法加载 API 文档 (Swagger + 离线备份均失败)": "Unable to load API documentation (both Swagger and offline backups failed)",
    "网络连接失败,请检查网络": "Network connection failed, please check the network",
    "网络连接失败，请检查网络": "Network connection failed, please check the network",
    "请粘贴至少一个 OEM 编号": "Please paste at least one OEM number",
    "已发送暂停信号, checkpoint_id=${r.checkpointId ?? 'N/A'}": "Pause signal sent, checkpoint_id=${r.checkpointId ?? 'N/A'}",
    "已发送暂停信号，checkpoint_id=${r.checkpointId ?? 'N/A'}": "Pause signal sent, checkpoint_id=${r.checkpointId ?? 'N/A'}",
    "测试错误已记录, 请查看下方列表": "Test error recorded, see list below",
    "测试错误已记录，请查看下方列表": "Test error recorded, see list below",
    "已加入: ${data.items[0].oemNoDisplay}": "Added: ${data.items[0].oemNoDisplay}",
    "已加入：${data.items[0].oemNoDisplay}": "Added: ${data.items[0].oemNoDisplay}",
    "该产品已在对比列表中": "This product is already in the comparison list",
    "请先登录": "Please log in first",
    "请求超时,请检查网络后重试": "Request timed out, please check your network and try again",
    "请求超时，请检查网络后重试": "Request timed out, please check your network and try again",
    "服务器繁忙,请稍后重试 (错误码:${status})": "Server is busy, please try again later (Error code: ${status})",
    "服务器繁忙，请稍后重试 (错误码:${status})": "Server is busy, please try again later (Error code: ${status})",
    "最多 500 个 OEM, 当前 ${oems.length}": "Up to 500 OEMs, currently ${oems.length} entered",
    "最多 500 个 OEM，当前 ${oems.length}": "Up to 500 OEMs, currently ${oems.length} entered",
    "最多对比 ${MAX_COMPARE} 个产品": "Compare up to ${MAX_COMPARE} products",
    "粘贴 OEM 编号, 每行一个 (支持 tab/换行/逗号/分号分隔)\\n例如:\\nOEN-123\\nAB/CD/456\\n滤清器 1142": "Paste OEM numbers, one per line (tab/newline/comma/semicolon delimited)\\nExample:\\nOEN-123\\nAB/CD/456\\nFilter 1142",
    "至少需要输入 1 个搜索字段": "At least 1 search field is required",
    "清空确认": "Clear confirmation",
    "确定停售产品 ": "Confirm discontinue product ",
    "确定删除 ": "Confirm delete ",
    "确定删除品牌 ": "Confirm delete brand ",
    "确定删除用户 ": "Confirm delete user ",
    "确认": "Confirm",
    "替代 OEM 表格未就绪": "Alternative OEM form not ready",
    "搜索 message / type / tags…": "Search message / type / tags…",
    "搜索 message / type / tags...": "Search message / type / tags...",
    "搜索路径 / 方法 / 摘要…": "Search path / method / summary…",
    "搜索路径 / 方法 / 摘要...": "Search path / method / summary...",
    "已加载离线 openapi.json, 后端 Swagger 不可用": "Offline openapi.json loaded, backend Swagger is unavailable",
    "已加载离线 openapi.json，后端 Swagger 不可用": "Offline openapi.json loaded, backend Swagger is unavailable",
    "已移除": "Removed",
    "输入 045090 试试": "Try entering 045090",
    "输入产品 ID 加入": "Enter product ID to add",
    "请求频率超限, 请 ${retryAfter || 60}s 后重试": "Request rate limit exceeded, please try again in ${retryAfter || 60}s",
    "请求频率超限，请 ${retryAfter || 60}s 后重试": "Request rate limit exceeded, please try again in ${retryAfter || 60}s",
    "已触发 ${form.entity} ETL (${form.mode}${form.dryRun ? ', 试运行' : ''})": "Triggering ${form.entity} ETL (${form.mode}${form.dryRun ? ', dry run' : ''})",
    "即将触发 ${form.entity} ETL (${form.mode})": "About to trigger ${form.entity} ETL (${form.mode})",
    "网络异常: ${err.message || '请稍后重试'}": "Network exception: ${err.message || 'please try again later'}",
    "已发送暂停信号, checkpoint_id=${r.checkpointId ?? \\\"?\\\"}, 当前批次跑完后退出": "Pause signal sent, checkpoint_id=${r.checkpointId ?? \\\"?\\\"}, will exit after current batch finishes",
    "网络异常: ${err.message || \\\"请稍后重试\\\"}": "Network exception: ${err.message || \\\"please try again later\\\"}",
    "即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? ', 试运行' : ''}), 是否继续?": "About to trigger ${form.entity} ETL (${form.mode}${form.dryRun ? ', dry run' : ''}), continue?",
    "即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? \\\", 试运行\\\" : \\\"\\\"}), 是否继续?": "About to trigger ${form.entity} ETL (${form.mode}${form.dryRun ? \\\", dry run\\\" : \\\"\\\"}), continue?",
    "已发送暂停信号，checkpoint_id=${r.checkpointId ?? \"?\"}, 当前批次跑完后退出": "Pause signal sent, checkpoint_id=${r.checkpointId ?? \"?\"}, will exit after current batch finishes",
    "网络异常: ${err.message || \"请稍后重试\"}": "Network exception: ${err.message || \"please try again later\"}",
    "即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? \", 试运行\" : \"\"}), 是否继续?": "About to trigger ${form.entity} ETL (${form.mode}${form.dryRun ? \", dry run\" : \"\"}), continue?",
    "粘贴 OEM 编号, 每行一个 (支持 tab/换行/逗号/分号分隔)\n例如:\nOEN-123\nAB/CD/456\n滤清器 1142": "Paste OEM numbers, one per line (tab/newline/comma/semicolon delimited)\nExample:\nOEN-123\nAB/CD/456\nFilter 1142",
    "已发送暂停信号, checkpoint_id=${r.checkpointId ?? \"?\"}, 当前批次跑完后退出": "Pause signal sent, checkpoint_id=${r.checkpointId ?? \"?\"}, will exit after current batch finishes",
}


# ============================================================
# 词典 fallback
# ============================================================
def _dict_translate(text: str) -> Optional[str]:
    try:
        from _i18n_glossary import translate_zh_to_en  # type: ignore
        result = translate_zh_to_en(text)
        if result and _is_english_enough(result):
            return result
    except Exception:
        pass
    return None


# ============================================================
# 主翻译函数
# ============================================================
_cache: Dict[str, str] = {}


def translate_zh_to_en_online(text: str, use_cache: bool = True) -> str:
    if not text or not text.strip():
        return text
    safe, placeholders, tokens = protect_placeholders(text)

    # 0) 白名单 (最高优先级)
    if text in PHRASE_OVERRIDES:
        return PHRASE_OVERRIDES[text]
    if safe in PHRASE_OVERRIDES:
        return PHRASE_OVERRIDES[safe]

    if use_cache and safe in _cache:
        en = _cache[safe]
        return restore_placeholders(en, placeholders, tokens)

    # 1) 词典 (优先, 离线 + 精准)
    en = _dict_translate(safe)
    # 2) MyMemory (网络, 作为补全)
    if not en:
        en = translate_mymemory(safe)
    # 3) 终极 fallback: 保留中文 (人工后续修)
    if not en:
        en = safe
    en = _post_process(en)
    _cache[safe] = en
    time.sleep(0.12)
    return restore_placeholders(en, placeholders, tokens)


def batch_translate(texts: List[str], progress_cb=None) -> Dict[str, str]:
    out: Dict[str, str] = {}
    total = len(texts)
    for i, zh in enumerate(texts):
        if zh in out:
            continue
        out[zh] = translate_zh_to_en_online(zh)
        if progress_cb and (i + 1) % 5 == 0:
            progress_cb(i + 1, total, zh)
    return out


if __name__ == "__main__":
    samples = [
        "请输入完整 OEM 编号",
        "OEM 查询",
        "已退出登录",
        "密码修改成功",
        "请输入当前密码",
        "至少 8 个字符",
        "再次输入新密码",
        "粘贴 OEM 编号, 每行一个",
        "即将触发 ${form.entity} ETL (${form.mode})",
        "已加入: ${data.items[0].oemNoDisplay}",
        "cURL 已复制",
        "已清空",
        "用户名或密码错误",
        "服务器繁忙,请稍后重试 (错误码:${status})",
        "请输入密码",
        "请输入用户名",
        "最多 500 个 OEM, 当前 ${oems.length}",
        "最多对比 ${MAX_COMPARE} 个产品",
    ]
    for zh in samples:
        en = translate_zh_to_en_online(zh)
        print(f"  {zh:55s} → {en}")
