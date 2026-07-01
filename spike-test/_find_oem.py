"""找一些有 xrefs/apps 的产品"""
import json
from collections import Counter

c = Counter()
with open(r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl", encoding="utf-8") as f:
    for l in f:
        c[json.loads(l).get("product_oem")] += 1
top = c.most_common(5)
print("xrefs 出现最多的产品:")
for oem, n in top:
    print(f"  {oem}: {n}")

c2 = Counter()
with open(r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl", encoding="utf-8") as f:
    for l in f:
        c2[json.loads(l).get("product_oem")] += 1
top2 = c2.most_common(5)
print("\napps 出现最多的产品:")
for oem, n in top2:
    print(f"  {oem}: {n}")
