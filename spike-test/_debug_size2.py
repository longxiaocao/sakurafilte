"""Debug 多页 size"""
import requests
for p in [1, 2, 3, 4, 5]:
    r = requests.get('http://localhost:5148/api/admin/products/search', params={
        'd1Min': 80, 'd1Max': 100, 'sizeTolerance': 5, 'page': p, 'pageSize': 100, 'countMode': 'exact'
    })
    data = r.json()
    items = [i['id'] for i in data['items']]
    print(f'page {p}: total={data["total"]}, items[0:3]={items[:3]}, items[-3:]={items[-3:]}, count={len(items)}')
