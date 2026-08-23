"""
100 万条合成数据生成器
- 读入 原 4,600 条样本
- 按真实分布膨胀到 1,000,000 products
- 每个产品附 5-20 个交叉引用 + 1-30 个机型适配
- 输出 xlsx (ETL 用) + jsonl (直载用) + 统计报告
"""
import pandas as pd
import json
import random
import string
import time
from pathlib import Path
from collections import Counter

SEED = 42
random.seed(SEED)

SRC = Path(r"d:\projects\sakurafilter\新思路.xlsx")
OUT_DIR = Path(r"d:\projects\sakurafilter\spike-test\output")
OUT_DIR.mkdir(parents=True, exist_ok=True)

# 真实工业滤芯 type 集合（基于样本 + Sakura 实际品类）
TYPES = [
    "AIR FILTER", "OIL FILTER", "FUEL FILTER", "CABIN AIR FILTER",
    "HYDRAULIC FILTER", "PETROL FILTER", "AIR/OIL SEPARATOR",
    "ACTIVATED CARBON FILTER", "WATER SEPARATOR", "COOLANT FILTER",
    "SPIN-ON FILTER", "CARTRIDGE FILTER", "INDUSTRIAL FILTER",
    "AIR DRYER", "OIL SEPARATOR", "DIESEL FILTER", "MARINE FILTER",
    "TURBO FILTER", "EXHAUST FILTER", "BREATHER FILTER",
    "POWER STEERING FILTER", "TRANSMISSION FILTER", "UREA FILTER"
]
TYPE_WEIGHTS = [25, 22, 15, 8, 8, 6, 5, 4, 3, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1]

# 品牌（基于样本 + 工业真实品牌库）
BRANDS = [
    "BOSCH", "MAHLE", "MANN", "HENGST", "DONALDSON", "FLEETGUARD",
    "FRAM", "WIX", "AC DELCO", "MOTORCRAFT", "PUROLATOR", "K&N",
    "KTM", "HIFLO", "CHAMPION", "BALDWIN", "LUBER-FINER", "NAPA",
    "1A FIRST AUTOMOTIVE", "ABAC", "AC DELCO", "AAF", "A.Z. MEISTERTEILE",
    "AB FILTER", "ABG", "ABSOLENT", "2 G-ENERGY", "3F QUALITY", "JAPANPARTS",
    "ASHIKA", "JAPKO", "NIPPARTS", "BLUE PRINT", "FEBI", "SWAG", "MEYLE",
    "TOPRAN", "VAICO", "OPTIMAL", "MAPCO", "MASTER-SPORT", "RIDEX",
    "STARK", "ENERGY", "MDR", "FILTRON", "DENCKERMANN", "MULLER FILTER",
    "MISFAT", "PBR", "ALCO FILTER", "WIX FILTERS", "TECNOCAR", "MAGNETI MARELLI",
    "CLEAN FILTERS", "COOPERSFIAAM", "SOGEFI", "UFI", "FIAT", "FIAT-HITACHI"
]
BRAND_WEIGHTS = [50] + [10]*10 + [5]*40 + [3]*(len(BRANDS)-51)

# 滤材 + 密封 + 效率（基于样本）
MEDIAS = ["Glass Fiber", "Cellulose", "Synthetic", "Pleated Paper",
          "Activated Carbon", "Metal Mesh", "Felt", "Non-woven"]
MEDIA_WEIGHTS = [40, 25, 15, 8, 5, 4, 2, 1]

SEAL_MATERIALS = ["Nitrile", "Viton", "Silicone", "EPDM", "FKM", "HNBR", "Polyurethane"]
SEAL_WEIGHTS = [40, 20, 15, 10, 8, 4, 3]

# 效率值（β200 比例）
EFFICIENCIES = ["3µ @ β200", "5µ @ β200", "10µ @ β200", "15µ @ β200",
                "20µ @ β200", "25µ @ β200", "50µ @ β200"]
EFFICIENCY_WEIGHTS = [5, 15, 25, 20, 15, 10, 10]

# 螺纹格式（基于样本）
THREADS = ["3/4-16UNF", "M20x1.5", "M22x1.5", "M24x1.5", "M26x1.5",
           "M27x2", "1-12UNF", "1-14UNS", "13/16-16UN", "M30x1.5", "M36x1.5"]
