"""P1 性能回归测试

用法:
  python _test_p1_perf.py

流程:
  1. GET /api/admin/products/{id} 详情接口耗时 < 300ms (需先查询一个产品 ID)
  2. grep 验证 AdminProductService.GetByIdAsync 含 Task.WhenAll (并行预签名 URL)
  3. grep 验证 AdminProductImageService.ListAsync 含 Task.WhenAll

 WHY 300ms 阈值: 详情接口含 6 张图预签名 URL 生成, 串行 600ms+,
   Task.WhenAll 并行后实测 <200ms, 300ms 是含网络抖动的安全上限
"""
import json
import re
import sys
import time
import urllib.request
import urllib.error
from pathlib import Path

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"Content-Type": "application/json", "X-Admin-Token": TOKEN}
REPO = Path(__file__).resolve().parent.parent

PERF_THRESHOLD_MS = 300


def curl(method, path, body=None, timeout=10, headers=None):
    """API 调用, 返回 (status_code, body, elapsed_sec)"""
    url = f"{BASE}{path}"
    h = headers or HEADERS
    data = json.dumps(body).encode("utf-8") if body else None
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    start = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read().decode("utf-8", errors="replace"), time.time() - start
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", errors="replace"), time.time() - start
    except Exception as e:
        return 0, str(e), time.time() - start


def grep_count(rel_path, pattern):
    """在文件中搜索正则, 返回匹配数; 文件不存在返回 -1"""
    try:
        content = Path(REPO / rel_path).read_text(encoding="utf-8")
        return len(re.findall(pattern, content))
    except FileNotFoundError:
        return -1


def main():
    print("=" * 70)
    print("P1 性能回归测试")
    print("=" * 70)

    pass_cnt = 0
    fail_cnt = 0
    warn_cnt = 0

    # ===== 用例 1: GET /api/admin/products/{id} 详情接口耗时 < 300ms =====
    print(f"\n[用例 1] GET /api/admin/products/{{id}} 详情接口耗时 < {PERF_THRESHOLD_MS}ms")
    # 1.1 先查一个产品 ID (list 接口取第一个)
    list_code, list_body, _ = curl("GET", "/api/admin/products?pageSize=1", timeout=8)
    product_id = None
    if list_code == 200:
        try:
            data = json.loads(list_body)
            items = data.get("items") or data.get("data") or []
            if items and isinstance(items, list):
                first = items[0]
                if isinstance(first, dict):
                    product_id = first.get("id") or first.get("Id")
        except json.JSONDecodeError:
            pass

    if product_id is None:
        print(f"  [WARN] 无法获取产品 ID (list 接口返回空?), 跳过耗时验证")
        print(f"         list_code={list_code} body={list_body[:200]}")
        warn_cnt += 1
    else:
        code, body, elapsed = curl("GET", f"/api/admin/products/{product_id}", timeout=8)
        elapsed_ms = elapsed * 1000
        if code == 200 and elapsed_ms < PERF_THRESHOLD_MS:
            print(f"  [PASS] id={product_id} HTTP 200 time={elapsed_ms:.1f}ms (阈值 {PERF_THRESHOLD_MS}ms)")
            pass_cnt += 1
        elif code == 200:
            print(f"  [FAIL] id={product_id} HTTP 200 time={elapsed_ms:.1f}ms 超过 {PERF_THRESHOLD_MS}ms 阈值")
            fail_cnt += 1
        else:
            print(f"  [FAIL] id={product_id} HTTP {code} (期望 200) body={body[:200]}")
            fail_cnt += 1

    # ===== 用例 2: grep 验证 AdminProductService.GetByIdAsync 含 Task.WhenAll =====
    print("\n[用例 2] AdminProductService.GetByIdAsync 含 Task.WhenAll (并行预签名 URL)")
    file_path = "backend/src/SakuraFilter.Api/Services/AdminProductService.cs"
    content_text = Path(REPO / file_path).read_text(encoding="utf-8") if Path(REPO / file_path).exists() else ""
    if not content_text:
        print(f"  [FAIL] 文件不存在: {file_path}")
        fail_cnt += 1
    else:
        # 定位 GetByIdAsync 方法体
        m = re.search(r"public\s+async\s+Task<\w+>\s+GetByIdAsync\s*\(", content_text)
        if not m:
            print(f"  [FAIL] 未找到 GetByIdAsync 方法定义")
            fail_cnt += 1
        else:
            # 取方法体 (从 { 到配平的 })
            brace_start = content_text.index("{", m.start())
            depth = 0
            i = brace_start
            while i < len(content_text):
                if content_text[i] == "{":
                    depth += 1
                elif content_text[i] == "}":
                    depth -= 1
                    if depth == 0:
                        break
                i += 1
            method_body = content_text[brace_start:i + 1]
            if "Task.WhenAll" in method_body:
                print(f"  [PASS] GetByIdAsync 方法体含 Task.WhenAll")
                pass_cnt += 1
            else:
                print(f"  [FAIL] GetByIdAsync 方法体不含 Task.WhenAll (P1-4.1 回归)")
                fail_cnt += 1

    # ===== 用例 3: grep 验证 AdminProductImageService.ListAsync 含 Task.WhenAll =====
    print("\n[用例 3] AdminProductImageService.ListAsync 含 Task.WhenAll")
    file_path = "backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs"
    content_text = Path(REPO / file_path).read_text(encoding="utf-8") if Path(REPO / file_path).exists() else ""
    if not content_text:
        print(f"  [FAIL] 文件不存在: {file_path}")
        fail_cnt += 1
    else:
        # v28-4 P0 修复: 正则 Task<\w+> 不匹配嵌套泛型 Task<List<ProductImageInfo>>
        #   \w+ 只匹配单个词 (不含 < >), 嵌套泛型返回类型 ListAsync FAIL
        #   修复: 用 .*? 非贪婪匹配返回类型, 同一行内到 ListAsync(
        m = re.search(r"public\s+async\s+Task.*?ListAsync\s*\(", content_text)
        if not m:
            print(f"  [FAIL] 未找到 ListAsync 方法定义")
            fail_cnt += 1
        else:
            brace_start = content_text.index("{", m.start())
            depth = 0
            i = brace_start
            while i < len(content_text):
                if content_text[i] == "{":
                    depth += 1
                elif content_text[i] == "}":
                    depth -= 1
                    if depth == 0:
                        break
                i += 1
            method_body = content_text[brace_start:i + 1]
            if "Task.WhenAll" in method_body:
                print(f"  [PASS] ListAsync 方法体含 Task.WhenAll")
                pass_cnt += 1
            else:
                print(f"  [FAIL] ListAsync 方法体不含 Task.WhenAll (P1-4.2 回归)")
                fail_cnt += 1

    # ===== 汇总 =====
    print("\n" + "=" * 70)
    print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
    print("===== 验证完成 =====")
    if fail_cnt > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
