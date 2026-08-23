"""测试: CI 全新 DB, 不跑 baseline seed, 直接 EF Core Migrate 跑全"""
import os
import subprocess
import sys
import time

DB_NAME = f"spike_ef_only_{int(time.time())}"
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

try:
    # 步骤 2: 直接 dotnet run (EF Core Migrate 跑全)
    print(f"\n[2] dotnet run (EF Core Migrate 跑全, 无 baseline seed)")
    api_log = f"d:/projects/sakurafilter/spike-test/_ci_ef_only_{DB_NAME}.log"
    api_dir = "d:/projects/sakurafilter/backend/src/SakuraFilter.Api"
    env = {
        **os.environ,
        "ConnectionStrings__Postgres": f"Host={PG_HOST};Port={PG_PORT};Database={DB_NAME};Username={PG_USER};Password={PG_PWD}",
        "PGPASSWORD": PG_PWD,
    }
    p = subprocess.Popen(
        ["dotnet", "run", "-c", "Release", "--no-build", "--urls", "http://localhost:5199"],
        cwd=api_dir, env=env,
        stdout=open(api_log, "w"), stderr=subprocess.STDOUT
    )
    print(f"API PID: {p.pid}, log: {api_log}")
    start = time.time()
    ready = False
    for i in range(120):
        if p.poll() is not None:
            print(f"[fail] API 进程已退出, rc={p.returncode}")
            break
        try:
            import urllib.request
            req = urllib.request.Request("http://localhost:5199/api/etl/status",
                headers={"X-Admin-Token": "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"})
            with urllib.request.urlopen(req, timeout=2) as r2:
                if r2.status == 200:
                    print(f"[ready] API 在 {i+1}s 后可达")
                    ready = True
                    break
        except Exception:
            pass
        time.sleep(1)
    elapsed = time.time() - start
    print(f"\n[result] elapsed={elapsed:.1f}s, ready={ready}")
    if not ready:
        print("\n--- API 日志最后 50 行 ---")
        if os.path.exists(api_log):
            with open(api_log, encoding='utf-8', errors='replace') as f:
                lines = f.readlines()
                for line in lines[-50:]:
                    print(line.rstrip())
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
