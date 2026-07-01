"""测试 recentErrors 环形缓冲"""
import json
import subprocess
import time
from pathlib import Path

# 构造 3 个解析错误行 + 1 个有效行
test_dir = Path(r"d:\projects\sakurafilter\spike-test\output\test_recent_errors")
test_dir.mkdir(exist_ok=True)
test_path = test_dir / "apps_bad.jsonl"

# 1 个完全无效的 JSON + 1 个缺关键字段 + 1 个 brand=null + 1 个有效
# Note: 完全无效 JSON 在 JsonSerializer.Deserialize 会抛 → catch 块 → IncrErrorsWith
bad_rows = [
    "{ this is not valid json",   # 解析错误
    '{"product_oem": "0H56617"}',  # 缺 machine_brand/machine_model → skippedNullField (不是 errors)
    '{"product_oem": null, "machine_brand": "X", "machine_model": "Y"}',  # null oem → skippedMissingOem
    "null",  # 完全 null → 解析错
    "{}",  # 空对象 → 缺 product_oem → skippedMissingOem
    "not even json",  # 解析错
]
with open(test_path, "w", encoding="utf-8") as f:
    for r in bad_rows:
        f.write(r + "\n")
print(f"测试文件: {test_path} ({len(bad_rows)} 行)")

# 调用 ETL
req = {"jsonlPath": str(test_path).replace("\\", "\\\\"), "mode": "insert-only"}
req_path = test_dir / "req.json"
with open(req_path, "w", encoding="utf-8") as f:
    json.dump(req, f, ensure_ascii=False)

subprocess.run(["curl", "-s", "-X", "POST", "http://localhost:5148/api/etl/import-apps",
                "-H", "Content-Type: application/json", "-d", f"@{req_path}"],
               capture_output=True, text=True)

time.sleep(3)
status = json.loads(subprocess.run(["curl", "-s", "http://localhost:5148/api/etl/status"], capture_output=True, text=True).stdout)
print("\n=== 进度报告 (期望 errors=3, recentErrors 含 3 条) ===")
for k in ['status', 'read', 'inserted', 'updated', 'skipped', 'skippedMissingOem', 'skippedNullField', 'skippedDuplicate', 'errors']:
    print(f"  {k}: {status.get(k)}")

recent = status.get('recentErrors', [])
print(f"\nrecentErrors ({len(recent)} 条):")
for e in recent:
    print(f"  [{e.get('at')}] {e.get('message')}")

# 断言: errors 应该至少 3 (3 行解析失败: invalid json, null, not even json)
assert status['errors'] >= 3, f"errors 应≥3, 实际 {status['errors']}"
assert len(recent) <= 5, f"recentErrors 容量应≤5, 实际 {len(recent)}"
assert len(recent) >= 3, f"recentErrors 应≥3, 实际 {len(recent)}"
# 每条 message 应包含 "apps 行 X:"
for e in recent:
    assert "apps 行" in e.get('message', ''), f"消息格式错误: {e}"
print("\n✅ recentErrors 环形缓冲测试通过")
