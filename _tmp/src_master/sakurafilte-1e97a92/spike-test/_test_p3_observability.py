"""P3 可观测性回归测试

用法:
  python _test_p3_observability.py

流程:
  1. GET /metrics 返回 200 + text/plain
  2. GET /metrics 响应含 sakurafilter_meili_circuit_breaker
  3. GET /metrics 响应含 sakurafilter_etl_progress_read (依赖 /metrics 端点扩展)
  4. GET /metrics 响应含 sakurafilter_dead_letter_depth
  5. grep 验证 CorrelationIdMiddleware.cs 存在
  6. grep 验证 Program.cs 含 UseMiddleware<CorrelationIdMiddleware>()
  7. grep 验证 appsettings.json 含 IncludeScopes: true
  8. GET /api/etl/status 带 X-Correlation-Id header, 验证响应正常 (日志关联需手动检查)

 WHY 此测试: P3 系列覆盖可观测性三件套 (指标/日志/链路),
   /metrics 是 Prometheus 抓取入口, CorrelationId 是日志聚合关联键
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


def curl(method, path, body=None, timeout=10, headers=None):
    """API 调用, 返回 (status_code, body, elapsed_sec, content_type)"""
    url = f"{BASE}{path}"
    h = headers or HEADERS
    data = json.dumps(body).encode("utf-8") if body else None
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    start = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            ct = r.headers.get("Content-Type", "")
            return r.status, r.read().decode("utf-8", errors="replace"), time.time() - start, ct
    except urllib.error.HTTPError as e:
        ct = e.headers.get("Content-Type", "") if e.headers else ""
        return e.code, e.read().decode("utf-8", errors="replace"), time.time() - start, ct
    except Exception as e:
        return 0, str(e), time.time() - start, ""


def read_file(rel_path):
    """读取项目相对路径文件, 返回文本; 不存在返回空字符串"""
    p = Path(REPO / rel_path)
    if not p.exists():
        return ""
    return p.read_text(encoding="utf-8")


def main():
    print("=" * 70)
    print("P3 可观测性回归测试")
    print("=" * 70)

    pass_cnt = 0
    fail_cnt = 0
    warn_cnt = 0

    # ===== 用例 1: GET /metrics 返回 200 + text/plain =====
    print("\n[用例 1] GET /metrics 返回 200 + text/plain")
    code, body, elapsed, ct = curl("GET", "/metrics", timeout=8)
    if code == 200 and "text/plain" in ct.lower():
        print(f"  [PASS] HTTP 200 Content-Type={ct} time={elapsed:.3f}s")
        pass_cnt += 1
    else:
        print(f"  [FAIL] HTTP {code} Content-Type={ct} (期望 200 + text/plain)")
        fail_cnt += 1

    # ===== 用例 2: /metrics 含 sakurafilter_meili_circuit_breaker =====
    print("\n[用例 2] /metrics 响应含 sakurafilter_meili_circuit_breaker")
    if "sakurafilter_meili_circuit_breaker" in body:
        print(f"  [PASS] 找到 sakurafilter_meili_circuit_breaker 指标")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 sakurafilter_meili_circuit_breaker 指标")
        fail_cnt += 1

    # ===== 用例 3: /metrics 含 sakurafilter_etl_progress_read =====
    print("\n[用例 3] /metrics 响应含 sakurafilter_etl_progress_read (ETL 进度指标)")
    if "sakurafilter_etl_progress_read" in body:
        print(f"  [PASS] 找到 sakurafilter_etl_progress_read 指标")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 sakurafilter_etl_progress_read 指标 (Program.cs /metrics 端点未扩展?)")
        fail_cnt += 1

    # ===== 用例 4: /metrics 含 sakurafilter_dead_letter_depth =====
    print("\n[用例 4] /metrics 响应含 sakurafilter_dead_letter_depth (死信队列深度)")
    if "sakurafilter_dead_letter_depth" in body:
        print(f"  [PASS] 找到 sakurafilter_dead_letter_depth 指标")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 sakurafilter_dead_letter_depth 指标 (Program.cs /metrics 端点未扩展?)")
        fail_cnt += 1

    # ===== 用例 5: grep 验证 CorrelationIdMiddleware.cs 存在 =====
    print("\n[用例 5] backend/src/SakuraFilter.Api/Services/CorrelationIdMiddleware.cs 存在")
    file_path = "backend/src/SakuraFilter.Api/Services/CorrelationIdMiddleware.cs"
    p = Path(REPO / file_path)
    if p.exists():
        print(f"  [PASS] 文件存在 (size={p.stat().st_size} bytes)")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 文件不存在: {file_path}")
        fail_cnt += 1

    # ===== 用例 6: grep 验证 Program.cs 含 UseMiddleware<CorrelationIdMiddleware>() =====
    print("\n[用例 6] MiddlewarePipelineExtensions.cs 含 UseMiddleware<CorrelationIdMiddleware>()")
    # v28-4 P0 修复: 中间件管道代码实际在 MiddlewarePipelineExtensions.cs
    #   (Program.cs L38 调用 app.UseSakuraFilterMiddleware 扩展方法)
    content = read_file("backend/src/SakuraFilter.Api/Extensions/MiddlewarePipelineExtensions.cs")
    if re.search(r"UseMiddleware\s*<\s*CorrelationIdMiddleware\s*>", content):
        print(f"  [PASS] 找到 UseMiddleware<CorrelationIdMiddleware>()")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 UseMiddleware<CorrelationIdMiddleware>()")
        fail_cnt += 1

    # ===== 用例 7: grep 验证 appsettings.json 含 IncludeScopes: true =====
    print("\n[用例 7] appsettings.json 含 IncludeScopes: true (日志作用域启用)")
    content = read_file("backend/src/SakuraFilter.Api/appsettings.json")
    if re.search(r'"IncludeScopes"\s*:\s*true', content):
        print(f"  [PASS] appsettings.json: IncludeScopes=true")
        pass_cnt += 1
    else:
        print(f"  [FAIL] appsettings.json 未设置 IncludeScopes=true")
        fail_cnt += 1

    # ===== 用例 8: GET /api/etl/status 带 X-Correlation-Id header =====
    print("\n[用例 8] GET /api/etl/status 带 X-Correlation-Id: test-123 header")
    corr_headers = dict(HEADERS)
    corr_headers["X-Correlation-Id"] = "test-123"
    code, body, elapsed, _ = curl("GET", "/api/etl/status", timeout=5, headers=corr_headers)
    if code == 200:
        print(f"  [PASS] HTTP 200 (带 X-Correlation-Id 请求被接受) time={elapsed:.3f}s")
        print(f"  [WARN] 需手动验证: 后端日志应含 CorrelationId=test-123 (查 server 日志)")
        print(f"         验证步骤: grep 'CorrelationId=test-123' backend/logs/*.log 或控制台输出")
        pass_cnt += 1
        warn_cnt += 1
    else:
        print(f"  [FAIL] HTTP {code} (期望 200) body={body[:200]}")
        fail_cnt += 1

    # ===== 汇总 =====
    print("\n" + "=" * 70)
    print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
    print("===== 验证完成 =====")
    if fail_cnt > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
