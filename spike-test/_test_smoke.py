import requests, time
t0 = time.perf_counter()
try:
    r = requests.get('http://localhost:5000/api/admin/products/search', params={'pageSize': 1}, timeout=10)
    dt = time.perf_counter() - t0
    data = r.json()
    print(f'{dt:.2f}s status={r.status_code} total={data.get("total")} countModeUsed={data.get("countModeUsed")}')
except Exception as e:
    print(f'error: {e}')
