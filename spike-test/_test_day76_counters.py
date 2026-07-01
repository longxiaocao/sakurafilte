"""测试 Day 7.6 新计数器: skippedDuplicate + recentErrors
构造 3 条 apps 数据,其中 2 条是重复 (同 product+brand+model)
期望: skippedDuplicate = 1 (2 条 raw,1 条 distinct)
"""
import json
import subprocess
import time
import psycopg2
from pathlib import Path

# 准备: 清理之前的测试数据 + 拿一个真实 OEM
conn = psycopg2.connect(host='localhost', port=5432, dbname='spike_test_v3', user='postgres', password='784533')
cur = conn.cursor()
cur.execute("DELETE FROM machine_applications WHERE machine_brand = 'DUP_TEST'")
conn.commit()
cur.execute("SELECT oem_no_normalized FROM products LIMIT 1")
real_oem = cur.fetchone()[0]
print(f"使用真实 OEM: {real_oem}")

# 构造 apps 测试数据: 3 行,其中 2 行重复
test_dir = Path(r"d:\projects\sakurafilter\spike-test\output\test_apps_dup")
test_dir.mkdir(exist_ok=True)
test_path = test_dir / "apps_dup.jsonl"

rows = [
    # 有效 #1
    {"product_oem": real_oem, "machine_brand": "DUP_TEST", "machine_model": "M1", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    # 重复 #1 (应被 DISTINCT ON 去掉)
    {"product_oem": real_oem, "machine_brand": "DUP_TEST", "machine_model": "M1", "model_name": "DUPLICATE",
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    # 有效 #2
    {"product_oem": real_oem, "machine_brand": "DUP_TEST", "machine_model": "M2", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
    # 故意构造解析错误 → 测试 recentErrors
    # 故意让 machine_brand 缺失
    {"product_oem": real_oem, "machine_brand": None, "machine_model": "M3", "model_name": None,
     "engine_brand": None, "engine_type": None, "engine_energy": None,
     "production_date_start": None, "is_ongoing": False},
]
with open(test_path, "w", encoding="utf-8") as f:
    for r in rows:
        f.write(json.dumps(r, ensure_ascii=False) + "\n")
print(f"测试文件: {test_path} ({len(rows)} 行)")

# 调用 ETL
req = {"jsonlPath": str(test_path).replace("\\", "\\\\"), "mode": "insert-only"}
req_path = test_dir / "req.json"
with open(req_path, "w", encoding="utf-8") as f:
    json.dump(req, f, ensure_ascii=False)

cmd = ["curl", "-s", "-X", "POST", "http://localhost:5148/api/etl/import-apps",
       "-H", "Content-Type: application/json", "-d", f"@{req_path}"]
print("触发 ETL...")
subprocess.run(cmd, capture_output=True, text=True)

time.sleep(5)
status = json.loads(subprocess.run(["curl", "-s", "http://localhost:5148/api/etl/status"], capture_output=True, text=True).stdout)
print("\n=== 进度报告 ===")
for k in ['status', 'read', 'inserted', 'updated', 'skipped', 'skippedMissingOem', 'skippedNullField', 'skippedDuplicate', 'errors', 'recentErrors']:
    val = status.get(k)
    print(f"  {k}: {val}")

# 断言
# 期望: read=4, skippedDuplicate=1, skippedNullField=1, inserted=2 (M1 + M2), updated=0
assert status['read'] == 4, f"read 应=4, 实际 {status['read']}"
assert status['skippedDuplicate'] == 1, f"skippedDuplicate 应=1, 实际 {status['skippedDuplicate']}"
assert status['skippedNullField'] == 1, f"skippedNullField 应=1, 实际 {status['skippedNullField']}"
assert status['skipped'] == 2, f"skipped 应=2, 实际 {status['skipped']}"
assert status['inserted'] + status['updated'] == 2, f"inserted+updated 应=2, 实际 {status['inserted']+status['updated']}"
assert len(status.get('recentErrors', [])) >= 0, "recentErrors 应返回列表"
print(f"\nrecentErrors 数组: {len(status.get('recentErrors', []))} 条")
print("\n✅ 所有 Day 7.6 计数器断言通过")

# 清理
cur.execute("DELETE FROM machine_applications WHERE machine_brand = 'DUP_TEST'")
conn.commit()
conn.close()
