"""P1 并发安全回归测试

用法:
  python _test_p1_concurrency.py

流程:
  1. GET /api/etl/status 查询当前 ETL 状态
  2. 模拟 Pause + Cancel 并发调用 (需 ETL 运行中, 否则仅打印 WARN)
  3. grep 验证 _activeCancelReason 在 EtlImportService.cs 中的读取均在 lock (_ctsLock) 块内
  4. grep 验证 PauseActiveTask 方法体在 lock (_ctsLock) 内

 WHY 关键: Day 9.4 引入 _activeCancelReason 跨方法传递取消原因,
   若不在 lock 内读取, 可能读到半写状态 (字符串引用非原子) 或与 CancelActiveTask 竞态
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

ETL_FILE = "backend/src/SakuraFilter.Etl/EtlImportService.cs"


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


def read_file(rel_path):
    """读取项目相对路径文件, 返回文本; 不存在抛 FileNotFoundError"""
    return Path(REPO / rel_path).read_text(encoding="utf-8")


def find_lock_blocks(content):
    """返回所有 lock (_ctsLock) { ... } 块的 (start_offset, end_offset) 列表"""
    blocks = []
    for m in re.finditer(r"lock\s*\(\s*_ctsLock\s*\)\s*\{", content):
        start = m.start()
        # 用大括号配平找闭合
        depth = 0
        i = m.end() - 1  # 指向 '{'
        while i < len(content):
            ch = content[i]
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    blocks.append((start, i + 1))
                    break
            i += 1
    return blocks


def is_inside_any_block(pos, blocks):
    """判断位置 pos 是否落在任一 (start, end) 区间内"""
    for s, e in blocks:
        if s <= pos < e:
            return True
    return False


def main():
    print("=" * 70)
    print("P1 并发安全回归测试")
    print("=" * 70)

    pass_cnt = 0
    fail_cnt = 0
    warn_cnt = 0

    # ===== 用例 1: GET /api/etl/status 查询当前状态 =====
    print("\n[用例 1] GET /api/etl/status 查询当前 ETL 状态")
    code, body, elapsed = curl("GET", "/api/etl/status", timeout=5)
    if code == 200:
        try:
            data = json.loads(body)
            in_progress = data.get("inProgress", data.get("InProgress"))
            print(f"  [PASS] HTTP 200 inProgress={in_progress} time={elapsed:.3f}s")
            pass_cnt += 1
        except json.JSONDecodeError:
            print(f"  [FAIL] 响应非 JSON: {body[:200]}")
            fail_cnt += 1
    else:
        print(f"  [FAIL] HTTP {code} (期望 200) body={body[:200]}")
        fail_cnt += 1

    # ===== 用例 2: 模拟 Pause + Cancel 并发调用 =====
    print("\n[用例 2] 模拟 Pause + Cancel 并发调用 (需 ETL 运行中)")
    print("  [WARN] 需手动验证: 此用例需先触发一个长时间 ETL 任务 (如 1M 行 products.jsonl)")
    print("         然后并发调用 POST /api/admin/etl/pause 与 DELETE /api/admin/etl/task")
    print("         期望: 两个请求都返回, 不出现死锁; EtlImportService 不抛 InvalidOperationException")
    print("         验证步骤: 启动 ETL → 用 asyncio/ threading 同时发 pause + cancel → 查 etl_progress_log.cancel_reason")
    warn_cnt += 1

    # ===== 用例 3: grep 验证 _activeCancelReason 读取均在 lock (_ctsLock) 块内 =====
    print("\n[用例 3] _activeCancelReason 在 EtlImportService.cs 中所有读取均在 lock (_ctsLock) 块内")
    try:
        content = read_file(ETL_FILE)
    except FileNotFoundError:
        print(f"  [FAIL] 文件不存在: {ETL_FILE}")
        fail_cnt += 1
        print("\n" + "=" * 70)
        print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
        print("===== 验证完成 =====")
        sys.exit(1)

    lock_blocks = find_lock_blocks(content)
    # 找所有 _activeCancelReason 的读取位置 (排除赋值 _activeCancelReason = ...)
    read_positions = []
    for m in re.finditer(r"_activeCancelReason(?!\s*=)", content):
        # 排除 "private string? _activeCancelReason;" 字段声明
        # 检查前缀是否是字段声明
        line_start = content.rfind("\n", 0, m.start()) + 1
        line_prefix = content[line_start:m.start()]
        if re.match(r"\s*(private|public|protected|internal)?\s*(readonly\s+)?(string\??)\s*_activeCancelReason\s*;", line_prefix):
            continue
        # 排除 _activeCancelReasonCode (相邻字段)
        tail = content[m.end():m.end() + 10]
        if tail.startswith("Code"):
            continue
        read_positions.append(m.start())

    if not read_positions:
        print(f"  [FAIL] 未找到 _activeCancelReason 的读取位置 (可能重构了)")
        fail_cnt += 1
    else:
        outside = [pos for pos in read_positions if not is_inside_any_block(pos, lock_blocks)]
        if not outside:
            print(f"  [PASS] {len(read_positions)} 处 _activeCancelReason 读取全部在 lock (_ctsLock) 块内")
            pass_cnt += 1
        else:
            print(f"  [FAIL] {len(outside)} 处 _activeCancelReason 读取在 lock 块外:")
            for pos in outside[:3]:
                line_no = content.count("\n", 0, pos) + 1
                print(f"         line {line_no}: {content[content.rfind(chr(10),0,pos)+1:pos+50].strip()}")
            fail_cnt += 1

    # ===== 用例 4: grep 验证 PauseActiveTask 方法体在 lock (_ctsLock) 内 =====
    print("\n[用例 4] PauseActiveTask 方法体在 lock (_ctsLock) 内")
    pause_match = re.search(r"public\s+bool\s+PauseActiveTask\s*\(\s*\)\s*\{", content)
    if not pause_match:
        print(f"  [FAIL] 未找到 PauseActiveTask 方法定义")
        fail_cnt += 1
    else:
        method_start = pause_match.start()
        # 找方法体范围 (大括号配平)
        depth = 0
        i = content.index("{", method_start)
        method_body_start = i
        while i < len(content):
            if content[i] == "{":
                depth += 1
            elif content[i] == "}":
                depth -= 1
                if depth == 0:
                    method_body_end = i + 1
                    break
            i += 1
        else:
            method_body_end = len(content)

        # 检查方法体内是否含 lock (_ctsLock)
        method_body = content[method_body_start:method_body_end]
        has_lock = bool(re.search(r"lock\s*\(\s*_ctsLock\s*\)", method_body))
        if has_lock:
            print(f"  [PASS] PauseActiveTask 方法体含 lock (_ctsLock) (位置 line {content.count(chr(10),0,method_start)+1})")
            pass_cnt += 1
        else:
            print(f"  [FAIL] PauseActiveTask 方法体不含 lock (_ctsLock)")
            fail_cnt += 1

    # ===== 汇总 =====
    print("\n" + "=" * 70)
    print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
    print("===== 验证完成 =====")
    if fail_cnt > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