THREAD_WEIGHTS = [25, 20, 15, 10, 8, 5, 5, 4, 4, 3, 1]

# WHY 新增: product_name_1/2 变体库 (Excel 规范分区 1 主信息区)
#   - product_name_1: 产品主名, 业务规则"当 product1 和 product3 同时存在时只录入 product1" (前端优先显示)
#   - product_name_2: 副名/修饰, 可空
#   - dict_product_name1/2 字典来源这两个字段, 故需有 distinct 多样性
NAME_VARIANTS = ["Standard", "Heavy Duty", "Premium", "Industrial", "Compact",
                 "High Flow", "Long Life", "Pro", "Eco", "Heavy"]

# 车型大类
MACHINE_TYPES = ["Compressor", "Light vehicle", "Road paver", "Motor",
                 "Scrubber", "Power generator", "Compactor", "Sandblast",
                 "Vibrating plate", "Curb installation", "Tractor", "Excavator",
                 "Wheel loader", "Bulldozer", "Crane", "Dump truck", "Bus",
                 "Heavy truck", "Agricultural machinery", "Construction equipment"]
MACHINE_WEIGHTS = [15, 18, 8, 5, 3, 4, 3, 1, 1, 1, 8, 6, 5, 3, 2, 3, 2, 5, 4, 3]

MACHINE_BRANDS = ["ABARTH", "ACURA", "AUDI", "BMW", "CHEVROLET", "CITROEN",
                  "FIAT", "FORD", "HONDA", "HYUNDAI", "KIA", "MAZDA",
                  "MERCEDES", "MITSUBISHI", "NISSAN", "OPEL", "PEUGEOT",
                  "RENAULT", "SKODA", "SUBARU", "SUZUKI", "TOYOTA",
                  "VOLKSWAGEN", "VOLVO", "ABAC", "ABG", "ACME", "CATERPILLAR",
                  "KOMATSU", "JCB", "HITACHI", "KUBOTA", "JOHN DEERE",
                  "CASE", "NEW HOLLAND", "MASSEY FERGUSON", "DEUTZ",
                  "PERKINS", "CUMMINS", "DEUTZ-FAHR"]
MACHINE_BRAND_WEIGHTS = [3]*25 + [4]*15

ENGINES = ["GASOLINE", "DIESEL", "ELECTRIC", "HYBRID", "LPG", "CNG", "BIODIESEL"]
ENGINE_WEIGHTS = [55, 30, 5, 4, 3, 2, 1]


def gen_oem_no(idx: int) -> str:
    """生成工业 OEM 风格编号: SA 42359 / SO 3335 / BE 610 / 等"""
    prefix = random.choice(["SA", "SO", "SH", "BE", "OT", "OV", "PT", "FT", "CT", "ET", "WT", "AC", "AF"])
    # 数字部分 4-6 位
    digits = random.randint(10000, 999999)
    sep = " " if random.random() < 0.7 else ""
    return f"{prefix}{sep}{digits:06d}"


def gen_oem_no_normalized(oem: str) -> str:
    """归一化: 去空格 + 大写"""
    return oem.replace(" ", "").upper()


def gen_thread(threads_pool) -> str:
    return random.choice(threads_pool) if random.random() < 0.85 else ""


def gen_dim() -> float | None:
    """真实尺寸分布: 30-300 mm"""
    if random.random() < 0.02:  # 2% 缺失
        return None
    return round(random.uniform(30.0, 300.0), 1)


def gen_height() -> float | None:
    if random.random() < 0.02:
        return None
    return round(random.uniform(20.0, 500.0), 1)


