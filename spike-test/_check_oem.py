"""检查 SO3335 是否在 xrefs/apps 中"""
import json

for name in ["SO3335", "SA42359"]:
    print(f"=== {name} ===")
    with open(r"d:\projects\sakurafilter\spike-test\output\cleaned\xrefs.jsonl", encoding="utf-8") as f:
        cnt = sum(1 for l in f if json.loads(l).get("product_oem") == name)
    print(f"  xrefs.jsonl: {cnt} 条")
    with open(r"d:\projects\sakurafilter\spike-test\output\cleaned\apps.jsonl", encoding="utf-8") as f:
        cnt = sum(1 for l in f if json.loads(l).get("product_oem") == name)
    print(f"  apps.jsonl: {cnt} 条")
