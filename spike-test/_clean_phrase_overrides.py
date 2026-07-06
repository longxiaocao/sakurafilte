"""清理 _online_translate.py 中 PHRASE_OVERRIDES 重复 key + 补充完整短语白名单"""
import re
from pathlib import Path

FP = Path(r"d:\projects\sakurafilter\spike-test\_online_translate.py")
src = FP.read_text(encoding="utf-8")

# 1) 解析现有 PHRASE_OVERRIDES
m = re.search(r"PHRASE_OVERRIDES:\s*Dict\[str,\s*str\]\s*=\s*\{(.*?)\n\}", src, re.DOTALL)
if not m:
    raise RuntimeError("PHRASE_OVERRIDES not found")
body = m.group(1)

# 提取每个 key (字符串) + value (字符串)
pairs: dict[str, str] = []
pair_re = re.compile(r'"((?:[^"\\]|\\.)*)"\s*:\s*"((?:[^"\\]|\\.)*)"')
for km in pair_re.finditer(body):
    pairs.append((km.group(1), km.group(2)))

# 2) 合并: 后面出现的覆盖前面 (保持插入顺序, 但同 key 取最后值)
seen: dict[str, str] = {}
order: list[str] = []
for k, v in pairs:
    if k not in seen:
        order.append(k)
    seen[k] = v

