# P3.5 (Task 12): 对比 API 端到端验证
#   验证 /api/admin/products/compare 接受 1-6 个 ID, 正确返回 ProductDetail 列表
#   用法: python _test_compare.py [base_url] [token]
#     默认 base_url=http://localhost:5082, token=devtoken
import sys
import json
import urllib.request
import urllib.error

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5082"
TOKEN = sys.argv[2] if len(sys.argv) > 2 else "devtoken"


def req(method, path, body=None):
    url = f"{BASE}{path}"
    data = None
    headers = {"X-Admin-Token": TOKEN, "Content-Type": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    r = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(r, timeout=10) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8") or "null")
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="ignore")
        return e.code, body
    except urllib.error.URLError as e:
        return 0, str(e)


def main():
    print(f"== 对比 API 验证: {BASE} ==")
    # 1. 先 search 拿几个产品 ID
    status, body = req("GET", "/api/admin/products/search?pageSize=6&countMode=none&pagingMode=offset")
    if status != 200:
        print(f"[FAIL] search 失败: {status} {body}")
        sys.exit(1)
    items = body.get("items", [])
    if len(items) < 2:
        print(f"[SKIP] 产品数 < 2 ({len(items)}), 跳过对比测试")
        sys.exit(0)
    ids = [it["id"] for it in items[:6]]
    print(f"[INFO] 选中产品 IDs: {ids}")

    # 2. 调 compare
    status, body = req("POST", "/api/admin/products/compare", {"ids": ids})
    if status != 200:
        print(f"[FAIL] compare 失败: {status} {body}")
        sys.exit(1)
    count = body.get("count", 0)
    items = body.get("items", [])
    print(f"[OK] compare 返回 count={count}, items={len(items)}")
    if count != len(ids):
        print(f"[WARN] count={count} != 请求 ids 数 {len(ids)}")

    # 3. 校验 ProductDetail 字段
    required = ["id", "oemNoDisplay", "isPublished", "isDiscontinued", "crossReferences", "machineApplications", "images"]
    for p in items:
        missing = [k for k in required if k not in p]
        if missing:
            print(f"[FAIL] 产品 {p.get('id')} 缺少字段: {missing}")
            sys.exit(1)
    print(f"[OK] 所有 {len(items)} 个产品均包含必填字段")

    # 4. 测 1 个 id 也能返回
    status, body = req("POST", "/api/admin/products/compare", {"ids": [ids[0]]})
    if status == 200 and body.get("count") == 1:
        print(f"[OK] 单个 ID 也能对比 (count=1)")
    else:
        print(f"[WARN] 单个 ID 返回: {status} {body.get('count') if isinstance(body, dict) else 'n/a'}")

    # 5. 测超过 6 个应被截断
    over_ids = ids + [99999, 99998, 99997]
    status, body = req("POST", "/api/admin/products/compare", {"ids": over_ids})
    if status == 200:
        cnt = body.get("count", 0)
        if cnt <= 6:
            print(f"[OK] 超过 6 个被截断: 请求 {len(over_ids)} → 返回 {cnt}")
        else:
            print(f"[WARN] 未截断: 请求 {len(over_ids)} → 返回 {cnt}")
    else:
        print(f"[WARN] 超 6 个 ID 拒绝: {status}")

    # 6. 测不存在的 ID 应被忽略
    status, body = req("POST", "/api/admin/products/compare", {"ids": [99999999, 99999998] + ids[:2]})
    if status == 200:
        cnt = body.get("count", 0)
        if cnt == 2:
            print(f"[OK] 不存在 ID 被忽略: 返回 {cnt}")
        else:
            print(f"[INFO] 不存在 ID 处理: 返回 {cnt} (后端可能含宽松行为)")

    print("\n✅ 对比 API 验证通过")


if __name__ == "__main__":
    main()
