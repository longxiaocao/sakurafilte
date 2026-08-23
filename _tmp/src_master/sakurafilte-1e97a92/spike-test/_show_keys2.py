import re
text = open(r'../frontend/src/i18n/locales/zh-CN.ts', encoding='utf-8').read()
# 找所有 'admin.helpview.string.l26_xxx' 类似
matches = re.findall(r"'admin\.helpview\.string\.l(26|38|46)_[^']+':\s*'[^']*'", text)
for m in matches[:20]:
    print(m)
print('---')
# 找所有 key 包含 l26 / l38 / l46
for prefix in ['l26', 'l38', 'l46']:
    for m in re.finditer(r"'(admin\.helpview\.string\." + prefix + r"_[^']+)':\s*'([^']*)'", text):
        print(m.group(1) + ' => ' + m.group(2)[:60])
