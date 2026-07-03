"""模拟 CI 跑 _test_day96.py"""
import os
import subprocess
import sys
import time
import urllib.request

# 步骤 1: 启动 API (用 5148 端口, 如被占用会失败)
DB_NAME = f"spike_day96_{int(time.time())}"
PG_USER = "postgres"
PG_PWD = "784533"
PG_HOST = "localhost"
PG_PORT = 5432

print(f"[1] CREATE DATABASE {DB_NAME}")
r = subprocess.run(
    ["psql", "-h", PG_HOST, "-U", PG_USER, "-c", f"CREATE DATABASE {DB_NAME};"],
    env={**os.environ, "PGPASSWORD": PG_PWD},
    capture_output=True, text=True
)
print(r.stdout, r.stderr)

# 端口探测
def find_free_port(start):
    import socket
    p = start
    for _ in range(20):
        try:
            s = socket.socket()
            s.bind(("", p))
            s.close()
            return p
        except OSError:
            p += 1
    return start

api_port = find_free_port(5148)

try:
    print(f"\n[2] dotnet run on port {api_port}, DB={DB_NAME}")
    api_log = f"d:/projects/sakurafilter/spike-test/_ci_day96_{DB_NAME}.log"
    api_dir = "d:/projects/sakurafilter/backend/src/SakuraFilter.Api"
    env = {
        **os.environ,
        "ConnectionStrings__Postgres": f"Host={PG_HOST};Port={PG_PORT};Database={DB_NAME};Username={PG_USER};Password={PG_PWD}",
        "PGPASSWORD": PG_PWD,
        "Admin__DevStaticToken": "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C",
    }
    p = subprocess.Popen(
        ["dotnet", "run", "-c", "Release", "--no-build", "--urls", f"http://localhost:{api_port}"],
        cwd=api_dir, env=env,
        stdout=open(api_log, "w"), stderr=subprocess.STDOUT
    )
    print(f"API PID: {p.pid}")
    TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
    start = time.time()
    ready = False
    for i in range(60):
        if p.poll() is not None:
            print(f"[fail] API 退出, rc={p.returncode}")
            break
        try:
            req = urllib.request.Request(f"http://localhost:{api_port}/api/etl/status",
                headers={"X-Admin-Token": TOKEN})
            with urllib.request.urlopen(req, timeout=2) as r2:
                if r2.status == 200:
                    print(f"[ready] {i+1}s")
                    ready = True
                    break
        except Exception:
            pass
        time.sleep(1)
    if not ready:
        print("[fail] API not ready")
        with open(api_log, encoding='utf-8', errors='replace') as f:
            for line in f.readlines()[-30:]:
                print(line.rstrip())
    else:
        # 跑 _test_day96.py (本地版本)
        print(f"\n[3] 跑 _test_day96.py (BASE=http://localhost:{api_port})")
        env_test = {**env, "BASE_URL": f"http://localhost:{api_port}", "ADMIN_TOKEN": TOKEN}
        # 改 BASE 常量得在脚本里设环境变量 (脚本读的是硬编码 BASE)
        # 临时方案: 用 sed 替换
        r = subprocess.run(
            ["python", "_test_day96.py"],
            cwd="d:/projects/sakurafilter/spike-test",
            env=env_test,
            capture_output=True, text=True
        )
        print("--- stdout ---")
        print(r.stdout[-3000:])
        print("--- stderr ---")
        print(r.stderr[-1500:])
    p.terminate()
    try:
        p.wait(timeout=10)
    except subprocess.TimeoutExpired:
        p.kill()
finally:
    print(f"\n[cleanup] DROP DATABASE {DB_NAME}")
    r = subprocess.run(
        ["psql", "-h", PG_HOST, "-U", PG_USER, "-c", f"DROP DATABASE IF EXISTS {DB_NAME};"],
        env={**os.environ, "PGPASSWORD": PG_PWD},
        capture_output=True, text=True
    )
