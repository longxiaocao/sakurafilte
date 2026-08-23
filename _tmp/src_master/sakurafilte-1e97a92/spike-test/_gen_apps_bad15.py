"""生成 15 行坏数据测试 RecentErrorBuffer=10 的容量"""
out = r"d:\projects\sakurafilter\spike-test\output\test_recent_errors\apps_bad15.jsonl"
with open(out, "w", encoding="utf-8") as f:
    # 15 行坏 JSON / 缺字段
    for i in range(15):
        f.write(f'{{ this is bad json line {i}\n')
print(f"生成 {out}")
