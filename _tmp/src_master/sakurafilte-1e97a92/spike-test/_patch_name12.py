"""
快速给现有 synthetic_products_1000k.jsonl 加 product_name_1/2 字段
WHY: 跑完整 generate_synthetic_data.py 重新生成 100 万条 + 10 万 xlsx 样本需 2-5 分钟,
     本脚本只读旧 jsonl, 给每行加 product_name_1/2, 输出新文件, 30 秒内完成.
     生成逻辑与 generate_synthetic_data.py gen_product() 一致 (同 SEED, 同分布).
"""
import json
import random
import time
from pathlib import Path

SEED = 42
random.seed(SEED)

NAME_VARIANTS = ["Standard", "Heavy Duty", "Premium", "Industrial", "Compact",
                 "High Flow", "Long Life", "Pro", "Eco", "Heavy"]

IN_PATH = Path(r"d:\projects\sakurafilter\spike-test\output\synthetic_products_1000k.jsonl")
OUT_PATH = Path(r"d:\projects\sakurafilter\spike-test\output\synthetic_products_1000k_v2.jsonl")

t0 = time.time()
n = 0
with open(IN_PATH, encoding='utf-8') as fin, open(OUT_PATH, 'w', encoding='utf-8') as fout:
    for line in fin:
        doc = json.loads(line)
        type_ = doc.get('type', '')
        # 与 generate_synthetic_data.gen_product 一致的分布
        r1 = random.random()
        if r1 < 0.70:
            doc['product_name_1'] = type_
        elif r1 < 0.90:
            doc['product_name_1'] = f"{type_} {random.choice(NAME_VARIANTS)}"
        else:
            doc['product_name_1'] = None
        doc['product_name_2'] = random.choice(NAME_VARIANTS) if random.random() < 0.25 else None
        # 字段顺序: 把 product_name_1/2 放到 type 后, product_name_3 前 (与 generate_synthetic_data.py 一致)
        ordered = {}
        for k in ['oem_no_display', 'oem_no_normalized', 'remark', 'product_name_1', 'product_name_2',
                  'product_name_3', 'type', 'd1_mm', 'd2_mm', 'd3_mm', 'h1_mm', 'h2_mm', 'h3_mm',
                  'd7_thread', 'd8_thread', 'media', 'sealing_material', 'efficiency_1',
                  'bypass_valve_lr', 'collapse_pressure_bar', 'temp_range', 'qty_per_carton',
                  'weight_kgs', 'carton_length_mm', 'carton_width_mm', 'carton_height_mm',
                  'image_key', 'image_status', 'is_discontinued']:
            if k in doc:
                ordered[k] = doc[k]
        # 兜底: 漏字段
        for k, v in doc.items():
            if k not in ordered:
                ordered[k] = v
        fout.write(json.dumps(ordered, ensure_ascii=False) + '\n')
        n += 1
        if n % 100_000 == 0:
            print(f"  已处理 {n:,} | 耗时 {time.time()-t0:.1f}s", flush=True)

elapsed = time.time() - t0
size_mb = OUT_PATH.stat().st_size / 1024 / 1024
print(f"\n[完成] {n:,} 行 | {elapsed:.1f}s | {size_mb:.1f} MB", flush=True)
print(f"输出: {OUT_PATH}", flush=True)
