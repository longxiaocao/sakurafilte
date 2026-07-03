"""复现 P4.1 orchestrator"""
import os
import subprocess
import time
import urllib.request

DB_NAME = f"spike_p41_{int(time.time())}"
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

api_port = 5148

try:
    print(f"\n[2] dotnet run on port {api_port}, DB={DB_NAME}")
    api_log = f"d:/projects/sakurafilter/spike-test/_ci_p41_{DB_NAME}.log"
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
        # 跑 P4.1 orchestrator 9 tests
        env_test = {**env, "ADMIN_TOKEN": TOKEN, "BASE_URL": f"http://localhost:{api_port}"}
        print(f"\n[3] P4.1 9 tests")
        suites = [
            "_test_escape_underscore.py",
            "_test_day10_oem_brands.py",
            "_test_p22_seven_dicts.py",
            "_test_type_ordering.py",
            "_test_batch_oem.py",
            "_test_public_product.py",
            "_test_public_search.py",
            "_test_compare.py",
            "_test_p5_polish.py",
        ]
        for s in suites:
            print(f"\n=== Running {s} ===")
            r = subprocess.run(
                ["python", s],
                cwd="d:/projects/sakurafilter/spike-test",
                env=env_test,
                capture_output=True, text=True
            )
            print("stdout:", r.stdout[-1500:])
            if r.returncode != 0:
                print("stderr:", r.stderr[-1000:])
                print(f"[FAIL] {s} rc={r.returncode}")
                break
            else:
                print(f"[PASS] {s}")
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
