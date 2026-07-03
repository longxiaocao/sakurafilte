"""跑 P4.1 day10 部分 case 后不 cleanup, dump 状态"""
import os
import subprocess
import time
import urllib.request
import psycopg2

DB_NAME = f"spike_day10_dump_{int(time.time())}"
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
    api_log = f"d:/projects/sakurafilter/spike-test/_ci_day10_dump_{DB_NAME}.log"
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
    else:
        env_test = {**env, "ADMIN_TOKEN": TOKEN, "BASE_URL": f"http://localhost:{api_port}"}
        # 不跑 cleanup: 用 sed 替换 _test_day10_oem_brands.py 注释掉 cleanup
        # 改用更直接的方式: 跑一个简单脚本, 重复 case 5 流程
        import json
        H_ADMIN = {"X-Admin-Token": TOKEN, "Content-Type": "application/json"}
        def http(method, path, body=None, headers=None, timeout=5):
            h = {"Content-Type": "application/json"}
            if headers: h.update(headers)
            data = json.dumps(body).encode() if body is not None else None
            req = urllib.request.Request(f"http://localhost:{api_port}{path}",
                data=data, headers=h, method=method)
            try:
                with urllib.request.urlopen(req, timeout=timeout) as r2:
                    return r2.status, r2.read().decode()
            except urllib.error.HTTPError as e:
                return e.code, e.read().decode()

        # 模拟 case 3 + 4 + 5
        TAG = f"r{int(time.time())}"
        PREFIX = "_day10_test_"
        print(f"\n[3] 模拟 case 3+4+5, TAG={TAG}")

        # case 3: list1
        b1 = f"{PREFIX}{TAG}_list1"
        code, body = http("POST", "/api/admin/dict/oem-brands", body={"brand": b1}, headers=H_ADMIN)
        print(f"  create list1: code={code} body={body[:100]}")

        # case 3: delete list1
        code_l, body_l = http("GET", f"/api/admin/dict/oem-brands?q={b1}", headers=H_ADMIN)
        try:
            item_id = json.loads(body_l)["items"][0]["id"]
        except Exception as e:
            print(f"  list 失败: {e}, body={body_l[:200]}")
            raise
        code, body = http("DELETE", f"/api/admin/dict/oem-brands/{item_id}", headers=H_ADMIN)
        print(f"  delete list1: code={code}")

        # case 4: type1, type2
        b2_ = f"{PREFIX}{TAG}_type1"
        b3_ = f"{PREFIX}{TAG}_type2"
        for b in (b2_, b3_):
            code, body = http("POST", "/api/admin/dict/oem-brands", body={"brand": b}, headers=H_ADMIN)
            print(f"  create {b}: code={code} body={body[:100]}")
        # case 4: delete type1
        code_l, body_l = http("GET", f"/api/admin/dict/oem-brands?q={b2_}", headers=H_ADMIN)
        item_id2 = json.loads(body_l)["items"][0]["id"]
        code, body = http("DELETE", f"/api/admin/dict/oem-brands/{item_id2}", headers=H_ADMIN)
        print(f"  delete type1: code={code}")

        # case 5: dup1
        b4 = f"{PREFIX}{TAG}_dup1"
        code, body = http("POST", "/api/admin/dict/oem-brands", body={"brand": b4}, headers=H_ADMIN)
        print(f"  create dup1: code={code} body={body[:200]}")

        # case 5: 查 max
        conn = psycopg2.connect(host=PG_HOST, port=PG_PORT, dbname=DB_NAME, user=PG_USER, password=PG_PWD)
        cur = conn.cursor()
        cur.execute("SELECT COALESCE(MAX(sort_order), 0) FROM xref_oem_brand WHERE deleted_at IS NULL")
        max_so = cur.fetchone()[0]
        print(f"  max_so (deleted_at IS NULL) = {max_so}")
        cur.execute("SELECT id, brand, sort_order, deleted_at FROM xref_oem_brand ORDER BY id")
        print("  --- brands ---")
        for r in cur.fetchall():
            print(f"    {r}")
        conn.close()

        # case 5: b2 (sodo)
        b5 = f"{PREFIX}{TAG}_sodo"
        code, body = http("POST", "/api/admin/dict/oem-brands", body={"brand": b5}, headers=H_ADMIN)
        print(f"  create sodo: code={code} body={body[:200]}")

    p.terminate()
    try:
        p.wait(timeout=10)
    except subprocess.TimeoutExpired:
        p.kill()
    time.sleep(2)

    print(f"\n[final DB state]")
    conn = psycopg2.connect(host=PG_HOST, port=PG_PORT, dbname=DB_NAME, user=PG_USER, password=PG_PWD)
    cur = conn.cursor()
    cur.execute("SELECT id, brand, sort_order, deleted_at FROM xref_oem_brand ORDER BY id")
    for r in cur.fetchall():
        print(f"  {r}")
    conn.close()
finally:
    print(f"\n[cleanup] DROP DATABASE {DB_NAME}")
    r = subprocess.run(
        ["psql", "-h", PG_HOST, "-U", PG_USER, "-c", f"DROP DATABASE IF EXISTS {DB_NAME};"],
        env={**os.environ, "PGPASSWORD": PG_PWD},
        capture_output=True, text=True
    )
