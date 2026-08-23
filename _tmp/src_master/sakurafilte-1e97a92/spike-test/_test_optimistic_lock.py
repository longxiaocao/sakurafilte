#!/usr/bin/env python3
"""
E2E BD.3 乐观锁验证脚本 v2
  模拟两个管理员同时编辑同一产品的真实场景:
    1. 管理员 A GET 产品 → 获得 RowVersion (xmin=V1)
    2. 管理员 B GET 产品 → 获得 RowVersion (xmin=V1)
    3. 管理员 A PUT 产品 (带 RowVersion=V1) → 成功 (xmin 变为 V2)
    4. 管理员 B PUT 产品 (带 RowVersion=V1) → 失败 409 (因为 xmin 已是 V2)
"""
import json
import requests

BACKEND = "http://localhost:5148"
LOGIN = {"username": "admin", "password": "Admin@2026"}

# 1. 登录
r = requests.post(f"{BACKEND}/api/auth/login", json=LOGIN, timeout=5)
assert r.status_code == 200, f"登录失败: {r.status_code} {r.text}"
token = r.json()["accessToken"]
print(f"[OK] 登录成功, token={token[:20]}...")
H = {"Authorization": f"Bearer {token}", "Content-Type": "application/json"}

# 2. 获取产品列表 (取第一个产品)
r = requests.get(f"{BACKEND}/api/admin/products?limit=1", headers=H, timeout=10)
assert r.status_code == 200, f"获取产品列表失败: {r.status_code} {r.text}"
data = r.json()
items = data.get("items") or data.get("data", {}).get("items", [])
assert items, f"产品列表为空: {data}"
p = items[0]
pid = p["id"]
print(f"[OK] 选取产品 id={pid}, oem2={p.get('oem2')}")

# 3. 管理员 A 和 B 都 GET 详情 (获得同一个 RowVersion)
r = requests.get(f"{BACKEND}/api/admin/products/{pid}", headers=H, timeout=10)
assert r.status_code == 200, f"GET 详情失败: {r.status_code} {r.text}"
detail = r.json()
orig_rv = detail.get("rowVersion")
print(f"[OK] GET 详情, d1Mm={detail.get('d1Mm')}, rowVersion={orig_rv}")

if orig_rv is None:
    print("[FAIL] 详情未返回 rowVersion 字段, 后端修复未生效")
    exit(1)

# 4. 构造 PUT payload (模拟管理员 A 修改 d1Mm)
payload_a = dict(detail)
payload_a["d1Mm"] = (payload_a.get("d1Mm") or 0) + 0.01
payload_a["rowVersion"] = orig_rv  # 带 GET 时的 RowVersion

# 模拟管理员 B (用同一份原始数据, 修改不同字段)
payload_b = dict(detail)
payload_b["d2Mm"] = (payload_b.get("d2Mm") or 0) + 0.02
payload_b["rowVersion"] = orig_rv  # 也带原始 RowVersion (B 不知道 A 已修改)

# 移除后端只读字段
for k in ["createdAt", "updatedAt", "createdBy", "images", "id", "oemNoDisplay", "isDiscontinued", "volumePerCartonM3"]:
    payload_a.pop(k, None)
    payload_b.pop(k, None)

# 5. 管理员 A 先 PUT (应成功, xmin 从 V1 → V2)
r1 = requests.put(f"{BACKEND}/api/admin/products/{pid}", headers=H, json=payload_a, timeout=10)
print(f"\n[A] PUT status={r1.status_code}")
print(f"[A] body={r1.text[:200]}")

# 6. 管理员 B 后 PUT (应失败 409, 因为 RowVersion=V1 已过期)
r2 = requests.put(f"{BACKEND}/api/admin/products/{pid}", headers=H, json=payload_b, timeout=10)
print(f"\n[B] PUT status={r2.status_code}")
print(f"[B] body={r2.text[:300]}")

# 7. 验证
if r1.status_code == 200 and r2.status_code == 409:
    print("\n[PASS] 乐观锁生效: A=200 (修改成功), B=409 (检测到数据已被 A 修改)")
    print("       防止了 lost update (B 的修改不会覆盖 A 的修改)")
else:
    print(f"\n[FAIL] 期望 A=200, B=409, 实际 A={r1.status_code}, B={r2.status_code}")
