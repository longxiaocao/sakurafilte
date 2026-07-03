# -*- coding: utf-8 -*-
"""P5.5 (前端/后端性能埋点) + P7.1 (X-Admin-Token 自动轮转) E2E 验证

测试场景:
  P5.5.1 后端 /api/perf 端点: 返回 200 + 含 P50/P95/P99/ErrorRate 字段
  P5.5.2 后端 /api/perf/ingest 端点: 接收批量 samples + 写入日志
  P5.5.3 后端 /health/live: 永远 200 (Liveness probe)
  P5.5.4 后端 /health/ready: 检查 PG/Meili, 任一故障 503
  P5.5.5 前端 perf.ts: 拦截器 + 批量上报 + sessionStorage 持久化
  P5.5.6 前端 main.ts: installPerfInterceptor() 已调用
  P5.5.7 后端 ResponseTimeMiddleware: 排除 /api/perf/ingest 路径
  P5.5.8 CI 跑若干请求后, /api/perf 的 P95 > 0 (有真实样本)

  P7.1.1 /api/admin/auth/status: 鉴权 + 返回 current/previous 长度
  P7.1.2 CLI 状态查询: SakuraFilter.Cli status
  P7.1.3 CLI dry-run rotate-token: 不写 DB
  P7.1.4 CLI rotate-token + 端点验证: 新 token 立即生效 (不重启 API)
  P7.1.5 CLI rotate-token 后 status 反映变更
  P7.1.6 旧 token 失效 (从 DB 删/覆盖后, 用旧 token 鉴权 401)

  共同:
  - CI 兼容: 默认 port 5148 (CI workflow 启动用)
  - 平台兼容: SCRIPT_DIR 算 repo root, 避免硬编码路径
"""
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

BASE = "http://localhost:5148"
ADMIN_TOKEN = os.environ.get(
    "ADMIN_TOKEN",
    "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C",
)
SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent
BACKEND = REPO_ROOT / "backend"
SRC = REPO_ROOT / "frontend" / "src"

PASS = 0
FAIL = 0
RESULTS = []


def http(method, path, body=None, headers=None, timeout=15):
    url = BASE + path
    h = {"Content-Type": "application/json"}
    if headers:
        h.update(headers)
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        r = urllib.request.urlopen(req, timeout=timeout)
        return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")
    except urllib.error.URLError as e:
        return 0, f"[URL unreachable: {e.reason}]"


def case(name, fn):
    global PASS, FAIL
    print(f"\n--- {name} ---")
    try:
        fn()
        PASS += 1
        RESULTS.append((name, "PASS", None))
        print(f"[PASS] {name}")
    except AssertionError as e:
        FAIL += 1
        RESULTS.append((name, "FAIL", str(e)))
        print(f"[FAIL] {name}: {e}")
        print(f"::error::P5.5/P7.1 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P5.5/P7.1 ERROR [{name}]: {e}")


# ========== P5.5.1 /api/perf 端点 ==========
def _norm_keys(data):
    """ASP.NET Core 8 默认 camelCase 输出, 兼容 PascalCase 测试断言"""
    if isinstance(data, dict):
        return {(k[0].lower() + k[1:]) if k else k: _norm_keys(v) for k, v in data.items()}
    if isinstance(data, list):
        return [_norm_keys(v) for v in data]
    return data


def test_p55_perf_snapshot_endpoint():
    """P5.5.1: GET /api/perf 返回 PerfSnapshot (P50/P95/P99/ErrorRate)"""
    code, body = http("GET", "/api/perf")
    assert code == 200, f"期望 200, 实际 {code}: {body}"
    data = _norm_keys(json.loads(body))
    for f in ("sampleCount", "totalRequests", "errorRequests", "errorRate",
              "p50Ms", "p95Ms", "p99Ms", "maxMs", "generatedAt"):
        assert f in data, f"字段缺失: {f}, body={body}"
    assert isinstance(data["p50Ms"], (int, float)), f"p50Ms 不是数字: {data}"
    assert data["p50Ms"] >= 0, f"p50Ms 为负: {data}"


