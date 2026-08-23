"""检查 apps.jsonl 中 product_oem 的 prefix 分布"""
import json
from collections import Counter

with open(r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl", encoding="utf-8") as f:
    oems = [json.loads(l)["product_oem"] for l in f if l.strip()]

print(f"总 apps 行数: {len(oems)}")
print(f"唯一 product_oem: {len(set(oems))}")

# 按 prefix 分类
prefixes = Counter()
for o in oems:
    p = o[:2] if len(o) >= 2 else "?"
    prefixes[p] += 1
print(f"prefix 分布: {dict(prefixes)}")
print(f"前 10 个 OEM: {oems[:10]}")