def gen_product(idx: int) -> dict:
    """生成单条产品记录（模拟 ETL 清洗后的干净数据）"""
    oem_display = gen_oem_no(idx)
    oem_norm = gen_oem_no_normalized(oem_display)
    type_ = random.choices(TYPES, TYPE_WEIGHTS)[0]
    d1, d2, d3 = gen_dim(), gen_dim(), gen_dim()
    h1, h2, h3 = gen_height(), gen_height(), gen_height()
    # WHY 新增: product_name_1/2 模拟真实分布
    #   - product_name_1: 70% 与 type 同步 (前端优先显示), 20% type+variant (更细分类), 10% None (没填)
    #   - product_name_2: 25% variant 修饰, 75% None (可选字段, 多数空)
    r1 = random.random()
    if r1 < 0.70:
        product_name_1 = type_
    elif r1 < 0.90:
        product_name_1 = f"{type_} {random.choice(NAME_VARIANTS)}"
    else:
        product_name_1 = None
    product_name_2 = random.choice(NAME_VARIANTS) if random.random() < 0.25 else None
    return {
        "oem_no_display": oem_display,
        "oem_no_normalized": oem_norm,
        "remark": f"HiFi Filter {oem_display} {type_.title()}",
        "product_name_1": product_name_1,
        "product_name_2": product_name_2,
        "product_name_3": type_,
        "type": type_,  # ETL 已注入
        "d1_mm": d1, "d2_mm": d2, "d3_mm": d3,
        "h1_mm": h1, "h2_mm": h2, "h3_mm": h3,
        "d7_thread": gen_thread(THREADS),
        "d8_thread": gen_thread(THREADS),
        "media": random.choices(MEDIAS, MEDIA_WEIGHTS)[0] if random.random() < 0.7 else "",
        "sealing_material": random.choices(SEAL_MATERIALS, SEAL_WEIGHTS)[0] if random.random() < 0.6 else "",
        "efficiency_1": random.choices(EFFICIENCIES, EFFICIENCY_WEIGHTS)[0] if random.random() < 0.5 else "",
        "bypass_valve_lr": round(random.uniform(0.5, 3.0), 2) if random.random() < 0.4 else None,
        "collapse_pressure_bar": round(random.uniform(5, 30), 1) if random.random() < 0.5 else None,
        "temp_range": random.choice(["-30 - +100°C", "-40 - +120°C", "-20 - +80°C"]) if random.random() < 0.4 else "",
        "qty_per_carton": random.randint(1, 50) if random.random() < 0.3 else None,
        "weight_kgs": round(random.uniform(0.1, 5.0), 3) if random.random() < 0.3 else None,
        "carton_length_mm": round(random.uniform(100, 800), 1) if random.random() < 0.3 else None,
        "carton_width_mm": round(random.uniform(80, 600), 1) if random.random() < 0.3 else None,
        "carton_height_mm": round(random.uniform(80, 600), 1) if random.random() < 0.3 else None,
        "image_key": f"products/{oem_norm}/main.jpg" if random.random() < 0.7 else None,
        "image_status": "ready" if random.random() < 0.7 else "pending",
        "is_discontinued": False,
    }


def gen_cross_references(product_id: str) -> list:
    """每个产品 5-20 个交叉引用"""
    n = random.randint(5, 20)
    refs = []
    used = set()
    for _ in range(n):
        brand = random.choices(BRANDS, BRAND_WEIGHTS)[0]
        # 数字 part 3-7 位
        no = f"{random.choice(string.ascii_uppercase)}{random.randint(100, 9999999):X}"
        if (brand, no) in used:
            continue
        used.add((brand, no))
        refs.append({
            "product_oem": product_id,
            "product_name_1": random.choices(TYPES, TYPE_WEIGHTS)[0],
            "oem_brand": brand,
            "oem_no_3": no,
        })
    return refs


def gen_machine_applications(product_id: str) -> list:
    """每个产品 1-30 个机型适配"""
    n = random.randint(1, 30)
    apps = []
    used = set()
    for _ in range(n):
        brand = random.choices(MACHINE_BRANDS, MACHINE_BRAND_WEIGHTS)[0]
        model = f"{brand[0]}{random.randint(100, 9999)}"
        if (brand, model) in used:
            continue
        used.add((brand, model))
        # 生产日期 50% 概率存在
        if random.random() < 0.5:
            year = random.randint(1995, 2025)
            month = random.randint(1, 12)
            prod_date = f"{year}-{month:02d}-01>"
        else:
            prod_date = ""
        apps.append({
            "product_oem": product_id,
            "machine_brand": brand,
            "machine_model": model,
            "model_name": random.choices(MACHINE_TYPES, MACHINE_WEIGHTS)[0],
            "engine_brand": random.choice(["PERKINS", "CUMMINS", "DEUTZ", "CATERPILLAR", "HONDA", "TOYOTA", ""]) if random.random() < 0.4 else "",
            "engine_type": f"{random.choice(string.ascii_uppercase)}{random.randint(100, 999)}-{random.randint(10, 99)}" if random.random() < 0.4 else "",
            "engine_energy": random.choices(ENGINES, ENGINE_WEIGHTS)[0] if random.random() < 0.5 else "",
            "production_date_start_str": prod_date,
        })
    return apps


