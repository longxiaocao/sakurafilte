"""在 spike_test_v3 跑 day10, 模拟 CI P4.1 内 case 5/7/8 失败"""
import os
import subprocess
import time
import urllib.request

# 直接连本地 dotnet, 用 spike_test_v3 db (跟 CI 一致)
DB_NAME = "spike_test_v3"
PG_USER = "postgres"
PG_PWD = "784533"
PG_HOST = "localhost"
PG_PORT = 5432

# 先 cleanup 残留 brand
print("[prep] cleanup 残留 test brand")
r = subprocess.run(
    ["psql", "-h", PG_HOST, "-U", PG_USER, "-d", DB_NAME, "-c",
     "DELETE FROM xref_oem_brand WHERE brand LIKE '_day10_test_%' OR brand LIKE '_escape_test_%'"],
    env={**os.environ, "PGPASSWORD": PG_PWD},
    capture_output=True, text=True
)
print(r.stdout, r.stderr)

api_port = 5148
try:
    # 启 dotnet
    print(f"\n[2] dotnet run on port {api_port}, DB={DB_NAME}")
    api_log = "d:/projects/sakurafilter/spike-test/_ci_spike3.log"
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
    TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
    ready = False
    for i in range(60):
        if p.poll() is not None: break
        try:
            req = urllib.request.Request(f"http://localhost:{api_port}/api/etl/status",
                headers={"X-Admin-Token": TOKEN})
            with urllib.request.urlopen(req, timeout=2) as r2:
                if r2.status == 200:
                    print(f"[ready] {i+1}s"); ready = True; break
        except Exception: pass
        time.sleep(1)
    if ready:
        env_test = {**env, "ADMIN_TOKEN": TOKEN, "BASE_URL": f"http://localhost:{api_port}"}
        # 跑 P4.1 前 2 个: escape + day10
        for s in ["_test_escape_underscore.py", "_test_day10_oem_brands.py"]:
            print(f"\n=== Running {s} ===")
            r = subprocess.run(
                ["python", s],
                cwd="d:/projects/sakurafilter/spike-test",
                env=env_test,
                capture_output=True, text=True
            )
            print(r.stdout[-1500:])
            if r.returncode != 0:
                print(f"[FAIL] {s} rc={r.returncode}")
    p.terminate()
    try: p.wait(timeout=10)
    except subprocess.TimeoutExpired: p.kill()
finally:
    print("\n[cleanup] 删残留 test brand")
    r = subprocess.run(
        ["psql", "-h", PG_HOST, "-U", PG_USER, "-d", DB_NAME, "-c",
         "DELETE FROM xref_oem_brand WHERE brand LIKE '_day10_test_%' OR brand LIKE '_escape_test_%'"],
        env={**os.environ, "PGPASSWORD": PG_PWD},
        capture_output=True, text=True
    )
    print(r.stdout)
