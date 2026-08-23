#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Meili 断点续传驱动脚本 (压测环境机器频繁冻结专用, 限流保护版)
=====================================================================
WHY 必要: 压测机 C 盘 SSD 不稳, 原生 /reindex-all 或一次性续传会在单个
       HTTP 请求里把 100 万文档不间断灌入 Meili, 持续高 I/O 直接把 SSD 拖崩。

本脚本配合后端 /reindex-resume?limit=N 使用:
  - 每次只触发一小段 (limit 条), 后端处理完即返回并释放锁, 不长时间占满 I/O;
  - 段与段之间强制休眠 PAUSE 秒, 让 SSD/Meili 有喘息窗口刷盘, 避免系统被拖垮;
  - 单次冻结只丢"当前进行中的 chunk", 恢复 (重启 Docker+栈) 后重跑即无缝接力。

循环逻辑:
  while Meili.count < TARGET:
      fromId = max(0, current_count - MARGIN)   # MARGIN 覆盖边界缺口
      POST /reindex-resume?fromId=fromId&limit=CHUNK
      轮询直到本段 isIndexing=False (chunk 提交完成)
      休眠 PAUSE 秒 (保护系统) -> 重算 count 进入下一轮
      unreachable -> 视为冻结, 退出 (环境恢复后重跑即可)

用法:
  python _resume_reindex.py --target 1000000 --margin 2000 --chunk 12000 --pause 20
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # F:/sakurafilter-perf
ENV_FILE = os.path.join(ROOT, ".env.perf")
BACKEND = "http://localhost:55148"
MEILI = "http://localhost:57700"
POLL_INTERVAL = 15          # 秒, 轮询 Meili 状态间隔
SEGMENT_START_TIMEOUT = 120 # 触发后多久内应开始 isIndexing, 否则视为失败重触发
MAX_IDLE_RETRIES = 3        # 连续无进展段数上限, 防死循环
LOG_PATH = os.path.join(ROOT, "_tmp", "reindex_resume.log")


def log(msg: str):
    line = f"[{time.strftime('%H:%M:%S')}] {msg}"
    print(line, flush=True)
    try:
        os.makedirs(os.path.dirname(LOG_PATH), exist_ok=True)
        with open(LOG_PATH, "a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception:
        pass


def load_env() -> dict:
    env = {}
    with open(ENV_FILE, encoding="utf-8") as f:
        for raw in f:
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            env[k] = v.strip().strip('"').strip("'")
    return env


def meili_get(path: str, key: str):
    req = urllib.request.Request(MEILI + path, headers={"Authorization": "Bearer " + key})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        return {"__error__": f"HTTP {e.code}"}
    except Exception as e:
        return {"__error__": str(e)}


def trigger_resume(from_id: int, admin_token: str, limit: int) -> str:
    url = f"{BACKEND}/api/admin/etl/reindex-resume?fromId={from_id}&limit={limit}"
    req = urllib.request.Request(url, method="POST",
                                 headers={"X-Admin-Token": admin_token,
                                          "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return f"triggered ({r.status}): {r.read().decode()[:160]}"
    except urllib.error.HTTPError as e:
        return f"HTTP {e.code}: {e.read().decode()[:160]}"
    except Exception as e:
        return f"ERR: {e}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", type=int, default=1_000_000)
    ap.add_argument("--margin", type=int, default=2000,
                    help="fromId 相对当前文档数回退的余量, 覆盖边界缺口")
    ap.add_argument("--chunk", type=int, default=12000,
                    help="每次触发只写这么多条 (limit), 写完即停, 保护 SSD")
    ap.add_argument("--pause", type=int, default=20,
                    help="每个 chunk 完成后强制休眠秒数, 让系统喘息")
    ap.add_argument("--max-segments", type=int, default=9999,
                    help="最多触发多少段 (防止本进程失控), 默认无限")
    args = ap.parse_args()

    env = load_env()
    key = env["MEILI_MASTER_KEY"]
    token = env.get("ADMIN_TOKEN", "")

    log(f"限流续传启动: target={args.target} margin={args.margin} "
        f"chunk={args.chunk} pause={args.pause}s")

    seg = 0
    consecutive_idle = 0
    while seg < args.max_segments:
        stats = meili_get("/indexes/products/stats", key)
        if "__error__" in stats:
            log(f"Meili 不可达 ({stats['__error__']}) -> 视为冻结, 退出。环境恢复后重跑本脚本即可续传。")
            sys.exit(0)
        count = stats.get("numberOfDocuments", 0)
        indexing = stats.get("isIndexing", False)
        log(f"当前 Meili 文档数={count} / 目标={args.target}  isIndexing={indexing}")

        if count >= args.target:
            log("已达成目标文档数, 续传完成。")
            break

        if indexing:
            log("仍在索引中, 等待本轮结束...")
            while True:
                time.sleep(POLL_INTERVAL)
                s = meili_get("/indexes/products/stats", key)
                if "__error__" in s:
                    log(f"Meili 不可达 ({s['__error__']}) -> 冻结, 退出。恢复后重跑续传。")
                    sys.exit(0)
                if not s.get("isIndexing", False):
                    break
                log(f"  索引中... count={s.get('numberOfDocuments')}")
            continue  # 重算 count 进入下一轮

        # 未索引中且未达标 -> 触发下一个限流 chunk
        from_id = max(0, count - args.margin)
        seg += 1
        log(f"触发第 {seg} 段续传: fromId={from_id} limit={args.chunk}")
        log("  " + trigger_resume(from_id, token, args.chunk))

        # 等待本段真正开始 (isIndexing 变 True) 或 count 增长
        started = False
        t0 = time.time()
        while time.time() - t0 < SEGMENT_START_TIMEOUT:
            time.sleep(POLL_INTERVAL)
            s = meili_get("/indexes/products/stats", key)
            if "__error__" in s:
                log(f"Meili 不可达 ({s['__error__']}) -> 冻结, 退出。恢复后重跑续传。")
                sys.exit(0)
            if s.get("isIndexing", False) or s.get("numberOfDocuments", 0) > count:
                started = True
                break
            log(f"  等待开始... count={s.get('numberOfDocuments')} indexing={s.get('isIndexing')}")
        if not started:
            log("  本段未在超时内开始 (可能端点/锁问题), 直接进入下一轮重试。")
            consecutive_idle += 1
            if consecutive_idle >= MAX_IDLE_RETRIES:
                log(f"连续 {MAX_IDLE_RETRIES} 段无进展, 疑似后端异常, 退出等待人工排查。")
                sys.exit(1)
            continue
        consecutive_idle = 0

        # 轮询直到本 chunk 结束 (isIndexing=False)
        while True:
            time.sleep(POLL_INTERVAL)
            s = meili_get("/indexes/products/stats", key)
            if "__error__" in s:
                log(f"Meili 不可达 ({s['__error__']}) -> 冻结, 退出。恢复后重跑续传。")
                sys.exit(0)
            if not s.get("isIndexing", False):
                new_count = s.get("numberOfDocuments", 0)
                log(f"第 {seg} 段结束, count={new_count} (本段 +{new_count - count})")
                break
            log(f"  第 {seg} 段索引中... count={s.get('numberOfDocuments')}")

        # 【关键保护】chunk 完成后强制休眠, 让 SSD/系统充分喘息, 避免持续 I/O 拖垮机器
        log(f"  保护性休眠 {args.pause}s, 让系统恢复...")
        time.sleep(args.pause)

    log("续传驱动退出。")


if __name__ == "__main__":
    main()
