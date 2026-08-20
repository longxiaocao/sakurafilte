#!/usr/bin/env python3
"""性能回归检测: 跑 aggregate(搜索) + typeahead 压测, 对照基线红线, 越线即非零退出 (CI 失败/告警)。

阈值取自 PERF_BASELINE_2026-08-20.md:
  - 搜索(aggregate, 生产主路径) P95: c1/c10/c50/c100 均 < 200ms (红线)
  - typeahead P95: 每字段 < 100ms (红线)

注意: 判定用 P95 (稳态), 非 max。backend 重启后首条请求偶发冷启动尖刺 (JIT/连接池/PG 计划缓存),
属瞬态非回归, 故先预热再测量, 且 max 超阈值仅作 warning 不致命。
(见 PERF_BASELINE_2026-08-20.md §5)

退出码: 0=达标, 1=存在 P95 越线 (CI 据此失败并触发 GitHub 告警)。
输出: spike-test/perf_regression_results.json + spike-test/perf_regression_report.md (CI step summary 用)

用法:
  python spike-test/perf_regression.py [--rounds 10] [--base http://localhost:55148]
"""
import json, subprocess, sys, os, argparse, urllib.request, time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(HERE)
AGG_RESULTS = os.path.join(HERE, "_bench_aggregate_results.json")
TA_RESULTS = os.path.join(HERE, "_bench_typeahead_results.json")

# 基线红线 (来自 PERF_BASELINE_2026-08-20.md)
SEARCH_P95_MAX = 200.0    # ms, 搜索红线
TYPEAHEAD_P95_MAX = 100.0  # ms, typeahead 红线
COLD_SPIKE_WARN = 500.0   # ms, max 超此值仅告警 (冷启动瞬态)

# typeahead 各字段取样 query (与 _bench_typeahead.py 一致)
TA_FIELDS = {
    "oem-brand": "bo", "oem-no2": "AB", "oem-no3": "TX", "machine-brand": "ka",
    "machine-model": "PC", "model-name": "ZX", "engine-brand": "ko", "engine-type": "D1",
}


def warmup(base):
    """预热: 每个端点先打一次废请求, 消除冷启动 (JIT/连接池/PG 计划缓存) 对测量的污染。"""
    print("预热端点 ...")
    # 1) aggregate
    try:
        urllib.request.urlopen(urllib.request.Request(
            base + "/api/public/search/aggregate",
            data=json.dumps({"page": 1, "pageSize": 20, "q": "oil", "tolerance": 5}).encode(),
            headers={"Content-Type": "application/json"}), timeout=30).read()
    except Exception as e:
        print(f"  [warn] aggregate 预热失败: {e}")
    # 2) 各 typeahead 字段
    for field, q in TA_FIELDS.items():
        try:
            urllib.request.urlopen(
                base + f"/api/public/typeahead/{field}?q={urllib.parse.quote(q)}&limit=20",
                timeout=30).read()
        except Exception as e:
            print(f"  [warn] typeahead {field} 预热失败: {e}")
    time.sleep(2)
    print("预热完成\n")


def run_aggregate(rounds, base):
    subprocess.run(
        [sys.executable, os.path.join(HERE, "_bench_aggregate.py"), "--rounds", str(rounds)],
        check=True, cwd=REPO_ROOT, env={**os.environ, "BASE_URL": base})
    return json.load(open(AGG_RESULTS, encoding="utf-8"))


def run_typeahead(rounds, base):
    # 不启用 check=True: 子进程在 max 越线时会退出非0, 但 JSON 已写出, 由本脚本统一判定
    subprocess.run(
        [sys.executable, os.path.join(HERE, "_bench_typeahead.py"), str(rounds),
         "--base", base, "--json", TA_RESULTS],
        check=False, cwd=REPO_ROOT)
    return json.load(open(TA_RESULTS, encoding="utf-8"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rounds", type=int, default=10)
    ap.add_argument("--base", default=os.environ.get("BASE_URL", "http://localhost:55148"))
    ap.add_argument("--no-warmup", action="store_true", help="跳过预热 (调试用)")
    args = ap.parse_args()

    if not args.no_warmup:
        warmup(args.base)

    violations = []
    warnings = []
    print(f"=== 性能回归检测 BASE={args.base} rounds={args.rounds} ===\n")

    # 1) aggregate 搜索
    agg = run_aggregate(args.rounds, args.base)
    print()
    for key, m in agg.get("concurrency", {}).items():
        p95 = m["p95_ms"]
        status = "OK" if p95 < SEARCH_P95_MAX else "FAIL"
        if p95 >= SEARCH_P95_MAX:
            violations.append(f"[search] {key} P95={p95}ms >= {SEARCH_P95_MAX}ms")
        print(f"  search {key}: P95={p95}ms P99={m['p99_ms']}ms errors={m['errors']} -> {status}")
        if m.get("errors", 0) > 0:
            violations.append(f"[search] {key} errors={m['errors']}")

    # 2) typeahead
    ta = run_typeahead(args.rounds, args.base)
    print()
    for field, m in ta.get("fields", {}).items():
        p95 = m["p95_ms"]
        mx = m["max_ms"]
        status = "OK" if p95 < TYPEAHEAD_P95_MAX else "FAIL"
        if p95 >= TYPEAHEAD_P95_MAX:
            violations.append(f"[typeahead] {field} P95={p95}ms >= {TYPEAHEAD_P95_MAX}ms")
        if mx >= COLD_SPIKE_WARN:
            warnings.append(f"[typeahead] {field} max={mx}ms (冷启动瞬态, 非致命)")
        print(f"  typeahead {field}: P95={p95}ms max={mx}ms -> {status}")

    # 汇总
    report = {"base": args.base, "rounds": args.rounds,
              "search_p95_max": SEARCH_P95_MAX, "typeahead_p95_max": TYPEAHEAD_P95_MAX,
              "violations": violations, "warnings": warnings,
              "passed": len(violations) == 0}
    json.dump(report, open(os.path.join(HERE, "perf_regression_results.json"), "w"), indent=1)

    md = ["# 性能回归检测", "",
          f"- BASE: `{args.base}`  轮数/档: {args.rounds}",
          f"- 搜索 P95 红线: < {SEARCH_P95_MAX}ms | typeahead P95 红线: < {TYPEAHEAD_P95_MAX}ms", "",
          "## 结果: " + ("✅ 通过" if not violations else f"❌ {len(violations)} 项越线"), ""]
    if violations:
        md.append("### 越线项 (致命)")
        for v in violations:
            md.append(f"- {v}")
    if warnings:
        md.append("### 告警 (非致命, 冷启动瞬态)")
        for w in warnings:
            md.append(f"- {w}")
    open(os.path.join(HERE, "perf_regression_report.md"), "w").write("\n".join(md) + "\n")

    if violations:
        print("\n❌ 性能回归:", len(violations), "项")
        for v in violations:
            print("  -", v)
        sys.exit(1)
    print("\n✅ 全部达标, 无性能回归" + (f" (含 {len(warnings)} 条冷启动告警)" if warnings else ""))


if __name__ == "__main__":
    main()
