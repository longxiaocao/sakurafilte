"""
生成符合 etl_clean.py 期望的源数据.xlsx (3 sheets + 中文列名 + 业务合理数据)

WHY 之前 synthetic_products_1000k.xlsx 只有 1 个 Sheet1 + jsonl 字段名, etl_clean.py 无法处理
本次按 Excel 规范「后台新增产品格式」分区 1-3 设计:
  - 产品区: 50000 行 (主表, 含 Product Name 1/2/3 等全部规范字段)
  - OEM区:  ~250000 行 (每产品 5-20 个 xref, 平均 10)
  - 机型区: ~500000 行 (每产品 1-30 个机型, 平均 10)

业务合理性:
  - Product Name 1: 70% = type, 20% = type+variant, 10% = None (与 generate_synthetic_data.py 同分布)
  - Bypass Valve HR = LR × 1.5-2.0 (业务规则: HR > LR)
  - Production Date: "2008-08-01>" 格式 (etl_clean.py 期望)
  - OEM NO.2: 产品区与 OEM区/机型区 关联键 (同 oem_no_display)
"""
import random
import time
from pathlib import Path
import pandas as pd

SEED = 42
random.seed(SEED)

OUT_PATH = Path(r"d:\projects\sakurafilter\spike-test\output\源数据.xlsx")
N_PRODUCTS = 50_000

# ========== 滤芯类型 (23 种, 业务权重) ==========
TYPES = ["AIR FILTER", "OIL FILTER", "FUEL FILTER", "COOLANT FILTER", "HYDRAULIC FILTER",
         "CABIN AIR FILTER", "TRANSMISSION FILTER", "BRAKE FILTER", "AIR DRYER", "WATER SEPARATOR",
         "STRAINER", "SUCTION FILTER", "RETURN FILTER", "PRESSURE FILTER", "BREATHER",
         "MAGNETIC FILTER", "BYPASS FILTER", "CENTRIFUGAL FILTER", "COALESCER", "DUPLICATOR",
         "INTAKE FILTER", "EXHAUST FILTER", "LUBE FILTER"]
TYPE_WEIGHTS = [18, 15, 12, 8, 8, 7, 5, 3, 3, 3, 2, 2, 2, 2, 2, 1, 2, 1, 1, 1, 1, 1, 2]

# ========== 产品名变体 (Product Name 1/2 用) ==========
NAME_VARIANTS = ["Standard", "Heavy Duty", "Premium", "Industrial", "Compact",
                 "High Flow", "Long Life", "Pro", "Eco", "Heavy"]

# ========== 滤材 (Media) ==========
MEDIA = ["Cellulose", "Synthetic", "Glass Fiber", "Cotton", "Stainless Steel",
         "Paper", "Composite", "Activated Carbon"]
MEDIA_WEIGHTS = [25, 20, 15, 10, 8, 10, 7, 5]

# ========== 密封材料 ==========
SEAL_MATERIALS = ["NBR", "Viton", "Silicone", "EPDM", "H NBR", "FKM", "PU"]

# ========== 效率值 ==========
EFFICIENCIES = ["99.5%", "99.0%", "98.5%", "98.0%", "95.0%", "90.0%", "85.0%"]
EFFICIENCY_WEIGHTS = [15, 20, 15, 15, 15, 10, 10]

# ========== 螺纹规格 ==========
THREADS = ["3/4-16UNF", "M20x1.5", "M22x1.5", "M24x1.5", "M26x1.5",
           "M27x2", "1-12UNF", "1-14UNS", "13/16-16UN", "M30x1.5", "M36x1.5"]
THREAD_WEIGHTS = [25, 20, 15, 10, 8, 5, 5, 4, 4, 3, 1]

# ========== 温度范围 ==========
TEMP_RANGES = ["-30°C~+120°C", "-40°C~+150°C", "-20°C~+100°C", "-10°C~+80°C"]

