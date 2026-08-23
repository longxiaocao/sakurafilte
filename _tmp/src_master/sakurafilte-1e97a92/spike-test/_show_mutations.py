import json
r = json.load(open('api_resilience_audit.json', encoding='utf-8'))
# 写操作关键字
mutations = ['create', 'update', 'remove', 'delete', 'cancel', 'pause', 'resume', 'trigger',
             'discontinue', 'restore', 'resetPassword', 'upload', 'logout', 'reset', 'change']
print('=== 写操作无 loading (mutation) ===')
mut_no_loading = []
for f in r['findings']:
    if f['category'] != 'no_loading': continue
    name = f['message'].split()[0]  # 如 'etlApi.trigger()'
    if any(m in name for m in mutations):
        mut_no_loading.append(f)
        print('  {}:{}  {}'.format(f['file'], f['line'], f['message']))
print()
print('=== 查询操作无 loading (query) - 低风险 ===')
for f in r['findings']:
    if f['category'] != 'no_loading': continue
    name = f['message'].split()[0]
    if not any(m in name for m in mutations):
        print('  {}:{}  {}'.format(f['file'], f['line'], f['message']))
print()
print('写操作缺失总数: {}'.format(len(mut_no_loading)))
