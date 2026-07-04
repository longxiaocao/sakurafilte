"""P3 可用性回归测试

用法:
  python _test_p3_availability.py

流程:
  1. grep 验证 ResilientSearchProvider 含 Initialize(bool primaryAvailable) 方法
  2. grep 验证 Program.cs 启动后探活 Meili 调用 Initialize(
  3. grep 验证 ResilientSearchProvider.cs 含 HttpRequestException catch 分支
  4. grep 验证 ResilientSearchProvider.cs 含 SocketException 处理
  5. grep 验证 ResilientSearchProvider 含 IsCircuitBreakerOpen 属性
  6. GET /health/ready 在 Meili 不可用时返回 200 + degraded (需手动停 Meili)
  7. 停 Meili 后首次搜索响应 < 100ms (需手动验证)

 WHY 此测试: P3-6.3 ResilientSearchProvider 是搜索可用性核心,
   Meili 故障时需立即降级到 PG 兜底, 不能等重试超时 (避免请求堆积)
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

RSP_FILE = "backend/src/SakuraFilter.Search/ResilientSearchProvider.cs"
PROGRAM_FILE = "backend/src/SakuraFilter.Api/Program.cs"


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


def read_file(rel_path):
    """读取项目相对路径文件, 返回文本; 不存在返回空字符串"""
    p = Path(REPO / rel_path)
    if not p.exists():
        return ""
    return p.read_text(encoding="utf-8")


def main():
    print("=" * 70)
    print("P3 可用性回归测试")
    print("=" * 70)

    pass_cnt = 0
    fail_cnt = 0
    warn_cnt = 0

    rsp_content = read_file(RSP_FILE)
    if not rsp_content:
        print(f"[FAIL] 无法读取 {RSP_FILE}")
        print("\n===== 验证完成 =====")
        sys.exit(1)

    program_content = read_file(PROGRAM_FILE)

    # ===== 用例 1: ResilientSearchProvider 含 Initialize(bool primaryAvailable) 方法 =====
    print("\n[用例 1] ResilientSearchProvider 含 Initialize(bool primaryAvailable) 方法")
    if re.search(r"public\s+void\s+Initialize\s*\(\s*bool\s+primaryAvailable\s*\)", rsp_content):
        print(f"  [PASS] 找到 Initialize(bool primaryAvailable) 方法定义")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 Initialize(bool primaryAvailable) 方法")
        print(f"         WHY: 启动后探活 Meili, 失败时设 _primaryAvailable=false 立即降级")
        fail_cnt += 1

    # ===== 用例 2: Program.cs 启动后探活 Meili 调用 Initialize( =====
    print("\n[用例 2] Program.cs 启动后探活 Meili 调用 Initialize(")
    if program_content and re.search(r"\bInitialize\s*\(", program_content):
        # 进一步确认是 rsp.Initialize( 形式
        if re.search(r"rsp\.Initialize\s*\(|\bInitialize\s*\(\s*meiliOk", program_content):
            print(f"  [PASS] 找到启动后 Initialize() 调用")
            pass_cnt += 1
        else:
            print(f"  [PASS] 找到 Initialize() 调用 (但形式可能不同)")
            pass_cnt += 1
    else:
        print(f"  [FAIL] 未在 Program.cs 中找到 Initialize() 调用")
        fail_cnt += 1

    # ===== 用例 3: ResilientSearchProvider.cs 含 HttpRequestException catch 分支 =====
    print("\n[用例 3] ResilientSearchProvider.cs 含 HttpRequestException catch 分支")
    if re.search(r"catch\s*\(\s*HttpRequestException", rsp_content):
        print(f"  [PASS] 找到 HttpRequestException catch 分支")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 HttpRequestException catch 分支")
        print(f"         WHY: Meili HTTP 请求异常 (DNS/连接拒绝) 需显式捕获并降级")
        fail_cnt += 1

    # ===== 用例 4: ResilientSearchProvider.cs 含 SocketException 处理 =====
    print("\n[用例 4] ResilientSearchProvider.cs 含 SocketException 处理")
    # SocketException 可能出现在 catch when 子句或 InnerException 检查
    if "SocketException" in rsp_content:
        print(f"  [PASS] 找到 SocketException 处理")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 SocketException 处理")
        print(f"         WHY: Meili 进程未启动时 HttpClient 抛 SocketException, 需立即降级不等重试")
        fail_cnt += 1

    # ===== 用例 5: ResilientSearchProvider 含 IsCircuitBreakerOpen 属性 =====
    print("\n[用例 5] ResilientSearchProvider 含 IsCircuitBreakerOpen 属性")
    if re.search(r"public\s+bool\s+IsCircuitBreakerOpen", rsp_content):
        print(f"  [PASS] 找到 IsCircuitBreakerOpen 属性")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 IsCircuitBreakerOpen 属性")
        print(f"         WHY: /metrics 端点读取此属性暴露熔断状态给 Prometheus")
        fail_cnt += 1

    # ===== 用例 6: GET /health/ready 在 Meili 不可用时返回 200 + degraded =====
    print("\n[用例 6] GET /health/ready 在 Meili 不可用时返回 200 + degraded")
    print("  [WARN] 需手动验证: 停掉 Meili 进程后, /health/ready 应返回 200 且 status=degraded")
    print("         验证步骤: docker stop meili → curl http://localhost:5148/health/ready")
    print("         期望: HTTP 200, body.status='degraded', checks.meili.healthy=false, checks.fallback.healthy=true")
    warn_cnt += 1

    # 当前 Meili 可用时的健康检查 (作为基线)
    code, body, elapsed = curl("GET", "/health/ready", timeout=8)
    if code in (200, 503):
        try:
            data = json.loads(body)
            status = data.get("status", "unknown")
            print(f"  [INFO] 当前 /health/ready 状态: HTTP {code} status={status} time={elapsed:.3f}s")
        except json.JSONDecodeError:
            print(f"  [INFO] 当前 /health/ready: HTTP {code} (响应非 JSON)")
    else:
        print(f"  [WARN] /health/ready 当前不可达: HTTP {code}")

    # ===== 用例 7: 停 Meili 后首次搜索响应 < 100ms =====
    print("\n[用例 7] 停 Meili 后首次搜索响应 < 100ms (立即降级不等重试)")
    print("  [WARN] 需手动验证: 停 Meili 后立即调用 /api/search?q=xxx, 应 <100ms 返回 PG 兜底结果")
    print("         验证步骤: docker stop meili → curl 'http://localhost:5148/api/search?q=test' -w '%{time_total}'")
    print("         期望: HTTP 200, time < 0.1s, 响应含 fallback 标记")
    warn_cnt += 1

    # ===== 汇总 =====
    print("\n" + "=" * 70)
    print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
    print("===== 验证完成 =====")
    if fail_cnt > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
