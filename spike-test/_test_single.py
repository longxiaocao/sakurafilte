import requests, time
url = 'http://localhost:5000/api/admin/products/search'
p = {
    'countMode': 'exact', 'countTimeoutMs': 1,
    'pagingMode': 'cursor', 'pageSize': 1,
    'type': 'OIL FILTER', 'isPublished': 'true',
    'machineBrand': 'KOMATSU', 'oem3Batch': 'DAY82-XREF-001',
    'd1Min': 50, 'd1Max': 200, 'sizeTolerance': 5,
}
t0 = time.perf_counter()
try:
    r = requests.get(url, params=p, timeout=20)
    dt = time.perf_counter() - t0
    data = r.json()
    print(f'{dt:.2f}s status={r.status_code} total={data.get("total")} countMode={data.get("countMode")} countModeUsed={data.get("countModeUsed")}')
except Exception as e:
    print(f'ERROR after {time.perf_counter()-t0:.2f}s: {e}')
