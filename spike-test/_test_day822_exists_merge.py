"""Day 8.2.2 专项测试: 6 个机器应用字段 + 2 个 xref 字段合并 EXISTS 行为正确性

测试范围:
  1) 机器应用 5 字段独立过滤: machineBrand/machineModel/modelName/engineBrand/engineType
     - 任一字段单独过滤, 应正确命中
     - 多字段组合 (AND), 应正确取交集
  2) xref 2 字段独立过滤: oemBrand / oem3Batch
     - 任一字段单独过滤, 应正确命中
     - 多字段组合 (AND), 应正确取交集
  3) 机器应用 + xref 跨表组合
     - 同时过滤机器应用和 xref, 应正确取交集
  4) countMode=none 性能模式: 6 字段全开, total=-1, 但 items 仍按过滤命中
"""
import requests
import psycopg2

API = 'http://localhost:5000'
PG = dict(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')

# ========== 准备数据 ==========
# 清理 (idempotent)
conn = psycopg2.connect(**PG); cur = conn.cursor()
cur.execute("DELETE FROM cross_references WHERE oem_no_3 LIKE 'DAY822E-%'")
cur.execute("DELETE FROM machine_applications WHERE machine_model LIKE 'DAY822E-%' OR machine_model LIKE 'X-CAT/%'")
cur.execute("DELETE FROM product_images WHERE product_id IN (SELECT id FROM products WHERE oem_no_display LIKE 'DAY822E-%')")
cur.execute("DELETE FROM product_history WHERE changed_fields::text LIKE '%DAY822E-%'")
cur.execute("DELETE FROM products WHERE oem_no_display LIKE 'DAY822E-%'")
conn.commit()
print('清理完成\n')

# 5 个产品覆盖 5 字段:
#   E1: CAT 320D + C6.4 引擎 (xref: BOSCH + MANN)
#   E2: CAT 330D + C9 (xref: BOSCH)
#   E3: KOMATSU PC200 + SAA6D102E (xref: DONALDSON)
#   E4: VOLVO EC360 + D6D (xref: MANN)
#   E5: PERKINS 4.236 (xref: 空)  - 用来排除
products = [
    {  # E1
        "oem2": "DAY822E-001", "productName1": "Filter E1", "type": "oil",
        "mr1": "DAY822E-MR-001", "isPublished": True, "d1Mm": 95.0,
        "qtyPerCarton": 12, "weightKgs": 10.0,
        "cartonLengthMm": 400, "cartonWidthMm": 300, "cartonHeightMm": 250,
        "crossReferences": [
            {"productName1": "Oil Filter", "oemBrand": "BOSCH", "oemNo3": "DAY822E-XREF-001"},
            {"productName1": "Oil Filter", "oemBrand": "MANN", "oemNo3": "DAY822E-XREF-002"}
        ],
        "machineApplications": [
            {"machineBrand": "CATERPILLAR", "machineModel": "X-CAT/320D",
             "modelName": "X-CAT/320D-EXCAVATOR",
             "engineBrand": "CATERPILLAR", "engineType": "X-CAT/C6.4"}
        ]
    },
    {  # E2
        "oem2": "DAY822E-002", "productName1": "Filter E2", "type": "fuel",
        "mr1": "DAY822E-MR-002", "isPublished": True, "d1Mm": 110.0,
        "qtyPerCarton": 12, "weightKgs": 14.0,
        "cartonLengthMm": 400, "cartonWidthMm": 300, "cartonHeightMm": 250,
        "crossReferences": [
            {"productName1": "Fuel Filter", "oemBrand": "BOSCH", "oemNo3": "DAY822E-XREF-003"}
        ],
        "machineApplications": [
            {"machineBrand": "CATERPILLAR", "machineModel": "X-CAT/330D",
             "modelName": "X-CAT/330D-EXCAVATOR",
             "engineBrand": "CATERPILLAR", "engineType": "X-CAT/C9"}
        ]
    },
    {  # E3
        "oem2": "DAY822E-003", "productName1": "Filter E3", "type": "oil",
        "mr1": "DAY822E-MR-003", "isPublished": True, "d1Mm": 80.0,
        "qtyPerCarton": 12, "weightKgs": 8.0,
        "cartonLengthMm": 400, "cartonWidthMm": 300, "cartonHeightMm": 250,
        "crossReferences": [
            {"productName1": "Oil Filter", "oemBrand": "DONALDSON", "oemNo3": "DAY822E-XREF-004"}
        ],
        "machineApplications": [
            {"machineBrand": "KOMATSU", "machineModel": "DAY822E-KOM/PC200",
             "modelName": "DAY822E-KOM/PC200-EXCAVATOR",
             "engineBrand": "KOMATSU", "engineType": "DAY822E-KOM/SAA6D102E"}
        ]
    },
    {  # E4
        "oem2": "DAY822E-004", "productName1": "Filter E4", "type": "air",
        "mr1": "DAY822E-MR-004", "isPublished": True, "d1Mm": 130.0,
        "qtyPerCarton": 6, "weightKgs": 18.0,
        "cartonLengthMm": 500, "cartonWidthMm": 400, "cartonHeightMm": 300,
        "crossReferences": [
            {"productName1": "Air Filter", "oemBrand": "MANN", "oemNo3": "DAY822E-XREF-005"}
        ],
        "machineApplications": [
            {"machineBrand": "VOLVO", "machineModel": "DAY822E-VOL/EC360",
             "modelName": "DAY822E-VOL/EC360-EXCAVATOR",
             "engineBrand": "VOLVO", "engineType": "DAY822E-VOL/D6D"}
        ]
    },
    {  # E5: 不同字段, 用来排除
        "oem2": "DAY822E-005", "productName1": "Filter E5", "type": "cabin",
        "mr1": "DAY822E-MR-005", "isPublished": True, "d1Mm": 75.0,
        "qtyPerCarton": 24, "weightKgs": 5.0,
        "cartonLengthMm": 350, "cartonWidthMm": 250, "cartonHeightMm": 200,
        "crossReferences": [],
        "machineApplications": [
            {"machineBrand": "PERKINS", "machineModel": "DAY822E-PER/4-236",
             "modelName": "DAY822E-PER/4-236-DIESEL",
             "engineBrand": "PERKINS", "engineType": "DAY822E-PER/4-236"}
        ]
    }
]

ids = []
for p in products:
    r = requests.post(f'{API}/api/admin/products', json=p)
    assert r.status_code == 201, f'创建失败 {p["oem2"]} {r.status_code} {r.text[:200]}'
    ids.append(r.json()['id'])
    print(f'  创建 {p["oem2"]} id={ids[-1]} type={p["type"]}')
print(f'\n5 个产品创建完成, ids={ids}\n')

# 用 mr1 前缀把所有测试产品拉出来 (避开老产品干扰)
# mr1 前缀 = DAY822E-MR-

def search(params, max_pages=20):
    """遍历所有页, 返回所有命中 id"""
    all_ids = []
    for p in range(1, max_pages + 1):
        r = requests.get(f'{API}/api/admin/products/search',
                         params={**params, 'mr1': 'DAY822E-MR-', 'page': p, 'pageSize': 50})
        data = r.json()
        all_ids.extend([i['id'] for i in data['items']])
        if len(data['items']) < 50 or len(all_ids) >= data['total']:
            break
    return all_ids

# ========== [1] 机器应用 5 字段独立过滤 ==========
print('[1] 机器应用 5 字段独立过滤 (合并 EXISTS 行为正确性)')

# 1a) machineBrand=CATERPILLAR → E1/E2
got = search({'machineBrand': 'CATERPILLAR'})
assert set(ids[0:2]) == set(got), f'machineBrand=CAT 应命中 E1/E2, 实际 {got}'
print(f'  machineBrand=CATERPILLAR: 命中 E1/E2 ✓')

# 1b) machineModel=DAY822E-KOM/PC200 → E3
got = search({'machineModel': 'DAY822E-KOM/PC200'})
assert ids[2] in got, f'machineModel 应命中 E3, 实际 {got}'
assert ids[0] not in got and ids[1] not in got, 'E1/E2 不应命中'
print(f'  machineModel=DAY822E-KOM/PC200: 命中 E3 ✓')

# 1c) modelName=DAY822E-VOL/EC360-EXCAVATOR → E4
got = search({'modelName': 'DAY822E-VOL/EC360-EXCAVATOR'})
assert ids[3] in got, f'modelName 应命中 E4, 实际 {got}'
assert ids[0] not in got, 'E1 不应命中'
print(f'  modelName=DAY822E-VOL/EC360-EXCAVATOR: 命中 E4 ✓')

# 1d) engineBrand=KOMATSU → E3
got = search({'engineBrand': 'KOMATSU'})
assert ids[2] in got, f'engineBrand=KOMATSU 应命中 E3, 实际 {got}'
assert ids[0] not in got, 'E1(CAT) 不应命中'
print(f'  engineBrand=KOMATSU: 命中 E3 ✓')

# 1e) engineType=DAY822E-VOL/D6D → E4
got = search({'engineType': 'DAY822E-VOL/D6D'})
assert ids[3] in got, f'engineType 应命中 E4, 实际 {got}'
print(f'  engineType=DAY822E-VOL/D6D: 命中 E4 ✓')

# ========== [2] 机器应用 5 字段组合 (AND) ==========
print('\n[2] 机器应用多字段组合 (合并 EXISTS + 短路求值)')

# 2a) machineBrand=CAT + machineModel=X-CAT/320D → 仅 E1
got = search({'machineBrand': 'CATERPILLAR', 'machineModel': 'X-CAT/320D'})
assert ids[0] in got and ids[1] not in got, f'CAT + 320D 应仅命中 E1, 实际 {got}'
print(f'  machineBrand=CAT + machineModel=X-CAT/320D: 命中 E1 ✓')

# 2b) engineBrand=CAT + engineType=X-CAT/C9 → 仅 E2
got = search({'engineBrand': 'CATERPILLAR', 'engineType': 'X-CAT/C9'})
assert ids[1] in got and ids[0] not in got, f'engine CAT + C9 应仅命中 E2, 实际 {got}'
print(f'  engineBrand=CAT + engineType=X-CAT/C9: 命中 E2 ✓')

# 2c) 3 字段组合: machineBrand + modelName + engineBrand 都 CAT → E1/E2
got = search({
    'machineBrand': 'CATERPILLAR',
    'modelName': 'X-CAT/320D-EXCAVATOR',  # 故意给 E1 专属的, 验证交集
    'engineBrand': 'CATERPILLAR'
})
assert ids[0] in got, f'3 字段交集应含 E1, 实际 {got}'
print(f'  3 字段交集 (machineBrand+modelName+engineBrand): 命中 E1 ✓')

# 2d) 5 字段全填不同值 → 0 命中 (验证 EXISTS 内部 AND 逻辑)
got = search({
    'machineBrand': 'CATERPILLAR',
    'machineModel': 'X-CAT/320D',
    'modelName': 'X-CAT/320D-EXCAVATOR',
    'engineBrand': 'CATERPILLAR',
    'engineType': 'X-CAT/C9'  # 这是 E2 的引擎, 不是 E1 的
})
assert all(i not in got for i in ids), f'5 字段不匹配应 0 命中, 实际 {got}'
print(f'  5 字段不匹配交集: 0 命中 ✓ (短路求值)')

# ========== [3] xref 2 字段组合 ==========
print('\n[3] xref 2 字段组合 (OemBrand + Oem3Batch 合并 EXISTS)')

# 3a) oemBrand=BOSCH → E1/E2
got = search({'oemBrand': 'BOSCH'})
assert ids[0] in got and ids[1] in got, f'oemBrand=BOSCH 应命中 E1/E2, 实际 {got}'
assert ids[2] not in got, 'E3(DONALDSON) 不应命中'
print(f'  oemBrand=BOSCH: 命中 E1/E2 ✓')

# 3b) oemBrand=BOSCH + oem3Batch=DAY822E-XREF-003 → 仅 E2 (E1 xref-003 没有)
got = search({'oemBrand': 'BOSCH', 'oem3Batch': 'DAY822E-XREF-003'})
assert ids[1] in got and ids[0] not in got, f'BOSCH + xref-003 应仅 E2, 实际 {got}'
print(f'  oemBrand=BOSCH + oem3Batch=xref-003: 命中 E2 ✓')

# 3c) oem3Batch=DAY822E-XREF-001 → 仅 E1
got = search({'oem3Batch': 'DAY822E-XREF-001'})
assert ids[0] in got and ids[1] not in got, f'oem3Batch=xref-001 应仅 E1, 实际 {got}'
print(f'  oem3Batch=DAY822E-XREF-001: 命中 E1 ✓')

# 3d) oemBrand=MANN + oem3Batch=DAY822E-XREF-002 → 仅 E1
got = search({'oemBrand': 'MANN', 'oem3Batch': 'DAY822E-XREF-002'})
assert ids[0] in got, f'MANN + xref-002 应含 E1, 实际 {got}'
assert ids[3] not in got, f'E4(MANN 也有, 但 xref-005 不是 002) 不应命中'
print(f'  oemBrand=MANN + oem3Batch=DAY822E-XREF-002: 命中 E1 ✓')

# ========== [4] 跨表组合 (machine + xref) ==========
print('\n[4] 跨表组合 (machine + xref)')

# 4a) machineBrand=CAT + oemBrand=BOSCH → E1/E2
got = search({'machineBrand': 'CATERPILLAR', 'oemBrand': 'BOSCH'})
assert set(ids[0:2]) == set(got), f'CAT + BOSCH 应命中 E1/E2, 实际 {got}'
print(f'  machineBrand=CAT + oemBrand=BOSCH: 命中 E1/E2 ✓')

# 4b) machineBrand=KOMATSU + oemBrand=MANN → 0 命中 (E3 是 DONALDSON, E4 是 MANN 但不是 KOMATSU)
got = search({'machineBrand': 'KOMATSU', 'oemBrand': 'MANN'})
assert all(i not in got for i in ids), f'KOMATSU + MANN 应 0 命中, 实际 {got}'
print(f'  machineBrand=KOMATSU + oemBrand=MANN: 0 命中 ✓ (跨表 AND)')

# 4c) machineBrand=CAT + oemBrand=MANN + oem3Batch=DAY822E-XREF-002 → 仅 E1
got = search({
    'machineBrand': 'CATERPILLAR',
    'oemBrand': 'MANN',
    'oem3Batch': 'DAY822E-XREF-002'
})
assert ids[0] in got, f'CAT + MANN + xref-002 应含 E1, 实际 {got}'
assert ids[1] not in got, 'E2 没有 MANN xref 不应命中'
print(f'  machineBrand=CAT + oemBrand=MANN + oem3Batch=DAY822E-XREF-002: 命中 E1 ✓')

# ========== [5] countMode=none 性能模式 ==========
print('\n[5] countMode=none 性能模式 (5 字段全开, total=-1)')

r = requests.get(f'{API}/api/admin/products/search', params={
    'mr1': 'DAY822E-MR-',
    'machineBrand': 'CATERPILLAR',
    'machineModel': 'X-CAT/320D',
    'modelName': 'X-CAT/320D-EXCAVATOR',
    'engineBrand': 'CATERPILLAR',
    'engineType': 'X-CAT/C6.4',
    'oemBrand': 'BOSCH',
    'oem3Batch': 'DAY822E-XREF-001',
    'countMode': 'none',
    'pageSize': 50
})
data = r.json()
assert data['total'] == -1, f'countMode=none 应 total=-1, 实际 {data["total"]}'
assert ids[0] in [i['id'] for i in data['items']], f'7 字段交集 + none 应命中 E1, 实际 items={[i["id"] for i in data["items"]]}'
print(f'  7 字段交集 (5 machine + 2 xref) + countMode=none: 命中 E1 ✓, total=-1 ✓')

# 清理
cur.execute("DELETE FROM cross_references WHERE oem_no_3 LIKE 'DAY822E-%'")
cur.execute("DELETE FROM machine_applications WHERE machine_model LIKE 'DAY822E-%' OR machine_model LIKE 'X-CAT/%'")
cur.execute("DELETE FROM product_images WHERE product_id IN (SELECT id FROM products WHERE oem_no_display LIKE 'DAY822E-%')")
cur.execute("DELETE FROM product_history WHERE changed_fields::text LIKE '%DAY822E-%'")
cur.execute("DELETE FROM products WHERE oem_no_display LIKE 'DAY822E-%'")
conn.commit()
print('\n========== Day 8.2.2 EXISTS 合并专项: 全部通过 ✓ ==========')