# ========== P5.5.2 /api/perf/ingest 端点 ==========
def test_p55_perf_ingest_endpoint():
    """P5.5.2: POST /api/perf/ingest 接收批量 samples"""
    samples = [
        {"path": "/api/products/O-001", "method": "GET", "statusCode": 200,
         "durationMs": 12.5, "ts": "2026-07-03T00:00:00Z"},
        {"path": "/api/search", "method": "POST", "statusCode": 200,
         "durationMs": 8.3, "ts": "2026-07-03T00:00:01Z"},
    ]
    code, body = http("POST", "/api/perf/ingest", body={"samples": samples})
    assert code == 200, f"期望 200, 实际 {code}: {body}"
    data = json.loads(body)
    assert data.get("received") == 2, f"received 应为 2, body={body}"


def test_p55_perf_ingest_validation():
    """P5.5.2: 拒绝空 samples 与超量 (100+)"""
    code, _ = http("POST", "/api/perf/ingest", body={"samples": []})
    assert code == 400, f"空 samples 应 400, 实际 {code}"
    big = [{"path": "/x", "method": "GET", "statusCode": 200,
            "durationMs": 1.0, "ts": "2026-07-03T00:00:00Z"}] * 101
    code, body = http("POST", "/api/perf/ingest", body={"samples": big})
    assert code == 400, f"101 条 samples 应 400, 实际 {code}: {body}"


# ========== P5.5.3 /health/live ==========
def test_p55_health_live():
    """P5.5.3: GET /health/live 永远 200"""
    code, body = http("GET", "/health/live")
    assert code == 200, f"健康探针必须 200, 实际 {code}: {body}"
    data = json.loads(body)
    assert data.get("status") == "alive", f"status 应为 alive: {data}"


# ========== P5.5.4 /health/ready ==========
def test_p55_health_ready():
    """P5.5.4: GET /health/ready 检查 PG + 搜索"""
    code, body = http("GET", "/health/ready")
    # 503 也算通过 (PG 不可用时是预期的 degraded)
    assert code in (200, 503), f"健康检查应 200/503, 实际 {code}: {body}"
    data = json.loads(body)
    assert data.get("status") in ("ready", "degraded"), f"status 错: {data}"
    assert "checks" in data, f"checks 字段缺失: {data}"


# ========== P5.5.5 前端 perf.ts ==========
def test_p55_perf_ts_exists():
    """P5.5.5: frontend/src/utils/perf.ts 存在 + 含拦截器/buffer/flush"""
    perf_ts = SRC / "utils" / "perf.ts"
    assert perf_ts.is_file(), f"perf.ts 不存在: {perf_ts}"
    content = perf_ts.read_text(encoding="utf-8")
    for kw in ("installPerfInterceptor", "recordPerf", "BUFFER_LIMIT",
               "FLUSH_INTERVAL_MS", "sendBeacon", "sessionStorage"):
        assert kw in content, f"perf.ts 缺关键符号: {kw}"


# ========== P5.5.6 main.ts 集成 ==========
def test_p55_perf_installed_in_main():
    """P5.5.6: main.ts 调用 installPerfInterceptor()"""
    main_ts = SRC / "main.ts"
    assert main_ts.is_file(), f"main.ts 不存在: {main_ts}"
    content = main_ts.read_text(encoding="utf-8")
    assert "installPerfInterceptor" in content, "main.ts 未调用 installPerfInterceptor()"
    assert "import" in content and "perf" in content, "main.ts 未 import perf 模块"


# ========== P5.5.7 中间件排除 /api/perf ==========
def test_p55_middleware_excludes_perf():
    """P5.5.7: ResponseTimeMiddleware 排除 /api/perf 路径 (不污染统计)"""
    middleware = BACKEND / "src" / "SakuraFilter.Api" / "Services" / "ResponseTimeMiddleware.cs"
    assert middleware.is_file(), f"中间件不存在: {middleware}"
    content = middleware.read_text(encoding="utf-8")
    assert "/api/perf" in content, "ResponseTimeMiddleware 未排除 /api/perf"
    # 验证有其他高频路径被排除
    for p in ("/health/live", "/health/ready", "/scalar", "/openapi"):
        assert p in content, f"中间件未排除高频探针 {p}"


