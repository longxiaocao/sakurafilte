#!/usr/bin/env python3
# 限流分区键修复验证探针 (X-Forwarded-For 分桶)
#   WHY: 修复前限流用 RemoteIpAddress, 生产后置 LB 时全站共享一个桶 -> 集体 429。
#        修复后 GetClientIp 优先取 XFF 首段。本探针验证:
#          Test1: 两个不同 XFF 各发 250 请求 (均 < 300/min) -> 若按 XFF 分桶, 0 个 429
#          Test2: 同一 XFF 发 400 请求 (超 300) -> 应出现 ~100 个 429 (证明限流器生效)
#   判定: Test1 的 429 总数 == 0 且 Test2 的 429 > 0  => 修复生效 (PASS_XFF_KEYING)
import argparse, json, threading, urllib.request, urllib.error, collections


def send(base, path, xff):
    url = base.rstrip("/") + path
    data = json.dumps({"keyword": "CUMMINS"}).encode()
    req = urllib.request.Request(
        url, data=data,
        headers={"Content-Type": "application/json", "X-Forwarded-For": xff},
        method="POST")
    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code
    except Exception:
        return -1


def batch(base, path, xff, n, concurrency):
    codes = collections.Counter()
    lock = threading.Lock()

    def worker():
        c = send(base, path, xff)
        with lock:
            codes[c] += 1

    live = []
    for _ in range(n):
        t = threading.Thread(target=worker)
        t.start()
        live.append(t)
        if len(live) >= concurrency:
            live.pop(0).join()
    for t in live:
        t.join()
    return codes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://localhost:55148")
    ap.add_argument("--path", default="/api/search")
    ap.add_argument("--xf1", default="1.2.3.4")
    ap.add_argument("--xf2", default="5.6.7.8")
    ap.add_argument("--per-ip", type=int, default=250)
    ap.add_argument("--concurrency", type=int, default=50)
    ap.add_argument("--out")
    a = ap.parse_args()

    c1 = batch(a.base, a.path, a.xf1, a.per_ip, a.concurrency)
    c2 = batch(a.base, a.path, a.xf2, a.per_ip, a.concurrency)
    two_xff_429 = c1[429] + c2[429]
    c3 = batch(a.base, a.path, a.xf1, 400, a.concurrency)

    result = {
        "xf1_codes": dict(c1),
        "xf2_codes": dict(c2),
        "two_xff_total_429": two_xff_429,
        "single_xff_400_codes": dict(c3),
        "single_xff_429": c3[429],
        "interpretation": "PASS_XFF_KEYING" if (two_xff_429 == 0 and c3[429] > 0) else "FAIL",
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    main()
