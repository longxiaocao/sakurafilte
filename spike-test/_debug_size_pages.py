"""Debug size 翻页"""
import requests

all_ids = []
last_total = 0
for p in range(1, 11):
    r = requests.get('http://localhost:5148/api/admin/products/search', params={
        'd1Min': 80, 'd1Max': 100, 'sizeTolerance': 5, 'page': p, 'pageSize': 200
    })
    data = r.json()
    items_count = len(data['items'])
    all_ids.extend([i['id'] for i in data['items']])
    last_total = data['total']
    print(f'page {p}: got {items_count} items, total={last_total}, len(all_ids)={len(all_ids)}, hasMore={data["hasMore"]}')
    if items_count < 200 or len(all_ids) >= last_total:
        break

print()
print('A1 (11810) in result:', 11810 in all_ids)
print('A2 (11811) in result:', 11811 in all_ids)
print('B1 (11812) in result:', 11812 in all_ids)
print('C1 (11813) in result:', 11813 in all_ids)
print(f'\n共 {len(all_ids)} 条 (total={last_total})')
