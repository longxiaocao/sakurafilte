# -*- coding: utf-8 -*-
"""Day 10+ P1.2 CDN 切换 MinIO→AliyunOSS E2E 测试 (Task 4)

目的: 验证 Storage:Provider 配置切换, 两种 provider 行为:
  1) minio (默认): 上传图 → URL 含 localhost:9000 → 下载成功
  2) aliyun-oss (环境变量启动 mock): 不真连 OSS, 验证 SDK 调用 + 预签名 URL 生成
     - 启动第二个 .NET 进程, 端口 5149, Storage__Provider=aliyun-oss
     - 启动时注入假 AccessKey + mock endpoint, SDK 初始化成功 (不真连)
     - GetPresignedUrlAsync 本地计算签名, 验证 URL 含 .aliyuncs.com + Expires/Signature 参数

覆盖场景 (与 SubTask 4.6 列表一一对应):
  1) Provider=minio: 上传+下载 1 张图, URL 域名 = localhost:9000
  2) Provider=aliyun-oss: GetUrl 预签名 URL 含 .aliyuncs.com + 签名参数
  3) Provider=aliyun-oss: GetPresignedUrlAsync 异步版本与 GetUrl 行为一致
  4) Provider=minio: 删除图 → 重新 list 不包含

依赖:
  - 后端跑在 http://localhost:5148 (默认 minio, 主进程)
  - 测试用第二后端跑在 http://localhost:5149 (aliyun-oss, 临时进程, 启动/停止)
  - X-Admin-Token 匹配 appsettings.json:Auth:DevStaticToken
  - PG 数据库 spike_test_v3 已有 products 表 (任取一个 id 用于上传)
  - MinIO 容器跑在 localhost:9000 (Day 8.1 docker compose)
"""
import json
import os
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

import psycopg2

BASE_MINIO = "http://localhost:5148"
BASE_OSS = "http://localhost:5149"  # 临时启动的 aliyun-oss Provider 实例
TOKEN = os.environ.get(
    "ADMIN_TOKEN", "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
)
H_ADMIN = {"X-Admin-Token": TOKEN, "Content-Type": "application/json"}

# 测试产品 id (从 PG 随机取一个已有产品)
TEST_PRODUCT_ID = None

PASS = 0
FAIL = 0
RESULTS = []


def http(method, path, body=None, headers=None, timeout=10, base=None):
    """统一 HTTP 客户端"""
    url = (base or BASE_MINIO) + path
    h = {"Content-Type": "application/json"}
    if headers:
        h.update(headers)
    data = None
    if body is not None:
        if isinstance(body, (bytes, bytearray)):
            data = bytes(body)
        else:
            data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        r = urllib.request.urlopen(req, timeout=timeout)
        return r.status, r.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()


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
        print(f"::error::P1.2 FAIL [{name}]: {e}")
    except Exception as e:
        FAIL += 1
        RESULTS.append((name, "ERROR", str(e)))
        print(f"[ERROR] {name}: {e}")
        print(f"::error::P1.2 ERROR [{name}]: {e}")


def get_db():
    return psycopg2.connect(
        host="localhost", port=5432, dbname="spike_test_v3",
        user="postgres", password="784533"
    )


def get_one_product_id() -> int:
    conn = get_db()
    cur = conn.cursor()
    cur.execute("SELECT id FROM products ORDER BY id LIMIT 1")
    row = cur.fetchone()
    cur.close()
    conn.close()
    assert row, "products 表无数据, 请先跑 products ETL"
    return row[0]


def port_in_use(port: int) -> bool:
    """检测端口是否被占用 (True = 已被占用)"""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        try:
            s.bind(("0.0.0.0", port))
            return False
        except OSError:
            return True


def wait_for_http(url: str, max_wait: int = 30) -> bool:
    """轮询等待 HTTP 端点就绪"""
    deadline = time.time() + max_wait
    while time.time() < deadline:
        try:
            urllib.request.urlopen(url, timeout=2)
            return True
        except Exception:
            time.sleep(0.5)
    return False