# ========== OEM 品牌 (OEM区 xref 用, 60+ 品牌) ==========
OEM_BRANDS = [
    "Bosch", "Mann", "Wix", "Fram", "Donaldson", "Purflux", "Hengst", "Mahle",
    "Knecht", "Sogefi", "Ufi", "Baldwin", "Hifi", "Sakura", "Fleetguard", "Parker",
    "Pall", "Hilco", "Jonell", "Kayaba", "Yamalube", "Cat", "John Deere", "Volvo",
    "Renault", "DAF", "Iveco", "Scania", "MAN", "Mercedes",
]
# 30 品牌, 30 权重
OEM_BRAND_WEIGHTS = [10, 10, 8, 8, 6, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 2, 2, 2, 2, 2, 2,
                     2, 2, 2, 2, 2, 2, 2, 2]

# ========== 机型品牌 ==========
MACHINE_BRANDS = ["Caterpillar", "Komatsu", "Hitachi", "Volvo CE", "John Deere", "Liebherr",
                  "Doosan", "JCB", "Case", "New Holland", "Kubota", "Yanmar", "Hyundai CE",
                  "Sany", "XCMG", "LiuGong", "Zoomlion", "SDLG", "Liugong", "LonKing"]
MACHINE_BRAND_WEIGHTS = [15, 12, 10, 8, 7, 5, 5, 5, 4, 4, 4, 3, 3, 3, 3, 2, 2, 2, 1, 1]

# ========== 机型类型 (Model Name) ==========
MODEL_NAMES = ["Excavator", "Loader", "Bulldozer", "Dump Truck", "Grader", "Crane",
               "Forklift", "Compactor", "Paver", "Driller", "Harvester", "Tractor",
               "Backhoe", "Skid Steer", "Telehandler", "Reach Stacker", "Scraper", "Water Truck",
               "Fire Truck", "Concrete Mixer"]

# ========== 发动机品牌 ==========
ENGINE_BRANDS = ["Cummins", "Caterpillar", "Volvo Penta", "MTU", "Deutz", "Perkins",
                 "Isuzu", "Hino", "Mitsubishi", "Kubota", "Yanmar", "Scania"]
ENGINE_BRAND_WEIGHTS = [15, 12, 10, 8, 8, 7, 6, 5, 5, 5, 5, 4]

# ========== 发动机能源 ==========
ENGINE_ENERGIES = ["Diesel", "Gasoline", "Electric", "Hybrid", "LPG", "CNG"]
ENGINE_ENERGY_WEIGHTS = [60, 20, 5, 5, 5, 5]


def gen_oem_no(idx: int) -> str:
    """OEM NO.2 (产品唯一编号, 8 位)"""
    return f"P{idx:08d}"


def gen_oem_no_3(brand: str, idx: int) -> str:
    """OEM NO.3 (交叉引用号, 每品牌独立编号)"""
    return f"{brand[:3].upper()}-{idx % 100000:05d}"


def gen_dim() -> int:
    return random.randint(30, 300)


def gen_height() -> int:
    return random.randint(20, 500)


def gen_production_date() -> str:
    """机型区 Production Date 格式: 'YYYY-MM-DD>' (> 表示持续生产)"""
    year = random.randint(2005, 2024)
    month = random.randint(1, 12)
    day = random.randint(1, 28)
    suffix = random.choice([">", ">", ">", "-", "-"])  # 60% ongoing, 40% closed
    if suffix == ">":
        return f"{year}-{month:02d}-{day:02d}>"
    else:
        end_year = min(year + random.randint(1, 10), 2024)
        end_month = random.randint(1, 12)
        return f"{year}-{month:02d}-{day:02d}-{end_year}-{end_month:02d}"