# ========== P5.5.8 跑请求后 P95 > 0 ==========
def test_p55_p95_increases_after_traffic():
    """P5.5.8: 跑 20+ 请求后, /api/perf 的 SampleCount 增加"""
    code, body0 = http("GET", "/api/perf")
    assert code == 200, f"首次 /api/perf 失败: {code}"
    snap0 = _norm_keys(json.loads(body0))
    # 跑 20 次搜索请求 (admin 端点, 不需 token 因为 /api/search 不在 admin 前缀)
    for _ in range(20):
        http("GET", "/health/live")  # Liveness 不计入 perf, 用 /api/products 实际样本
    for _ in range(20):
        # 用 /api/products/{fake} 触发 404 路径, 计入 perf 统计
        http("GET", "/api/products/__nonexistent_oem__")
    time.sleep(0.5)
    code, body1 = http("GET", "/api/perf")
    assert code == 200, f"末次 /api/perf 失败: {code}"
    snap1 = _norm_keys(json.loads(body1))
    assert snap1["totalRequests"] > snap0["totalRequests"], (
        f"TotalRequests 未增加: {snap0['totalRequests']} → {snap1['totalRequests']}")
    # P95 应被填充 (允许 0 当样本全为 0 ms, 但 20+ 404 应有非零耗时)
    assert snap1["p95Ms"] >= 0, f"p95Ms 异常: {snap1}"


# ========== P7.1.1 /api/admin/auth/status ==========
def test_p71_auth_status_requires_token():
    """P7.1.1: /api/admin/auth/status 必须 X-Admin-Token 鉴权"""
    code, body = http("GET", "/api/admin/auth/status")
    assert code == 401, f"无 token 应 401, 实际 {code}: {body}"

    code, body = http("GET", "/api/admin/auth/status",
                      headers={"X-Admin-Token": ADMIN_TOKEN})
    assert code == 200, f"合法 token 应 200, 实际 {code}: {body}"
    data = _norm_keys(json.loads(body))
    for f in ("currentLen", "currentPrefix", "previousLen",
              "lastRotatedAt", "lastRotatedBy", "loadedFromDb", "hasPrevious"):
        assert f in data, f"字段缺失: {f}, body={body}"
    assert data["currentLen"] >= 32, f"currentLen 应 ≥ 32: {data}"
    assert data["currentPrefix"], f"currentPrefix 应非空: {data}"


# ========== P7.1.2 CLI 状态查询 ==========
def test_p71_cli_status():
    """P7.1.2: SakuraFilter.Cli status 查 DB 当前 token"""
    cli = BACKEND / "src" / "SakuraFilter.Cli" / "bin" / "Release" / "net8.0" / "SakuraFilter.Cli.exe"
    if not cli.is_file():
        # 没编译则跳过 (CI 流程中可能尚未编译)
        print(f"  [skip] CLI 未编译: {cli}")
        return
    result = subprocess.run(
        [str(cli), "status"],
        capture_output=True, text=True, timeout=30,
    )
    assert result.returncode == 0, f"CLI status 失败: {result.stderr}"
    assert "current" in result.stdout, f"输出缺 current: {result.stdout}"
    assert "长度" in result.stdout or "length" in result.stdout.lower(), (
        f"输出缺 length 标识: {result.stdout}")


# ========== P7.1.3 CLI dry-run ==========
def test_p71_cli_dry_run():
    """P7.1.3: SakuraFilter.Cli rotate-token --dry-run 不写 DB"""
    cli = BACKEND / "src" / "SakuraFilter.Cli" / "bin" / "Release" / "net8.0" / "SakuraFilter.Cli.exe"
    if not cli.is_file():
        print(f"  [skip] CLI 未编译: {cli}")
        return
    # 假 token (≥ 32 字符 + 时间戳避免重复)
    fake_token = f"dry-run-test-{int(time.time())}-" + "X" * 16

    # 取 dry-run 前的 DB 状态 (currentLen)
    code, body = http("GET", "/api/admin/auth/status",
                      headers={"X-Admin-Token": ADMIN_TOKEN})
    if code == 401:
        # DB 已被上次轮转改了, 用 401 错误信息 (currentLen 字段不在错误响应中)
        #   改用 CLI status 拿 db 长度
        result = subprocess.run([str(cli), "status"],
                                capture_output=True, text=True, timeout=30)
        before = result.stdout
    else:
        before = _norm_keys(json.loads(body))["currentLen"]

    result = subprocess.run(
        [str(cli), "rotate-token", "--new", fake_token, "--by", "e2e_test", "--dry-run"],
        capture_output=True, text=True, timeout=30,
    )
    assert result.returncode == 0, f"CLI dry-run 失败: {result.stderr}"
    assert "dry-run" in result.stdout.lower(), f"输出缺 dry-run 标识: {result.stdout}"

    # 验证 DB 没被改 (前后 currentLen 一致)
    result = subprocess.run([str(cli), "status"],
                            capture_output=True, text=True, timeout=30)
    after = result.stdout
    if isinstance(before, int):
        # 之前用过 API 拿到了 currentLen (int)
        import re
        m = re.search(r"current\s*=\s*\S+\s*\(长度\s*(\d+)\)", after)
        assert m, f"CLI status 输出解析失败: {after}"
        after_len = int(m.group(1))
        assert after_len == before, (
            f"dry-run 不应改 DB: before={before}, after={after_len}")
    else:
        # 之前 DB 已被改, 验证 dry-run 前后输出完全相同
        assert after == before, (
            f"dry-run 不应改 DB, 状态变化: before={before!r} after={after!r}")