# 3) 补充已知翻译问题 (zh -> en)
EXTRAS: dict[str, str] = {
    # === 全角逗号变体 ===
    "登录已过期，请重新登录": "Session expired, please log in again",
    "复制失败，请手动选择": "Copy failed, please select manually",
    "已触发测试错误，请查看下方列表": "Test error triggered, see list below",
    "已发送暂停信号，checkpoint_id=${r.checkpointId ?? 'N/A'}": "Pause signal sent, checkpoint_id=${r.checkpointId ?? 'N/A'}",
    "已发送暂停信号，checkpoint_id=${r.checkpointId ?? \"?\"}, 当前批次跑完后退出": "Pause signal sent, checkpoint_id=${r.checkpointId ?? \"?\"}, will exit after current batch finishes",
    "网络连接失败，请检查网络": "Network connection failed, please check the network",
    "请求超时，请检查网络后重试": "Request timed out, please check your network and try again",
    "服务器繁忙，请稍后重试 (错误码:${status})": "Server is busy, please try again later (Error code: ${status})",
    "最多 500 个 OEM，当前 ${oems.length}": "Up to 500 OEMs, currently ${oems.length} entered",
    "请求频率超限，请 ${retryAfter || 60}s 后重试": "Request rate limit exceeded, please try again in ${retryAfter || 60}s",
    "已加载离线 openapi.json，后端 Swagger 不可用": "Offline openapi.json loaded, backend Swagger is unavailable",
    # === 半中半英: "File was removed" 错误 ===
    "已移除": "Removed",
    # === "Alternate OEM Form Not Ready" 大小写错 ===
    "替代 OEM 表格未就绪": "Alternative OEM form not ready",
    # === "Identify discontinued products" 错译 ===
    "确定停售产品 ": "Confirm discontinue product ",
    # === "Confirm Deletion" 不准确 + 大小写 ===
    "确定删除 ": "Confirm delete ",
    # === "OK to delete brand" 不准确 ===
    "确定删除品牌 ": "Confirm delete brand ",
    "确定删除用户 ": "Confirm delete user ",
    # === "OK" 太短 ===
    "确认": "Confirm",
    # === "Clear Confirm" 词序错 ===
    "清空确认": "Clear confirmation",
    # === "Please log in first." 多余句号 ===
    "请先登录": "Please log in first",
    # === "Session token has expired..." 冗长 ===
    "登录已过期, 请重新登录": "Session expired, please log in again",
    # === "Enter 045090 to try" 语序 ===
    "输入 045090 试试": "Try entering 045090",
    # === "Please, enter Username" 多余逗号 + 大写 ===
    "请输入用户名": "Please enter username",
    # === "Please enter your current password." 多余句号 ===
    "请输入当前密码": "Please enter your current password",
    # === 词序: "Compare up to products X" 缺空格 ===
    "最多对比 ${MAX_COMPARE} 个产品": "Compare up to ${MAX_COMPARE} products",
    # === 句号多余 ===
    "再次输入新密码": "Re-enter the new password",
    # === 全角省略号 vs 三点 ===
    "搜索 message / type / tags…": "Search message / type / tags…",
    "搜索路径 / 方法 / 摘要…": "Search path / method / summary…",
    # === 输入产品 ID 加入 ===
    "输入产品 ID 加入": "Enter product ID to add",
    # === 错误码括号错位 ===
    "服务器繁忙,请稍后重试 (错误码:${status})": "Server is busy, please try again later (Error code: ${status})",
    # === OEM 编号 ===
    "OEM 查询": "OEM Lookup",
    # === placeholder/info 系列 ===
    "产品数据未加载": "Product data not loaded",
    "仅管理员可访问用户管理页": "Only administrators can access the user management page",
    "无法加载 API 文档": "Unable to load API documentation",
    "无法加载 API 文档 (Swagger + 离线备份均失败)": "Unable to load API documentation (both Swagger and offline backups failed)",
    "请粘贴至少一个 OEM 编号": "Please paste at least one OEM number",
    "该产品已在对比列表中": "This product is already in the comparison list",
    "至少需要输入 1 个搜索字段": "At least 1 search field is required",
    "至少 8 个字符": "8 characters or more",
    # === form.entity 模板 ===
    "已触发 ${form.entity} ETL (${form.mode}${form.dryRun ? ', 试运行' : ''})": "Triggering ${form.entity} ETL (${form.mode}${form.dryRun ? ', dry run' : ''})",
    "即将触发 ${form.entity} ETL (${form.mode})": "About to trigger ${form.entity} ETL (${form.mode})",
    "网络异常: ${err.message || '请稍后重试'}": "Network exception: ${err.message || 'please try again later'}",
    "网络异常: ${err.message || \"请稍后重试\"}": "Network exception: ${err.message || \"please try again later\"}",
    "即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? ', 试运行' : ''}), 是否继续?": "About to trigger ${form.entity} ETL (${form.mode}${form.dryRun ? ', dry run' : ''}), continue?",
    "即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? \", 试运行\" : \"\"}), 是否继续?": "About to trigger ${form.entity} ETL (${form.mode}${form.dryRun ? \", dry run\" : \"\"}), continue?",
    # === OEN-123 多行模板 ===
    "粘贴 OEM 编号, 每行一个 (支持 tab/换行/逗号/分号分隔)\n例如:\nOEN-123\nAB/CD/456\n滤清器 1142":
        "Paste OEM numbers, one per line (tab/newline/comma/semicolon delimited)\nExample:\nOEN-123\nAB/CD/456\nFilter 1142",
    # === 通用 ===
    "产品不存在或已下架": "Product does not exist or has been discontinued",
    "已退出登录": "Logged out",
    "密码修改成功": "Password changed successfully",
    "cURL 已复制": "cURL copied",
    "已复制到剪贴板": "Copied to clipboard",
    "已清空": "Cleared",
    "已清空搜索条件": "Search criteria cleared",
    "复制失败": "Copy failed",
    "已加载离线 openapi.json, 后端 Swagger 不可用": "Offline openapi.json loaded, backend Swagger is unavailable",
    "已加入: ${data.items[0].oemNoDisplay}": "Added: ${data.items[0].oemNoDisplay}",
    "已加入：${data.items[0].oemNoDisplay}": "Added: ${data.items[0].oemNoDisplay}",
    "已发送暂停信号, checkpoint_id=${r.checkpointId ?? 'N/A'}": "Pause signal sent, checkpoint_id=${r.checkpointId ?? 'N/A'}",
    "已发送暂停信号, checkpoint_id=${r.checkpointId ?? \"?\"}, 当前批次跑完后退出": "Pause signal sent, checkpoint_id=${r.checkpointId ?? \"?\"}, will exit after current batch finishes",
    "测试错误已记录, 请查看下方列表": "Test error recorded, see list below",
    "测试错误已记录，请查看下方列表": "Test error recorded, see list below",
    "网络连接失败,请检查网络": "Network connection failed, please check the network",
    "请求超时,请检查网络后重试": "Request timed out, please check your network and try again",
    "最多 500 个 OEM, 当前 ${oems.length}": "Up to 500 OEMs, currently ${oems.length} entered",
    "已触发测试错误, 请查看下方列表": "Test error triggered, see list below",
    "请输入密码": "Please enter password",
    "请输入新密码": "Please enter the new password",
    "用户名或密码错误": "Invalid username or password",
}

# 4) 合并: EXTRAS 优先 (新加)
for k, v in EXTRAS.items():
    if k not in seen:
        order.append(k)
    seen[k] = v

# 5) 重写 PHRASE_OVERRIDES
def esc(s: str) -> str:
    return s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")

new_lines = ["PHRASE_OVERRIDES: Dict[str, str] = {"]
for k in order:
    v = seen[k]
    new_lines.append(f'    "{esc(k)}": "{esc(v)}",')
new_lines.append("}")
new_body = "\n".join(new_lines)

new_src = src[:m.start()] + new_body + src[m.end():]
FP.write_text(new_src, encoding="utf-8")
print(f"PHRASE_OVERRIDES 重写完成, {len(order)} 个唯一 key")
print(f"原 {len(pairs)} 对 → 去重 {len(seen)} → 补 EXTRAS {len(EXTRAS)} → 终 {len(order)}")
