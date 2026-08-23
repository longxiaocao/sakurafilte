"""测试 EtlProgress skipped 拆分 (Day 7.5)
准备 3 类 apps 数据:
1. 有效行 (brand+model 都有) — 期望 inserted
2. product_oem 不在 products 集 — 期望 skipped_missing_oem
3. brand/model 为 null — 期望 skipped_null_field
"""
import json
import asyncio
import subprocess
import time
import psycopg2
import urllib.request
from pathlib import Path

# 1) 准备测试数据
test_dir = Path(r"d:\projects\sakurafilter\spike-test\output\test_apps_split")
test_dir.mkdir(exist_ok=True)

# 从 products 取 1 个真实 OEM 作为有效行
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("SELECT oem_no_normalized FROM products LIMIT 1")
real_oem = cur.fetchone()[0]
print(f"使用真实 OEM: {real_oem}")

# 3 类测试行
test_rows = [
    # 1. 有效 (期望 inserted)
    {"product_oem": real_oem, "machine_brand": "TEST_A", "machine_model": "M1", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    {"product_oem": real_oem, "machine_brand": "TEST_A", "machine_model": "M2", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    # 2. product_oem 不存在 (期望 skipped_missing_oem)
    {"product_oem": "FAKEXXX", "machine_brand": "X", "machine_model": "Y", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    {"product_oem": "FAKEYYY", "machine_brand": "X", "machine_model": "Y", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    # 3. brand 为 null (期望 skipped_null_field)
    {"product_oem": real_oem, "machine_brand": None, "machine_model": "M3", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    # 4. model 为 null
    {"product_oem": real_oem, "machine_brand": "TEST_B", "machine_model": None, "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
]

test_path = test_dir / "apps_test.jsonl"
with open(test_path, "w", encoding="utf-8") as f:
    for r in test_rows:
        f.write(json.dumps(r, ensure_ascii=False) + "\n")
print(f"测试文件: {test_path} ({len(test_rows)} 行)")

# 2) 调用 apps ETL API
req = {"jsonlPath": str(test_path).replace("\\", "\\\\"), "mode": "upsert"}
req_path = test_dir / "req.json"
with open(req_path, "w", encoding="utf-8") as f:
    json.dump(req, f, ensure_ascii=False)

cmd = ["curl", "-s", "-X", "POST", "http://localhost:5148/api/etl/import-apps",
       "-H", "Content-Type: application/json", "-d", f"@{req_path}"]
print("触发 ETL:", " ".join(cmd))
result = subprocess.run(cmd, capture_output=True, text=True)
print("初始响应:", result.stdout[:200])

# 3) 等 5s 查 status
time.sleep(5)
status_cmd = ["curl", "-s", "http://localhost:5148/api/etl/status"]
status = json.loads(subprocess.run(status_cmd, capture_output=True, text=True).stdout)
print("\n=== 进度报告 ===")
for k in ['status', 'read', 'inserted', 'updated', 'skipped', 'skippedMissingOem', 'skippedNullField', 'errors', 'elapsedSec']:
    print(f"  {k}: {status.get(k)}")

# 4) 断言
assert status['read'] == 6, f"read 应=6, 实际 {status['read']}"
assert status['skippedMissingOem'] == 2, f"skippedMissingOem 应=2, 实际 {status['skippedMissingOem']}"
assert status['skippedNullField'] == 2, f"skippedNullField 应=2, 实际 {status['skippedNullField']}"
assert status['skipped'] == 4, f"skipped 应=4, 实际 {status['skipped']}"
# inserted: 1 个 (TEST_A/M1 新增) + 1 个 (TEST_A/M2 新增) = 2, 但 M1/M2 同 product/brand 不同 model
# 实际上 TEST_A/M1 和 TEST_A/M2 是新行,upsert 模式会插入 → 2 inserted
# 让我们宽松断言: inserted + updated = 2 (那 2 个有效行)
assert status['inserted'] + status['updated'] == 2, f"inserted+updated 应=2, 实际 {status['inserted']+status['updated']}"
print("\n✅ 所有断言通过")
conn.close()