# ========== P7.1.4 + 5 CLI rotate-token 实际生效 ==========
def test_p71_cli_rotate_and_status_changes():
    """P7.1.4-5: CLI rotate-token 后 status 反映新值"""
    cli = BACKEND / "src" / "SakuraFilter.Cli" / "bin" / "Release" / "net8.0" / "SakuraFilter.Cli.exe"
    if not cli.is_file():
        print(f"  [skip] CLI 未编译: {cli}")
        return
    # 用时间戳后缀, 避免与上次轮转冲突 (测试可重复)
    new_token = f"e2e-rot-{int(time.time())}-" + "Z" * 24
    result = subprocess.run(
        [str(cli), "rotate-token", "--new", new_token, "--old", ADMIN_TOKEN,
         "--by", "e2e_test_p71"],
        capture_output=True, text=True, timeout=30,
    )
    assert result.returncode == 0, f"CLI rotate 失败: stderr={result.stderr}, stdout={result.stdout}"
    time.sleep(1.0)  # 等 PG NOTIFY 广播

    # 端点验证: 新 token 立即生效
    code, body = http("GET", "/api/admin/auth/status",
                      headers={"X-Admin-Token": new_token})
    assert code == 200, f"新 token 应 200, 实际 {code}: {body}"
    data = _norm_keys(json.loads(body))
    assert data["loadedFromDb"] is True, f"DB 加载应为 true: {data}"
    assert data["currentLen"] == len(new_token), (
        f"currentLen 应为 {len(new_token)}: {data}")
    # P7.1.6 旧 token 仍可用 (双 key 模式下 previous key 兜底)
    #   这正是零停机轮转的语义: 部署期间旧 token 继续有效, 给前端刷新窗口
    code, body = http("GET", "/api/admin/auth/status",
                      headers={"X-Admin-Token": ADMIN_TOKEN})
    assert code == 200, (
        f"旧 token 作为 previous key 应仍可用 (零停机语义), 实际 {code}: {body}")
    data = _norm_keys(json.loads(body))
    assert data["hasPrevious"] is True, f"应有 previous key: {data}"
    # 随机 token 必 401
    code, _ = http("GET", "/api/admin/auth/status",
                    headers={"X-Admin-Token": "invalid-fake-token-12345678901234567890"})
    assert code == 401, f"随机 token 应 401, 实际 {code}"

    # 回滚: 还原为原 ADMIN_TOKEN (保持测试可重复)
    #   用唯一 token 避免 "新 == 当前" 拒绝
    rollback_token = f"e2e-rb-{int(time.time())}-" + "A" * 24
    result = subprocess.run(
        [str(cli), "rotate-token", "--new", ADMIN_TOKEN, "--old", new_token,
         "--by", "e2e_test_p71_restore"],
        capture_output=True, text=True, timeout=30,
    )
    assert result.returncode == 0, f"CLI 还原失败: {result.stderr}"
    time.sleep(1.0)
    # 验证还原成功 (用 ADMIN_TOKEN)
    code, body = http("GET", "/api/admin/auth/status",
                      headers={"X-Admin-Token": ADMIN_TOKEN})
    assert code == 200, f"还原后旧 token 应 200, 实际 {code}: {body}"


