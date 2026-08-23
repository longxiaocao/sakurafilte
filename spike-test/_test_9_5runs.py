import requests, time

url = 'http://localhost:5000/api/admin/products/search'
super_heavy = {
    'countMode': 'exact', 'countTimeoutMs': 1,
    'pagingMode': 'cursor', 'pageSize': 1,
    'type': 'OIL FILTER', 'isPublished': 'true',
    'machineBrand': 'KOMATSU', 'machineModel': 'PC200',
    'modelName': 'EXCAVATOR', 'engineBrand': 'CATERPILLAR', 'engineType': 'C6.4',
    'oem3Batch': 'DAY82-XREF-001,DAY82-XREF-002,DAY82-XREF-003',
    'oemBrand': 'MANN',
    'd1Min': 50, 'd1Max': 200, 'sizeTolerance': 5,
    'h1Min': 50, 'h1Max': 300,
    'mediaName': 'Cell', 'productName1': 'Oil',
    'mr1': 'MR', 'oem2': 'OIL',
}

for i in range(5):
    t0 = time.perf_counter()
    try:
        r = requests.get(url, params=super_heavy, timeout=10)
        dt = time.perf_counter() - t0
        data = r.json()
        print(f'run {i+1}: dt={dt:.2f}s countModeUsed={data.get("countModeUsed")} total={data.get("total")}')
    except Exception as e:
        print(f'run {i+1}: ERROR {e}')
