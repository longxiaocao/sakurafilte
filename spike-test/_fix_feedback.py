"""重写 en-US.ts / zh-CN.ts 的 feedback 段, 移除断裂的 }, 重新合并"""
import re
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\frontend\src\i18n\locales")

# 标准 feedback 内容
FEEDBACK_ZH = """      error_001: '服务器繁忙,请稍后重试 (错误码:${status})',
      error_002: '网络连接失败,请检查网络',
      error_003: '请输入密码',
      error_004: '请输入当前密码',
      error_005: '请输入用户名',
      error_006: '请求超时,请检查网络后重试',
      error_007: '产品不存在或已下架',
      error_008: '复制失败',
      error_009: '复制失败, 请手动选择',
      error_010: '已触发测试错误, 请查看下方列表',
      info_001: 'OEM 查询',
      info_002: '产品数据未加载',
      info_003: '仅管理员可访问用户管理页',
      info_005: '已加入: ${data.items[0].oemNoDisplay}',
      info_006: '网络异常: ${err.message || \'请稍后重试\'}',
      info_007: '即将触发 ${form.entity} ETL (${form.mode}${form.dryRun ? \', 试运行\' : \'\'}), 是否继续?',
      info_008: '输入 045090 试试',
      info_009: '输入产品 ID 加入',
      success_001: '已加入: ${data.items[0].oemNoDisplay}',
      success_002: 'cURL 已复制',
      success_010: '密码修改成功',
      success_011: '已加载离线 openapi.json, 后端 Swagger 不可用',
      success_014: '已复制到剪贴板',
      success_015: '已清空',
      success_016: '已清空搜索条件',
      success_019: '已退出登录',
      success_020: '已发送暂停信号, checkpoint_id=${r.checkpointId ?? \'N/A\'}',
      success_021: '已发送暂停信号, checkpoint_id=${r.checkpointId ?? "?"}, 当前批次跑完后退出',
      warn_001: '再次输入新密码',
      warn_002: '最多 500 个 OEM, 当前 ${oems.length}',
      warn_003: '最多对比 ${MAX_COMPARE} 个产品',
      warn_004: '清空确认',
      warn_005: '确定停售产品 ',
      warn_006: '确定删除 ',
      warn_007: '确定删除品牌 ',
      warn_008: '确定删除用户 ',
      warn_009: '确认',
      warn_010: '至少 8 个字符',
"""

FEEDBACK_EN = """      error_001: 'Server is busy, please try again later (Error code: ${status})',
      error_002: 'Network connection failed, please check the network',
      error_003: 'Please enter password',
      error_004: 'Please enter your current password',
      error_005: 'Please enter username',
      error_006: 'Request timed out, please check your network and try again',
      error_007: 'Product does not exist or has been discontinued',
      error_008: 'Copy failed',
      error_009: 'Copy failed, please select manually',
      error_010: 'Test error triggered, see list below',
      info_001: 'OEM Lookup',
      info_002: 'Product data not loaded',
      info_003: 'Only administrators can access the user management page',
      info_005: 'Added: ${data.items[0].oemNoDisplay}',
      info_006: 'Network exception: ${err.message || \'please try again later\'}',
      info_007: 'About to trigger ${form.entity} ETL (${form.mode}${form.dryRun ? \', dry run\' : \'\'}), continue?',
      info_008: 'Try entering 045090',
      info_009: 'Enter product ID to add',
      success_001: 'Added: ${data.items[0].oemNoDisplay}',
      success_002: 'cURL copied',
      success_010: 'Password changed successfully',
      success_011: 'Offline openapi.json loaded, backend Swagger is unavailable',
      success_014: 'Copied to clipboard',
      success_015: 'Cleared',
      success_016: 'Search criteria cleared',
      success_019: 'Logged out',
      success_020: 'Pause signal sent, checkpoint_id=${r.checkpointId ?? \'N/A\'}',
      success_021: 'Pause signal sent, checkpoint_id=${r.checkpointId ?? "?"}, will exit after current batch finishes',
      warn_001: 'Re-enter the new password',
      warn_002: 'Up to 500 OEMs, currently ${oems.length} entered',
      warn_003: 'Compare up to ${MAX_COMPARE} products',
      warn_004: 'Clear confirmation',
      warn_005: 'Confirm discontinue product ',
      warn_006: 'Confirm delete ',
      warn_007: 'Confirm delete brand ',
      warn_008: 'Confirm delete user ',
      warn_009: 'Confirm',
      warn_010: '8 characters or more',
"""


def rewrite(fp: Path, new_feedback: str):
    """重写 feedback 段, 保留前后结构"""
    txt = fp.read_text(encoding="utf-8")
    # 找 feedback 段: 'feedback: {' 开始, 到对应的 '}' (用栈配对)
    start_match = re.search(r"(\s*)feedback\s*:\s*\{", txt)
    if not start_match:
        print(f"  {fp.name}: 未找到 feedback 段")
        return False
    start = start_match.end()
    # 栈式找匹配的 }
    depth = 1
    i = start
    in_str = False
    quote = None
    while i < len(txt) and depth > 0:
        c = txt[i]
        if in_str:
            if c == '\\':
                i += 2
                continue
            if c == quote:
                in_str = False
            i += 1
            continue
        if c in ("'", '"', '`'):
            in_str = True
            quote = c
        elif c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
        i += 1
    end = i  # '}' 之后
    # 检查 end 前的 '},' 模式
    end_minus_1 = end - 1
    # 重写: 前缀 + 新feedback + 后缀
    new_txt = txt[:start_match.start()] + new_feedback + txt[end:]
    # 修复: '},' 后跟随空行 + action: { 是 OK
    # 但要确保 feedback 段末尾有 '},'
    if not new_feedback.rstrip().endswith("},"):
        # 替换为 '},'
        new_txt = new_txt.replace(new_feedback.rstrip(), new_feedback.rstrip() + ",\n", 1)
    fp.write_text(new_txt, encoding="utf-8")
    print(f"  {fp.name}: feedback 段已重写")
    return True


# 不直接重写，先尝试更安全的修复
# 仅修复 feedback 段中的断裂 }, 问题：合并到 } 那里
def fix_feedback(fp: Path):
    """修复 feedback 段中部断裂的 }, """
    txt = fp.read_text(encoding="utf-8")
    # 找 feedback 段
    start_match = re.search(r"(\s*)feedback\s*:\s*\{", txt)
    if not start_match:
        return False
    start = start_match.end()
    # 找段结束
    depth = 1
    i = start
    in_str = False
    quote = None
    while i < len(txt) and depth > 0:
        c = txt[i]
        if in_str:
            if c == '\\':
                i += 2
                continue
            if c == quote:
                in_str = False
            i += 1
            continue
        if c in ("'", '"', '`'):
            in_str = True
            quote = c
        elif c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
        i += 1
    end_brace = i - 1
    fb_segment = txt[start:end_brace]
    # 移除 fb_segment 中的所有 },
    fb_clean = re.sub(r"\s*\},?\s*\n", "\n", fb_segment)
    # 重新拼装: 添加 } 关闭
    new_txt = txt[:start] + "\n" + fb_clean.strip() + "\n    " + txt[end_brace:]
    fp.write_text(new_txt, encoding="utf-8")
    print(f"  {fp.name}: feedback 段内 }, 已清理")
    return True


for fp in [ROOT / "zh-CN.ts", ROOT / "en-US.ts"]:
    fix_feedback(fp)