def gen_product_row(idx: int) -> dict:
    """生成产品区一行 (符合 etl_clean.py clean_products_sheet 期望)"""
    oem = gen_oem_no(idx)
    type_ = random.choices(TYPES, TYPE_WEIGHTS)[0]

    # Product Name 1/2 分布 (业务规则: 主名优先于 product_name_3 显示)
    r1 = random.random()
    if r1 < 0.70:
        product_name_1 = type_
    elif r1 < 0.90:
        product_name_1 = f"{type_} {random.choice(NAME_VARIANTS)}"
    else:
        product_name_1 = None
    product_name_2 = random.choice(NAME_VARIANTS) if random.random() < 0.25 else None

    d1, d2, d3 = gen_dim(), gen_dim(), gen_dim()
    h1, h2, h3 = gen_height(), gen_height(), gen_height()
    media = random.choices(MEDIA, MEDIA_WEIGHTS)[0]
    seal = random.choice(SEAL_MATERIALS)
    eff1 = random.choices(EFFICIENCIES, EFFICIENCY_WEIGHTS)[0]
    eff2 = random.choices(EFFICIENCIES, EFFICIENCY_WEIGHTS)[0] if random.random() < 0.6 else None
    bypass_lr = round(random.uniform(0.5, 3.0), 1)
    bypass_hr = round(bypass_lr * random.uniform(1.5, 2.0), 1)  # 业务规则: HR > LR
    collapse = round(random.uniform(5.0, 30.0), 1)
    temp = random.choice(TEMP_RANGES)
    bypass_pressure = round(random.uniform(0.5, 3.0), 1)
    thread1 = random.choices(THREADS, THREAD_WEIGHTS)[0]
    thread2 = random.choices(THREADS, THREAD_WEIGHTS)[0] if random.random() < 0.7 else None

    return {
        "OEM NO.2": oem,
        "Product Name 1": product_name_1,
        "Product Name 2": product_name_2,
        "product name 3": type_,  # etl_clean.py 期望小写
        "Remark": f"HiFi Filter {oem} {type_.title()}",
        "Dimension 1 (D1)": d1,
        "Dimension 2 (D2)": d2,
        "Dimension 3 (D3)": d3,
        "Height 1 (H1)": h1,
        "Height 2 (H2)": h2,
        "Height 3 (H3)": h3,
        "Thread 1 (D7)": thread1,
        "Thread 2 (D8)": thread2,
        "Media": media,
        "Seal Material": seal,
        "Efficiency 1": eff1,
        "Efficiency 2": eff2,
        "Bypass Valve Setting (LR)": bypass_lr,
        "Bypass Valve Setting (HR)": bypass_hr,
        "Δ Collapse Pressure": collapse,
        "Temperature Range": temp,
        "Bypass Pressure": bypass_pressure,
    }


def gen_xref_rows(product_oem: str, product_type: str) -> list[dict]:
    """为一个产品生成 5-20 个 xref (符合 etl_clean.py clean_xrefs_sheet 期望)"""
    n = random.randint(5, 20)
    rows = []
    for i in range(n):
        brand = random.choices(OEM_BRANDS, OEM_BRAND_WEIGHTS)[0]
        # xref 的 Product Name 1: 60% 与产品 type 同步, 30% 加 variant, 10% 用品牌名
        r = random.random()
        if r < 0.60:
            xref_name1 = product_type
        elif r < 0.90:
            xref_name1 = f"{product_type} {random.choice(NAME_VARIANTS)}"
        else:
            xref_name1 = f"{brand} {product_type}"
        rows.append({
            "OEM NO.2": product_oem,
            " OEM Brand": brand,  # 注意前导空格 (etl_clean.py 期望)
            "Product Name 1": xref_name1,
            "OEM NO.3": gen_oem_no_3(brand, random.randint(1, 999999)),
        })
    return rows


def gen_app_rows(product_oem: str) -> list[dict]:
    """为一个产品生成 1-30 个机型 (符合 etl_clean.py clean_apps_sheet 期望)"""
    n = random.randint(1, 30)
    rows = []
    for i in range(n):
        rows.append({
            "OEM NO.2": product_oem,
            "Machine Brand": random.choices(MACHINE_BRANDS, MACHINE_BRAND_WEIGHTS)[0],
            "Machine Model": f"M{random.randint(100, 999)}",
            "Model Name": random.choice(MODEL_NAMES),
            "Engine Brand": random.choices(ENGINE_BRANDS, ENGINE_BRAND_WEIGHTS)[0],
            "Engine Type": f"E{random.randint(1, 99)}",
            "Engine Energy": random.choices(ENGINE_ENERGIES, ENGINE_ENERGY_WEIGHTS)[0],
            "Production Date": gen_production_date(),
        })
    return rows


