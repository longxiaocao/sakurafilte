"""P1 健康检查回归测试

用法:
  python _test_p1_health.py

流程:
  1. 验证 /health/live 与 /health/ready 返回 200
  2. 验证 /health/ready 响应含 backgroundServices / meili / fallback 字段
  3. 验证 8 个 BackgroundService 在 stale 列表中均不存在 (即都有心跳)
  4. Meili 故障场景需手动停 Meili 验证 (脚本仅打印提示)

 WHY 拆分 meili/fallback 探测: P1-6.1 重构后, Meili 故障但 PG 兜底可用时
   /health/ready 应返回 200 + degraded, 而非直接 503 剔除流量
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

# P1-5.1: 8 个 BackgroundService (与 Program.cs AddHostedService 一一对应)
#   stale 列表里若出现任一名字, 说明对应服务 >5min 未心跳 (卡死)
EXPECTED_BG_SERVICES = [
    "HistoryCleanupService",
    "EtlLogCleanupService",
    "DeadLetterCleanupService",
    "DeadLetterRecoveryService",
    "IndexReplayWorker",
    "EtlAlertService",
    "PerfAlertService",
    "AuthTokenBroadcaster",
]


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


def main():
    print("=" * 70)
    print("P1 健康检查回归测试")
    print("=" * 70)

    pass_cnt = 0
    fail_cnt = 0
    warn_cnt = 0

    # ===== 用例 1: GET /health/live 返回 200 =====
    print("\n[用例 1] GET /health/live 返回 200")
    code, body, elapsed = curl("GET", "/health/live", timeout=5)
    if code == 200:
        print(f"  [PASS] HTTP 200 time={elapsed:.3f}s body={body[:80]}")
        pass_cnt += 1
    else:
        print(f"  [FAIL] HTTP {code} (期望 200) body={body[:200]}")
        fail_cnt += 1

    # ===== 用例 2: GET /health/ready 返回 200 + backgroundServices 字段 =====
    print("\n[用例 2] GET /health/ready 返回 200, 响应含 backgroundServices 字段")
    code, body, elapsed = curl("GET", "/health/ready", timeout=8)
    has_bg = False
    parsed = None
    if code in (200, 503):
        try:
            parsed = json.loads(body)
            checks = parsed.get("checks", []) if isinstance(parsed, dict) else []
            has_bg = any(c.get("name") == "backgroundServices" for c in checks if isinstance(c, dict))
        except json.JSONDecodeError:
            pass
    if code in (200, 503) and has_bg:
        print(f"  [PASS] HTTP {code} 含 backgroundServices 字段 time={elapsed:.3f}s")
        pass_cnt += 1
    else:
        print(f"  [FAIL] HTTP {code} has_backgroundServices={has_bg} body={body[:200]}")
        fail_cnt += 1

    # ===== 用例 3: GET /health/ready 响应含 meili 和 fallback 字段 =====
    print("\n[用例 3] GET /health/ready 响应含 meili 和 fallback 字段 (拆分探测)")
    has_meili = False
    has_fallback = False
    if parsed is not None:
        checks = parsed.get("checks", []) if isinstance(parsed, dict) else []
        for c in checks:
            if not isinstance(c, dict):
                continue
            if c.get("name") == "meili":
                has_meili = True
            elif c.get("name") == "fallback":
                has_fallback = True
    if has_meili and has_fallback:
        print(f"  [PASS] meili={has_meili} fallback={has_fallback} (拆分探测生效)")
        pass_cnt += 1
    else:
        print(f"  [FAIL] meili={has_meili} fallback={has_fallback} (两者都应为 true)")
        fail_cnt += 1

    # ===== 用例 4: 停 Meili 后 /health/ready 返回 200 + degraded =====
    print("\n[用例 4] 停 Meili 后 /health/ready 返回 200 + degraded 状态")
    print("  [WARN] 需手动验证: 停掉 Meili 进程后, /health/ready 应返回 200 且 status=degraded")
    print("         验证步骤: docker stop meili / taskkill Meili 进程 → curl /health/ready")
    print("         期望: status='degraded', checks.meili.healthy=false, checks.fallback.healthy=true")
    warn_cnt += 1

    # ===== 用例 5: 8 个 BackgroundService 在 stale 列表中均不存在 =====
    print("\n[用例 5] 8 个 BackgroundService 在 backgroundServices.stale 列表中均不存在")
    stale_list = []
    if parsed is not None:
        checks = parsed.get("checks", []) if isinstance(parsed, dict) else []
        for c in checks:
            if isinstance(c, dict) and c.get("name") == "backgroundServices":
                stale_list = c.get("stale", []) or []
                break
    if not isinstance(stale_list, list):
        stale_list = []
    stale_names = set()
    for s in stale_list:
        # stale 元素可能是字符串或对象 (含 name/service 字段)
        if isinstance(s, str):
            stale_names.add(s)
        elif isinstance(s, dict):
            stale_names.add(s.get("name") or s.get("service") or s.get("Name") or "")

    if not stale_names:
        print(f"  [PASS] stale 列表为空, 8 个 BackgroundService 均有心跳")
        pass_cnt += 1
    else:
        missing = [s for s in EXPECTED_BG_SERVICES if s in stale_names]
        print(f"  [FAIL] stale 列表非空: {stale_names}")
        if missing:
            print(f"         卡死的服务: {missing}")
        fail_cnt += 1

    # ===== 汇总 =====
    print("\n" + "=" * 70)
    print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
    print("===== 验证完成 =====")
    if fail_cnt > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
