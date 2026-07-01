import requests
API = 'http://localhost:5000'
TOKEN = 'dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C'

# 35 个连发
ok = 0
limited = 0
for i in range(35):
    r = requests.post(f'{API}/api/admin/etl/trigger', json={
        'jsonlPath': r'd:\projects\sakurafilter\spike-test\output\synthetic_products_100k.jsonl',
        'mode': 'upsert', 'dryRun': True
    }, headers={'X-Admin-Token': TOKEN})
    if r.status_code == 200: ok += 1
    elif r.status_code == 429: limited += 1
    else: print(f'  {i+1}: {r.status_code} {r.text[:100]}')
print(f'ok={ok} limited={limited}')
