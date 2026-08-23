import json
d = json.load(open('hardcoded_zh_audit.json', encoding='utf-8'))
print('total:', len(d.get('findings', [])))
print('前 30 个:')
for f in d.get('findings', [])[:30]:
    print('  ' + f['file'] + ':' + str(f['line']) + ' [' + f['context'] + '] ' + f['text'][:80])
