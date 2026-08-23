"""检查 products.jsonl 是否有重复 oem_no_normalized"""
import json
from collections import Counter

path = r"d:\projects\sakurafilter\spike-test\output\cleaned\products.jsonl"
counter = Counter()
with open(path, encoding="utf-8") as f:
    for line in f:
        d = json.loads(line)
        counter[d["oem_no_normalized"]] += 1

dups = {k: v for k, v in counter.items() if v > 1}
print(f"总行数: {sum(counter.values())}")
print(f"唯一 OEM: {len(counter)}")
print(f"重复 OEM: {len(dups)}")
if dups:
    print("前 10 个重复样例:")
    for k, v in list(dups.items())[:10]:
        print(f"  {k}: {v} 次")
