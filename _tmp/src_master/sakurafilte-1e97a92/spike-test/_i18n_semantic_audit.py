"""i18n 语义化分析 v5 - 修正格式"""
import re
import collections
from pathlib import Path

zh = Path('frontend/src/i18n/locales/zh-CN.ts').read_text(encoding='utf-8')

# 形如 l273_: '加载产品失败'  (key 在左, 无引号)
matches = re.findall(r"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*:\s*'", zh, re.M)
print(f'总 key 匹配数: {len(matches)}')
print('前 30:', matches[:30])

ln_keys = [k for k in matches if re.match(r'^l\d+_', k)]
sem_keys = [k for k in matches if not re.match(r'^l\d+_', k)]
print(f'  lXXX_ 模式 (行号基准, 非语义化): {len(ln_keys)}')
print(f'  语义化 key: {len(sem_keys)}')

# 按 file 统计 (上下文)
# 通过位置推断: 找 admin.{file} 块, 在其下的 key 归该 file
file_blocks = re.findall(r"(\w+):\s*\{", zh)
print(f'\n共 {len(file_blocks)} 个块')

# 提取 admin.* 块名
admin_files = re.findall(r"^\s{4}(\w+):\s*\{", zh, re.M)
admin_files = [f for f in admin_files if not f.startswith('admin') and f not in (
    'error', 'placeholder', 'string', 'success', 'title', 'warning',
    'label', 'info', 'buttontext', 'templatetext', 'nav', 'public',
    'common', 'auth', 'search', 'product', 'theme'
)]
print(f'admin.* 文件块: {set(admin_files)}')