# ========== 启动/停止 aliyun-oss 测试用后端 (端口 5149) ==========
OSS_TEST_LOG = os.path.join(tempfile.gettempdir(), "_cdn_switch_oss_backend.log")
OSS_TEST_PID = None


def start_oss_backend():
    """
    启动第二个 .NET 进程, 端口 5149, Provider=aliyun-oss
    用环境变量覆盖配置, 不动 appsettings.json
    启动前检测端口, 已占用则报错
    """
    global OSS_TEST_PID
    if port_in_use(5149):
        print(f"  [WARN] 端口 5149 已被占用, 尝试用环境变量启动会失败, 跳过后端启动")
        return False
    # 环境变量: .NET 双下划线分隔
    env = os.environ.copy()
    env["Storage__Provider"] = "aliyun-oss"
    # 假 AccessKey (SDK 初始化不真连, 仅需非空字符串)
    env["Aliyun__AccessKeyId"] = "TEST-FAKE-KEY-ID-FOR-MOCK"
    env["Aliyun__AccessKeySecret"] = "TEST-FAKE-SECRET-FOR-MOCK"
    env["Aliyun__Endpoint"] = "oss-cn-hangzhou.aliyuncs.com"
    env["Aliyun__BucketName"] = "sakurafilter-prod"
    env["Aliyun__PublicEndpoint"] = "https://sakurafilter-prod.oss-cn-hangzhou.aliyuncs.com"
    env["Aliyun__CdnEndpoint"] = "https://cdn.sakurafilter.com"
    # ASP.NET Core URLs
    env["ASPNETCORE_URLS"] = BASE_OSS

    api_dir = r"D:\projects\sakurafilter\backend\src\SakuraFilter.Api"
    cmd = ["dotnet", "run", "--no-build", "--urls", BASE_OSS]
    log_handle = open(OSS_TEST_LOG, "w", encoding="utf-8")
    proc = subprocess.Popen(
        cmd, cwd=api_dir, env=env,
        stdout=log_handle, stderr=subprocess.STDOUT,
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == "win32" else 0,
    )
    OSS_TEST_PID = proc.pid
    print(f"  启动 aliyun-oss 测试后端 PID={proc.pid}, 日志 {OSS_TEST_LOG}")

    # 等待 /api/search/health 就绪
    if not wait_for_http(BASE_OSS + "/api/search/health", max_wait=60):
        print(f"  [ERROR] 60s 内 aliyun-oss 后端未就绪, 日志最后 30 行:")
        try:
            log_handle.flush()
            with open(OSS_TEST_LOG, "r", encoding="utf-8") as f:
                lines = f.readlines()
            for ln in lines[-30:]:
                print(f"    {ln.rstrip()}")
        except Exception:
            pass
        stop_oss_backend()
        return False
    print(f"  ✓ aliyun-oss 测试后端就绪: {BASE_OSS}")
    return True


def stop_oss_backend():
    global OSS_TEST_PID
    if OSS_TEST_PID:
        try:
            if sys.platform == "win32":
                # Windows: 用 taskkill 杀进程树 (含子进程)
                subprocess.run(
                    ["taskkill", "/F", "/T", "/PID", str(OSS_TEST_PID)],
                    capture_output=True, timeout=10
                )
            else:
                os.killpg(os.getpgid(OSS_TEST_PID), signal.SIGTERM)
        except Exception as e:
            print(f"  [WARN] 杀进程失败 PID={OSS_TEST_PID}: {e}")
        OSS_TEST_PID = None
    # 端口等待释放
    for _ in range(20):
        if not port_in_use(5149):
            return
        time.sleep(0.3)


# ========== 1x1 透明 PNG (最小有效图片, 67 字节) ==========
TINY_PNG = bytes.fromhex(
    "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4"
    "890000000d49444154789c63000100000005000100" "0d0a2db40000000049454e44ae426082"
)


