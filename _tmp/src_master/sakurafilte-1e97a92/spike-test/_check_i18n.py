import re
from pathlib import Path
ROOT = Path('.').resolve().parent
FRONT = ROOT / 'frontend' / 'src'
texts = []
for f in list(FRONT.rglob('*.vue')) + list(FRONT.rglob('*.ts')):
    try: texts.append(f.read_text(encoding='utf-8', errors='replace'))
    except: pass
all_text = '\n'.join(texts)
# 严格 t 之前不能是字母数字/_/$/. (排除 createElement('meta') 等)
calls = set()
for m in re.finditer(r"(?:^|[^A-Za-z0-9_$.])(?:\$t|t|i18n\.t)\s*\(\s*['\"`]([A-Za-z][A-Za-z0-9_.\-]+)['\"`]", all_text, re.MULTILINE):
    calls.add(m.group(1))
print('所有 t 调用 key:', len(calls))
for c in sorted(calls):
    print(' ', c)
