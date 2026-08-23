"""验证 cursor 翻页: 创建 5 个新产品, 走 cursor 翻 2 页验证"""
import requests
import psycopg2
import time

API = 'http://localhost:5000'
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

# 清理
conn = psycopg2.connect(**PG); cur = conn.cursor()
cur.execute("DELETE FROM products WHERE oem_no_display LIKE 'DAY82P2-%'")
conn.commit()

# 创建 5 个新产品
ids = []
for i in range(5):
    p = {
        "oem2": f"DAY82P2-{i:03d}",
        "productName1": f"Cursor Test {i}",
        "type": "oil",
        "mr1": f"DAY82P2-MR-{i:03d}",
        "isPublished": True,
        "d1Mm": 80.0 + i,
        "qtyPerCarton": 24,
        "cartonLengthMm": 400, "cartonWidthMm": 300, "cartonHeightMm": 250,
        "crossReferences": [],
        "machineApplications": []
    }
    r = requests.post(f'{API}/api/admin/products', json=p)
    assert r.status_code == 201, f'创建失败 {i}: {r.text}'
    ids.append(r.json()['id'])
    time.sleep(0.05)  # 让 updated_at 略不同
print(f'创建 5 个产品: ids={ids}')

# cursor 翻页: pageSize=2, 期望 3 页
print('\n[验证] cursor 翻页 3 页')
all_got = []
cursor = None
for page in range(1, 5):
    params = {'pagingMode': 'cursor', 'pageSize': 2, 'countMode': 'none', 'type': 'oil'}
    if cursor:
        params['cursor'] = cursor
    r = requests.get(f'{API}/api/admin/products/search', params=params)
    data = r.json()
    got = [it['id'] for it in data['items']]
    print(f'  page {page}: url={r.request.url}')
    print(f'  page {page}: got={got}, nextCursor={data["nextCursor"]}, hasMore={data["hasMore"]}')
    all_got.extend(got)
    cursor = data['nextCursor']
    if not cursor:
        break

# 验证 5 个产品全拿到 + 顺序对 (id DESC)
expected_desc = sorted(ids, reverse=True)
print(f'\n期望 id DESC: {expected_desc}')
print(f'实际: {all_got}')
assert all_got == expected_desc, f'顺序错: {all_got} vs {expected_desc}'
print('✓ cursor 翻页 id DESC 顺序正确, 5 个产品全拿到')

# 清理
cur.execute("DELETE FROM products WHERE oem_no_display LIKE 'DAY82P2-%'")
conn.commit()
print('清理完成')