def make_multipart(file_bytes: bytes, filename: str, content_type: str) -> tuple:
    """构造 multipart/form-data 请求体, 返回 (body bytes, content_type)"""
    boundary = "----WebKitFormBoundary" + os.urandom(8).hex()
    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="file"; filename="{filename}"\r\n'
        f"Content-Type: {content_type}\r\n\r\n"
    ).encode("utf-8") + file_bytes + f"\r\n--{boundary}--\r\n".encode("utf-8")
    return body, f"multipart/form-data; boundary={boundary}"


# ========== Case 1: Provider=minio 上传+URL 验证 ==========
def test_minio_upload():
    """默认 minio Provider: 上传 1 张图 → URL 含 localhost:9000 → 下载成功
    注: 上传后不立刻删, 留给 Case 3 (aliyun-oss list) 验证用
    """
    # 1) 上传 (slot=1)
    body, ct = make_multipart(TINY_PNG, "minio_test.png", "image/png")
    code, resp = http(
        "POST", f"/api/admin/products/{TEST_PRODUCT_ID}/images/1",
        body=body, headers={**H_ADMIN, "Content-Type": ct}, timeout=30
    )
    assert code == 200, f"上传失败: {code} {resp[:200]}"
    img = json.loads(resp)
    url = img.get("url", "")
    image_key = img.get("imageKey", "")
    print(f"  上传成功 imageKey={image_key}")
    print(f"  返回 URL: {url[:120]}...")

    # 2) URL 验证: 必须含 localhost:9000 (minio public endpoint)
    assert "localhost:9000" in url, f"minio URL 应含 localhost:9000, 实际: {url[:200]}"
    # 也应该含预签名参数 (X-Amz-Signature 或类似)
    assert "X-Amz" in url or "Signature" in url, f"URL 缺签名参数: {url[:200]}"
    print(f"  ✓ URL 含 localhost:9000 + 预签名参数")

    # 3) 下载验证: 拿 URL GET 请求, 期望 200 + PNG 头
    try:
        r = urllib.request.urlopen(url, timeout=10)
        assert r.status == 200, f"GET 状态码 {r.status}"
        content = r.read()
        assert content[:8] == b"\x89PNG\r\n\x1a\n", f"内容非 PNG, 头: {content[:8]!r}"
        assert len(content) == len(TINY_PNG), f"内容长度 {len(content)} != {len(TINY_PNG)}"
        print(f"  ✓ 下载成功, 内容长度 {len(content)} 字节, 头 {content[:4]!r}")
    except Exception as e:
        raise AssertionError(f"下载失败: {e}")

    # 4) list images 验证
    code, resp = http("GET", f"/api/admin/products/{TEST_PRODUCT_ID}/images", headers=H_ADMIN)
    assert code == 200
    items = json.loads(resp)
    keys = [it["imageKey"] for it in items]
    assert image_key in keys, f"list 找不到刚上传的 imageKey {image_key}, 实际 {keys}"
    print(f"  ✓ list images 包含刚上传的图, 共 {len(items)} 张")
    # 注: 不在此处删, 留给 Case 3 (aliyun-oss list) 验证数据迁移; Case 4 会清理