def main():
    t0 = time.time()
    print(f"开始生成 {N_PRODUCTS:,} 条产品 + xrefs + apps ...", flush=True)

    # 1) 产品区
    print("[1/3] 生成产品区 ...", flush=True)
    products = [gen_product_row(i + 1) for i in range(N_PRODUCTS)]
    df_p = pd.DataFrame(products)
    print(f"  产品区: {len(df_p):,} 行", flush=True)

    # 2) OEM区
    print("[2/3] 生成 OEM区 (xrefs) ...", flush=True)
    xrefs = []
    for p in products:
        xrefs.extend(gen_xref_rows(p["OEM NO.2"], p["product name 3"]))
    df_x = pd.DataFrame(xrefs)
    print(f"  OEM区: {len(df_x):,} 行", flush=True)

    # 3) 机型区
    print("[3/3] 生成机型区 (apps) ...", flush=True)
    apps = []
    for p in products:
        apps.extend(gen_app_rows(p["OEM NO.2"]))
    df_a = pd.DataFrame(apps)
    print(f"  机型区: {len(df_a):,} 行", flush=True)

    # 写入 Excel
    print(f"\n写入 Excel: {OUT_PATH} ...", flush=True)
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with pd.ExcelWriter(OUT_PATH, engine="openpyxl") as writer:
        df_p.to_excel(writer, sheet_name="产品区", index=False)
        df_x.to_excel(writer, sheet_name="OEM区", index=False)
        df_a.to_excel(writer, sheet_name="机型区", index=False)

    elapsed = time.time() - t0
    size_mb = OUT_PATH.stat().st_size / 1024 / 1024
    print(f"\n[完成] 耗时 {elapsed:.1f}s | 文件 {size_mb:.1f} MB", flush=True)
    print(f"  产品区: {len(df_p):,} 行", flush=True)
    print(f"  OEM区:  {len(df_x):,} 行", flush=True)
    print(f"  机型区: {len(df_a):,} 行", flush=True)

    # 数据质量自检
    print("\n=== 数据质量自检 ===", flush=True)
    print(f"产品区 Product Name 1 非空率: {df_p['Product Name 1'].notna().mean():.1%}", flush=True)
    print(f"产品区 Product Name 2 非空率: {df_p['Product Name 2'].notna().mean():.1%}", flush=True)
    print(f"产品区 product name 3 非空率: {df_p['product name 3'].notna().mean():.1%}", flush=True)
    print(f"产品区 Efficiency 2 非空率:  {df_p['Efficiency 2'].notna().mean():.1%}", flush=True)
    print(f"产品区 Thread 2 非空率:      {df_p['Thread 2 (D8)'].notna().mean():.1%}", flush=True)
    print(f"OEM区  OEM Brand 非空率:     {df_x[' OEM Brand'].notna().mean():.1%}", flush=True)
    print(f"OEM区  OEM NO.3 非空率:      {df_x['OEM NO.3'].notna().mean():.1%}", flush=True)
    print(f"机型区 Machine Brand 非空率: {df_a['Machine Brand'].notna().mean():.1%}", flush=True)
    print(f"机型区 Engine Brand 非空率:  {df_a['Engine Brand'].notna().mean():.1%}", flush=True)
    print(f"产品区 vs OEM区 OEM 一致性:  {len(set(df_p['OEM NO.2']) & set(df_x['OEM NO.2'])):,}/{N_PRODUCTS:,}", flush=True)
    print(f"产品区 vs 机型区 OEM 一致性: {len(set(df_p['OEM NO.2']) & set(df_a['OEM NO.2'])):,}/{N_PRODUCTS:,}", flush=True)


if __name__ == "__main__":
    main()
