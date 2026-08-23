import json
r = json.load(open('api_resilience_audit.json', encoding='utf-8'))
no_error = [f for f in r['findings'] if f['category']=='no_error_ui']
print('=== 无 error UI 的 API 调用 ===')
for f in no_error:
    print('  {}:{}  {}'.format(f['file'], f['line'], f['message']))
    print('    ' + f['snippet'])