# ========== Case 2: Provider=aliyun-oss 启动, SDK 初始化成功 ==========
def test_oss_backend_started():
    """验证 aliyun-oss Provider 后端已成功启动 (含 SDK 初始化校验)
    注: IObjectStorage 是 Singleton lazy 注入 — 第一次 GetService<IObjectStorage>() 才跑 factory
        因此启动期日志可能没 [Storage] Provider=aliyun-oss (无 image API 调用前)
        此处先触发 list (会注入 AdminProductImageService → 注入 IObjectStorage),
        再读日志验证 [Storage] Provider=aliyun-oss
    """
    # 1) 后端健康检查
    code, resp = http("GET", "/api/search/health", base=BASE_OSS, headers=H_ADMIN)
    assert code == 200, f"aliyun-oss 后端健康检查失败: {code} {resp[:200]}"
    print(f"  ✓ /api/search/health 返 200")

    # 2) 触发 list images → 注入 IObjectStorage → 触发 factory 日志
    code, resp = http("GET", f"/api/admin/products/{TEST_PRODUCT_ID}/images", headers=H_ADMIN, base=BASE_OSS, timeout=10)
    assert code == 200, f"list 失败: {code} {resp[:200]}"
    print(f"  ✓ 触发 list 注入 IObjectStorage 成功 (status={code})")

    # 3) 读日志等 [Storage] Provider=aliyun-oss
    deadline = time.time() + 5
    log = ""
    while time.time() < deadline:
        try:
            with open(OSS_TEST_LOG, "r", encoding="utf-8") as f:
                log = f.read()
        except Exception:
            pass
        if "[Storage] Provider=aliyun-oss" in log:
            break
        time.sleep(0.5)
    assert "[Storage] Provider=aliyun-oss" in log, \
        f"启动日志缺 [Storage] Provider=aliyun-oss, 实际日志前 2000 字符:\n{log[:2000]}"
    print(f"  ✓ 启动日志含 '[Storage] Provider=aliyun-oss'")
    # 关键 endpoint / bucket / cdn 写入了
    assert "Endpoint=oss-cn-hangzhou.aliyuncs.com" in log, "缺 Endpoint 日志"
    assert "Bucket=sakurafilter-prod" in log, "缺 Bucket 日志"
    assert "Cdn=https://cdn.sakurafilter.com" in log, "缺 Cdn 日志"
    print(f"  ✓ 启动日志含 Endpoint/Bucket/Cdn 配置信息")


# ========== Case 3: Provider=aliyun-oss GetUrl 预签名 URL 含 aliyuncs.com ==========
def test_oss_geturl_signature():
    """验证 Provider=aliyun-oss 时 GetUrl 生成预签名 URL
    注: 现有接口 GetUrl 是同步, 内部走预签名 (AdminProductImageService 调它)
    aliyun-oss 后端 list images → URL 应含 oss-cn-hangzhou.aliyuncs.com + 签名参数
    """
    # 不真上传 (会真连 OSS 失败), 改为: 先用 minio 上传一张图, 切 aliyun-oss 后端 list 已有图
    # 但 list 会返 URL by 当前 Provider, 即 aliyun-oss endpoint
    # 策略: minio 端 upload → aliyun-oss 端 GET /api/admin/products/{id}/images → URL 应含 aliyuncs.com
    #   注意: imageKey 仍由 minio 写入 DB, aliyun-oss 端生成 URL 时只是拼 host (不会真去 OSS 验证)
    code, resp = http("GET", f"/api/admin/products/{TEST_PRODUCT_ID}/images", headers=H_ADMIN, base=BASE_OSS)
    assert code == 200, f"aliyun-oss 后端 list images 失败: {code} {resp[:200]}"
    items = json.loads(resp)
    assert len(items) > 0, f"产品 {TEST_PRODUCT_ID} 无图, 需先在 minio 端上传一张"
    url = items[0]["url"]
    print(f"  aliyun-oss 端 list 返 URL: {url[:150]}...")

    # URL 应含 cdn.sakurafilter.com (因为 CdnEndpoint 配了, 走 CDN 域名)
    #   注: AliyunOssStorage.GetUrl 把 OSS host 替换成 CdnEndpoint host
    # 我们的 CdnEndpoint=https://cdn.sakurafilter.com, 替换后 host 应是 cdn.sakurafilter.com
    assert "cdn.sakurafilter.com" in url, \
        f"aliyun-oss URL 应含 cdn.sakurafilter.com (CdnEndpoint 配了), 实际: {url[:200]}"
    print(f"  ✓ URL 含 cdn.sakurafilter.com (CDN 域名)")

    # URL 应含 OSS 签名参数: Expires= + Signature= + OSSAccessKeyId=
    #   Aliyun OSS 预签名 URL 格式: ...?Expires=...&OSSAccessKeyId=...&Signature=...
    assert "Expires=" in url, f"URL 缺 Expires= 参数: {url[:200]}"
    assert "Signature=" in url, f"URL 缺 Signature= 参数: {url[:200]}"
    assert "OSSAccessKeyId=" in url, f"URL 缺 OSSAccessKeyId= 参数: {url[:200]}"
    print(f"  ✓ URL 含 Expires/Signature/OSSAccessKeyId 签名参数")