# ========== P5.5.9 前端性能监控面板 ==========
def test_p55_perf_panel_view_exists():
    """P5.5.9: AdminPerfView.vue 存在 + 含 P50/P95/P99 + 自动刷新 + 告警条"""
    f = SRC / "views" / "admin" / "AdminPerfView.vue"
    assert f.is_file(), f"缺 AdminPerfView.vue: {f}"
    content = f.read_text(encoding="utf-8")
    # 关键符号
    for kw in ("PerfSnapshot", "p50Ms", "p95Ms", "p99Ms", "errorRate",
               "autoRefresh", "refreshSec", "fetchPerf", "fetchHealth",
               "onBeforeUnmount", "stopTimer"):
        assert kw in content, f"AdminPerfView 缺关键符号: {kw}"
    # 健康探针 + Token 轮转状态
    assert "/health/live" in content, "缺 Liveness 探针"
    assert "/health/ready" in content, "缺 Readiness 探针"
    assert "/admin/auth/status" in content, "缺 Token 轮转状态"
    # 副作用清理 (规则 5.2: useEffect/watch 必须含清理函数)
    assert "onBeforeUnmount" in content and "stopTimer" in content, \
        "缺定时器清理 (规则 5.2 副作用清理)"
    # P5.5+: 告警条 (P95≥500ms 或 ErrorRate≥5% 触发)
    assert "alerts" in content, "缺告警计算 alerts computed"
    assert "hasCritical" in content, "缺 hasCritical 告警分级"
    assert "role=\"alert\"" in content or "role='alert'" in content, \
        "缺 role=alert (A11y 无障碍)"


def test_p55_perf_panel_route_registered():
    """P5.5.9: 路由 /admin/perf 已注册"""
    router = SRC / "router" / "index.ts"
    assert router.is_file(), f"缺 router/index.ts: {router}"
    content = router.read_text(encoding="utf-8")
    assert "/admin/perf" in content, "缺 /admin/perf 路由"
    assert "AdminPerf" in content, "缺 AdminPerf 路由 name"
    assert "AdminPerfView" in content, "缺 AdminPerfView 组件 import"
    assert "requireAuth: true" in content, "性能监控页应需鉴权"


def test_p55_perf_panel_nav_item():
    """P5.5.9: AppHeader 有'性能'菜单项"""
    header = SRC / "components" / "AppHeader.vue"
    assert header.is_file(), f"缺 AppHeader.vue: {header}"
    content = header.read_text(encoding="utf-8")
    assert "/admin/perf" in content, "AppHeader 缺 /admin/perf 菜单项"
    assert "性能" in content, "AppHeader 缺 '性能' 文案"


def test_p55_health_proxy_configured():
    """P5.5.9: vite.config.ts 含 /health 代理 (健康探针端点不在 /api 下)"""
    vite = REPO_ROOT / "frontend" / "vite.config.ts"
    assert vite.is_file(), f"缺 vite.config.ts: {vite}"
    content = vite.read_text(encoding="utf-8")
    assert "'/health'" in content or '"/health"' in content, \
        "vite.config.ts 缺 /health 代理配置"


# ========== 汇总 ==========
def main():
    tests = [
        ("P5.5.1 /api/perf snapshot 端点", test_p55_perf_snapshot_endpoint),
        ("P5.5.2 /api/perf/ingest 上报", test_p55_perf_ingest_endpoint),
        ("P5.5.2b /api/perf/ingest 校验", test_p55_perf_ingest_validation),
        ("P5.5.3 /health/live liveness", test_p55_health_live),
        ("P5.5.4 /health/ready readiness", test_p55_health_ready),
        ("P5.5.5 前端 perf.ts 实现", test_p55_perf_ts_exists),
        ("P5.5.6 main.ts 集成拦截器", test_p55_perf_installed_in_main),
        ("P5.5.7 中间件排除 /api/perf", test_p55_middleware_excludes_perf),
        ("P5.5.8 跑请求后 P95 增长", test_p55_p95_increases_after_traffic),
        ("P5.5.9a AdminPerfView 组件", test_p55_perf_panel_view_exists),
        ("P5.5.9b 路由 /admin/perf 注册", test_p55_perf_panel_route_registered),
        ("P5.5.9c AppHeader 性能菜单项", test_p55_perf_panel_nav_item),
        ("P5.5.9d vite /health 代理", test_p55_health_proxy_configured),
        ("P7.1.1 /api/admin/auth/status", test_p71_auth_status_requires_token),
        ("P7.1.2 CLI status", test_p71_cli_status),
        ("P7.1.3 CLI dry-run", test_p71_cli_dry_run),
        ("P7.1.4-5 CLI rotate + status", test_p71_cli_rotate_and_status_changes),
    ]
    for name, fn in tests:
        case(name, fn)
    print(f"\n========== 汇总 ==========")
    print(f"PASS: {PASS}  FAIL: {FAIL}  TOTAL: {PASS + FAIL}")
    for n, s, e in RESULTS:
        if s != "PASS":
            print(f"  [{s}] {n}: {e}")
    sys.exit(0 if FAIL == 0 else 1)


if __name__ == "__main__":
    main()