def main():
    TARGET = 1_000_000
    print(f"开始生成 {TARGET:,} 条产品数据 ...")
    t0 = time.time()

    # 1) 生成主产品表
    products_xlsx_path = OUT_DIR / f"synthetic_products_{TARGET//1000}k.xlsx"
    products_jsonl_path = OUT_DIR / f"synthetic_products_{TARGET//1000}k.jsonl"
    xrefs_path = OUT_DIR / f"synthetic_xrefs_{TARGET//1000}k.jsonl"
    apps_path = OUT_DIR / f"synthetic_apps_{TARGET//1000}k.jsonl"

    # 用 jsonl 写 (内存友好),xlsx 单独写一份给 ETL 测
    n_total_xref = 0
    n_total_app = 0
    type_counter = Counter()

    with open(products_jsonl_path, "w", encoding="utf-8") as fp, \
         open(xrefs_path, "w", encoding="utf-8") as fx, \
         open(apps_path, "w", encoding="utf-8") as fa:
        for i in range(1, TARGET + 1):
            p = gen_product(i)
            type_counter[p["type"]] += 1
            fp.write(json.dumps(p, ensure_ascii=False) + "\n")
            # 交叉引用
            for x in gen_cross_references(p["oem_no_normalized"]):
                fx.write(json.dumps(x, ensure_ascii=False) + "\n")
                n_total_xref += 1
            # 机型
            for a in gen_machine_applications(p["oem_no_normalized"]):
                fa.write(json.dumps(a, ensure_ascii=False) + "\n")
                n_total_app += 1
            if i % 100_000 == 0:
                print(f"  已生成 {i:,}/{TARGET:,} | 累计 xref={n_total_xref:,} app={n_total_app:,} | 耗时 {time.time()-t0:.1f}s")

    elapsed = time.time() - t0
    print(f"\n[1] JSONL 生成完成: {elapsed:.1f}s")
    print(f"    products: {products_jsonl_path} ({products_jsonl_path.stat().st_size / 1024 / 1024:.1f} MB)")
    print(f"    xrefs:    {xrefs_path} ({xrefs_path.stat().st_size / 1024 / 1024:.1f} MB) | {n_total_xref:,} 条")
    print(f"    apps:     {apps_path} ({apps_path.stat().st_size / 1024 / 1024:.1f} MB) | {n_total_app:,} 条")

    # 2) 也输出一份小 xlsx 给 ETL 流程测试 (10 万条,模拟大文件分批)
    sample_size = 100_000
    print(f"\n[2] 生成 {sample_size:,} 条样本 xlsx (ETL 流程测试用) ...")
    sample_df = pd.DataFrame([gen_product(i) for i in range(1, sample_size + 1)])
    sample_df.to_excel(products_xlsx_path, index=False, engine="openpyxl")
    print(f"    {products_xlsx_path} ({products_xlsx_path.stat().st_size / 1024 / 1024:.1f} MB)")

    # 3) 输出分布报告
    report_path = OUT_DIR / "DISTRIBUTION-REPORT.md"
    with open(report_path, "w", encoding="utf-8") as fr:
        fr.write(f"# 合成数据分布报告\n\n")
        fr.write(f"- 产品总数: **{TARGET:,}**\n")
        fr.write(f"- 交叉引用: **{n_total_xref:,}** (平均 {n_total_xref/TARGET:.1f}/产品)\n")
        fr.write(f"- 机型适配: **{n_total_app:,}** (平均 {n_total_app/TARGET:.1f}/产品)\n\n")
        fr.write(f"## Type 分布 (Top 15)\n\n")
        fr.write("| Rank | Type | Count | % |\n|---|---|---|---|\n")
        for i, (t, c) in enumerate(type_counter.most_common(15), 1):
            fr.write(f"| {i} | {t} | {c:,} | {c/TARGET*100:.1f}% |\n")

    print(f"\n[3] 分布报告: {report_path}")
    print(f"\n[4] 总耗时: {time.time()-t0:.1f}s")
    print("=== Day 1 完成 ===\n")


if __name__ == "__main__":
    main()