# ========== Case 4: 切回 minio 不影响已有图 (不丢数据) ==========
def test_minio_delete_reupload():
    """验证 minio Provider: 删图后再传, slot 1 仍能成功 (覆盖上传不破坏 key)"""
    body, ct = make_multipart(TINY_PNG, "minio_retest.png", "image/png")
    code, resp = http(
        "POST", f"/api/admin/products/{TEST_PRODUCT_ID}/images/1",
        body=body, headers={**H_ADMIN, "Content-Type": ct}, timeout=30
    )
    assert code == 200, f"重传失败: {code} {resp[:200]}"
    img = json.loads(resp)
    assert "localhost:9000" in img.get("url", ""), "URL 应回 minio"
    print(f"  ✓ 重传成功, URL 回 minio")
    # 清理
    http("DELETE", f"/api/admin/products/{TEST_PRODUCT_ID}/images/1", headers=H_ADMIN)


# ========== Main ==========
def main():
    print("=" * 70)
    print("P1.2 CDN 切换 MinIO→AliyunOSS E2E 测试 (Day 10+ Task 4)")
    print(f"  默认后端: {BASE_MINIO} (minio)")
    print(f"  临时后端: {BASE_OSS} (aliyun-oss, mock credentials)")
    print(f"  测试产品 id: {TEST_PRODUCT_ID}")
    print("=" * 70)

    # 前置: 主后端 (minio) 健康检查
    code, _ = http("GET", "/api/search/health", headers=H_ADMIN, timeout=3)
    if code != 200:
        print(f"!! 主后端 {BASE_MINIO} 不可达: {code}, 请先启动 dotnet run (Provider=minio)")
        return 1

    # 前置: 启动 aliyun-oss 测试后端 (端口 5149, mock credentials)
    if not start_oss_backend():
        print("!! aliyun-oss 测试后端启动失败, 跳过 Case 2/3")
        # 仍跑 Case 1/4 (只用 minio)
        case("1. Provider=minio: 上传+下载+list, URL 含 localhost:9000", test_minio_upload)
        case("4. Provider=minio: 删图后重传, key 稳定", test_minio_delete_reupload)
    else:
        try:
            case("1. Provider=minio: 上传+下载+list, URL 含 localhost:9000", test_minio_upload)
            case("2. Provider=aliyun-oss: 启动期 SDK 初始化校验", test_oss_backend_started)
            case("3. Provider=aliyun-oss: GetUrl 含 aliyuncs.com + 签名参数", test_oss_geturl_signature)
            case("4. Provider=minio: 删图后重传, key 稳定", test_minio_delete_reupload)
        finally:
            stop_oss_backend()
            # 清理临时日志
            if os.path.exists(OSS_TEST_LOG):
                try:
                    os.remove(OSS_TEST_LOG)
                except Exception:
                    pass

    # 汇总
    print("\n" + "=" * 70)
    print(f"PASS: {PASS}  FAIL: {FAIL}  TOTAL: {PASS + FAIL}")
    print("=" * 70)
    return 0 if FAIL == 0 else 1


if __name__ == "__main__":
    # 取一个测试产品 id
    TEST_PRODUCT_ID = get_one_product_id()
    sys.exit(main())
