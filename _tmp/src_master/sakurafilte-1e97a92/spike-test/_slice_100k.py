"""截取 100K products + 对应 xref/apps (按 product_oem 关联)"""
import json

# 收集 100K products 的 oem_no_normalized
oem_norms = set()
with open('output/synthetic_products_1000k.jsonl', 'r', encoding='utf-8') as f:
    for i, line in enumerate(f):
        if i >= 100_000:
            break
        obj = json.loads(line)
        oem_norms.add(obj.get('oem_no_normalized', ''))

print(f'✓ 收集 100K oem_no_normalized: {len(oem_norms):,}')

# 截取 100K products
with open('output/synthetic_products_1000k.jsonl', 'r', encoding='utf-8') as f_in, \
     open('output/synthetic_products_100k.jsonl', 'w', encoding='utf-8') as f_out:
    for i, line in enumerate(f_in):
        if i >= 100_000:
            break
        f_out.write(line)
print(f'✓ products 100K → output/synthetic_products_100k.jsonl')

# 过滤 xref (按 product_oem)
xref_count = 0
with open('output/synthetic_xrefs_1000k.jsonl', 'r', encoding='utf-8') as f_in, \
     open('output/synthetic_xrefs_100k.jsonl', 'w', encoding='utf-8') as f_out:
    for line in f_in:
        obj = json.loads(line)
        if obj.get('product_oem', '') in oem_norms:
            f_out.write(line)
            xref_count += 1
print(f'✓ xref 过滤后: {xref_count:,} 条 → output/synthetic_xrefs_100k.jsonl')

# 过滤 apps (按 product_oem)
app_count = 0
with open('output/synthetic_apps_1000k.jsonl', 'r', encoding='utf-8') as f_in, \
     open('output/synthetic_apps_100k.jsonl', 'w', encoding='utf-8') as f_out:
    for line in f_in:
        obj = json.loads(line)
        if obj.get('product_oem', '') in oem_norms:
            f_out.write(line)
            app_count += 1
print(f'✓ apps 过滤后: {app_count:,} 条 → output/synthetic_apps_100k.jsonl')
