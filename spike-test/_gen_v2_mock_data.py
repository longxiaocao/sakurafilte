"""
V2 模拟数据生成器 (Task 5.2)
============================
生成 V2 架构迁移测试用的最小数据集:
  - mock_products_v2.jsonl  (100 行, MR000001~MR000100)
  - mock_xrefs_v2.jsonl     (300 行, 每 MR.1 对应 2-5 个 OEM 3)
  - mock_apps_v2.jsonl      (500 行, 每 MR.1 对应 3-8 个机型)
  - MinIO 占位图片:
      * 300 张主图  key=products/primary/{oem3}/{oem3}-1.png  (每个 OEM 3 一张)
      * 200 张详情图 key=products/detail/{mr1}/{mr1}-{slot}.png  (每个 MR.1 2 张, slot=2,3)

V2 字段全集覆盖:
  - products: mr_1, oem_2, is_published, d4_mm, h4_mm, d1~h4_mm_raw (8 个原始值)
  - xrefs:    oem_2, sort_order, machine_type, is_published (枚举白名单)
  - apps:     machine_category (5 类枚举全覆盖)

数据关系验证 (Task 5.2.3):
  - MR.1 一对多 OEM 3 (xrefs 中 mr_1 重复 2-5 次)
  - OEM 3 一对一主图 (300 个 oem3 = 300 张主图)
  - MR.1 一对多详情图 (100 个 mr1 * 2 slot = 200 张详情图)
  - machine_category 覆盖 agriculture/commercial/construction/industrial/others

依赖:
  - Pillow (图片生成): pip install Pillow
  - minio  (SDK 上传): pip install minio

环境变量 (MinIO 上传时必需, 可用 --skip-upload 跳过):
  - MINIO_ENDPOINT      默认 localhost:9000
  - MINIO_ACCESS_KEY    必填 (与 start-dev.ps1 生成的 .env 一致)
  - MINIO_SECRET_KEY    必填
  - MINIO_BUCKET        默认 sakurafilter
  - MINIO_USE_SSL       默认 false

用法:
  python spike-test/_gen_v2_mock_data.py                  # 生成 jsonl + 上传 MinIO
  python spike-test/_gen_v2_mock_data.py --skip-upload    # 仅生成 jsonl (无 MinIO 环境)
  python spike-test/_gen_v2_mock_data.py --out-dir <path> # 自定义输出目录
"""
import argparse
import io
import json
import os
import random
import sys
import time
from pathlib import Path
from collections import Counter, defaultdict

SEED = 20260717  # 固定 seed 保证可复现 (用日期方便记忆)
random.seed(SEED)

# ========== V2 枚举白名单 (与 EtlImportService.AllowedMachineCategories / DB CHECK 一致) ==========
MACHINE_CATEGORIES = ["agriculture", "commercial", "construction", "industrial", "others"]

# ========== 字典库 (复用 generate_synthetic_data.py 风格, 缩小规模适配 100 条测试) ==========
TYPES = [
    "AIR FILTER", "OIL FILTER", "FUEL FILTER", "CABIN AIR FILTER",
    "HYDRAULIC FILTER", "PETROL FILTER", "AIR/OIL SEPARATOR",
    "ACTIVATED CARBON FILTER", "WATER SEPARATOR", "COOLANT FILTER",
    "SPIN-ON FILTER", "CARTRIDGE FILTER", "INDUSTRIAL FILTER",
]
TYPE_WEIGHTS = [25, 22, 15, 8, 8, 6, 5, 4, 3, 2, 2, 2, 2]

# OEM 品牌 (xrefs.oem_brand) — 30 个高频品牌保证 300 xref 多样性
OEM_BRANDS = [
    "BOSCH", "MAHLE", "MANN", "HENGST", "DONALDSON", "FLEETGUARD",
    "FRAM", "WIX", "AC DELCO", "MOTORCRAFT", "PUROLATOR", "K&N",
    "HIFLO", "CHAMPION", "BALDWIN", "LUBER-FINER", "NAPA",
    "JAPANPARTS", "ASHIKA", "JAPKO", "BLUE PRINT", "FEBI", "SWAG",
    "MEYLE", "VAICO", "OPTIMAL", "MAPCO", "FILTRON", "DENCKERMANN", "UFI",
]
OEM_BRAND_WEIGHTS = [50] + [10] * 10 + [5] * 19  # 30 个品牌, BOSCH 权重最高便于搜索验证

NAME_VARIANTS = ["Standard", "Heavy Duty", "Premium", "Industrial", "Compact",
                 "High Flow", "Long Life", "Pro", "Eco"]

MACHINE_BRANDS = [
    "CATERPILLAR", "KOMATSU", "JCB", "HITACHI", "KUBOTA", "JOHN DEERE",
    "CASE", "NEW HOLLAND", "MASSEY FERGUSON", "DEUTZ", "PERKINS", "CUMMINS",
    "VOLVO", "RENAULT", "MERCEDES", "MAN", "SCANIA", "DAF",
]
MACHINE_BRAND_WEIGHTS = [10] * 6 + [6] * 6 + [4] * 6

ENGINES = ["GASOLINE", "DIESEL", "ELECTRIC", "HYBRID", "LPG"]
ENGINE_WEIGHTS = [50, 35, 5, 7, 3]


# ========== 生成器函数 ==========

def gen_mr1(idx: int) -> str:
    """MR.1 编号: MR + 6 位数字 (MR000001~MR000100), 与 spec L265 一致"""
    return f"MR{idx:06d}"


def gen_oem_no_3(brand: str, seq: int) -> str:
    """OEM 3 编号: 品牌前缀 + 6 位大写十六进制, 保证全表唯一"""
    prefix = brand[:3].upper().replace(" ", "X")
    # WHY 用 seq + 随机数: 保证唯一性, 同时避免连续 seq 看起来太规整
    rand_part = f"{(seq * 7919 + random.randint(0, 0xFFFFFF)) & 0xFFFFFF:06X}"
    return f"{prefix}{rand_part}"


def gen_oem_2(seq: int) -> str | None:
    """OEM 2: 30% 概率为空 (可选字段), 70% 生成 8 位字母数字"""
    if random.random() < 0.30:
        return None
    chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"  # 去除易混字符 I/O/0/1
    return "".join(random.choice(chars) for _ in range(8))


def gen_dim_raw_and_value() -> tuple[str | None, float | None]:
    """生成尺寸字段: 同时产出 raw 原始字符串与数值 (V2 双轨存储)
       返回 (raw, value): 95% 有值, 5% 缺失 (raw 与 value 同时为 None)
       20% 的有值情况 raw 带 "mm" 后缀 (模拟源数据脏格式)
    """
    if random.random() < 0.05:
        return None, None
    v = round(random.uniform(30.0, 300.0), 1)
    if random.random() < 0.20:
        return f"{v}mm", v
    return f"{v}", v


def gen_height_raw_and_value() -> tuple[str | None, float | None]:
    if random.random() < 0.05:
        return None, None
    v = round(random.uniform(20.0, 500.0), 1)
    if random.random() < 0.20:
        return f"{v}mm", v
    return f"{v}", v


def gen_product(idx: int) -> dict:
    """生成单条 V2 产品记录 (jsonl 字段 snake_case, 与 ETL 解析对齐)"""
    mr1 = gen_mr1(idx)
    oem_display = f"SA {idx:05d}"  # oem_no_display: SA 00001 (兼容旧 ETL 字段)
    type_ = random.choices(TYPES, TYPE_WEIGHTS)[0]

    # 尺寸 4 组 (D1-H4), 每组同时生成 raw + value
    d1_raw, d1 = gen_dim_raw_and_value()
    d2_raw, d2 = gen_dim_raw_and_value()
    d3_raw, d3 = gen_dim_raw_and_value()
    d4_raw, d4 = gen_dim_raw_and_value()
    h1_raw, h1 = gen_height_raw_and_value()
    h2_raw, h2 = gen_height_raw_and_value()
    h3_raw, h3 = gen_height_raw_and_value()
    h4_raw, h4 = gen_height_raw_and_value()

    # product_name_1: 70% 与 type 同步, 20% type+variant, 10% None
    r1 = random.random()
    if r1 < 0.70:
        product_name_1 = type_
    elif r1 < 0.90:
        product_name_1 = f"{type_} {random.choice(NAME_VARIANTS)}"
    else:
        product_name_1 = None
    product_name_2 = random.choice(NAME_VARIANTS) if random.random() < 0.25 else None

    return {
        # V2 主键 + 关联字段
        "mr_1": mr1,
        "oem_no_display": oem_display,
        "oem_no_normalized": oem_display.replace(" ", "").upper(),  # V2 从 mr_1 派生, 此处保留兼容
        "oem_2": gen_oem_2(idx),
        "is_published": random.random() > 0.05,  # 5% 下架
        # 名称
        "product_name_1": product_name_1,
        "product_name_2": product_name_2,
        "product_name_3": type_,
        "type": type_,
        "remark": f"HiFi Filter {mr1} {type_.title()}",
        # 尺寸 V2 (数值 + 原始字符串)
        "d1_mm": d1, "d2_mm": d2, "d3_mm": d3, "d4_mm": d4,
        "h1_mm": h1, "h2_mm": h2, "h3_mm": h3, "h4_mm": h4,
        "d1_mm_raw": d1_raw, "d2_mm_raw": d2_raw, "d3_mm_raw": d3_raw, "d4_mm_raw": d4_raw,
        "h1_mm_raw": h1_raw, "h2_mm_raw": h2_raw, "h3_mm_raw": h3_raw, "h4_mm_raw": h4_raw,
        # 其他字段 (兼容旧 ETL, 模拟值)
        "media": random.choice(["Glass Fiber", "Cellulose", "Synthetic", "Pleated Paper"]) if random.random() < 0.7 else None,
        "is_discontinued": False,
    }


def gen_xrefs_for_product(product: dict, n: int, start_seq: int) -> list:
    """为单个产品生成 n 个交叉引用 (V2 字段全集)
       关联主键用 mr_1 (替代 product_oem), 与 EtlImportService.ProcessXrefBatchAsync 对齐
       WHY 用 n 参数 (而非随机): 由 distribute_counts 预分配, 保证每个产品至少 min 个, 总数精确
    """
    refs = []
    used_no3 = set()
    for i in range(n):
        brand = random.choices(OEM_BRANDS, OEM_BRAND_WEIGHTS)[0]
        # WHY seq = start_seq + i + 1: 保证 OEM 3 全表唯一 (300 个不重复)
        no3 = gen_oem_no_3(brand, start_seq + i + 1)
        if no3 in used_no3:
            continue
        used_no3.add(no3)
        # machine_type 枚举 (V2 Task 5.1.7): 5% 故意留空, 其余均分 5 类
        if random.random() < 0.05:
            machine_type = None
        else:
            machine_type = random.choice(MACHINE_CATEGORIES)
        refs.append({
            # V2 关联主键 (替代 product_oem)
            "mr_1": product["mr_1"],
            "product_name_1": product["product_name_1"] or product["type"],
            "oem_brand": brand,
            "oem_no_3": no3,
            # V2 新增字段
            "oem_2": gen_oem_2(start_seq + i),
            "sort_order": random.randint(0, 100),  # 类竞价排名, 0-100
            "machine_type": machine_type,
            "is_published": random.random() > 0.10,  # 10% 不发布 (验证 is_published 过滤)
        })
    return refs


def gen_apps_for_product(product: dict, n: int, start_seq: int) -> list:
    """为单个产品生成 n 个机型适配 (V2 字段全集)
       关联主键用 mr_1 (替代 product_oem), 与 EtlImportService.ImportAppsAsync 对齐
    """
    apps = []
    used = set()
    for i in range(n):
        brand = random.choices(MACHINE_BRANDS, MACHINE_BRAND_WEIGHTS)[0]
        model = f"{brand[:2]}{random.randint(100, 9999)}"
        if (brand, model) in used:
            continue
        used.add((brand, model))
        # machine_category 枚举 (V2): 5% 留空, 其余 5 类均分
        if random.random() < 0.05:
            machine_category = None
        else:
            machine_category = random.choice(MACHINE_CATEGORIES)
        # 生产日期 50% 概率存在
        if random.random() < 0.5:
            year = random.randint(1995, 2025)
            month = random.randint(1, 12)
            prod_date = f"{year}-{month:02d}-01"
        else:
            prod_date = None
        apps.append({
            # V2 关联主键 (替代 product_oem)
            "mr_1": product["mr_1"],
            "machine_brand": brand,
            "machine_model": model,
            "model_name": random.choice(["Excavator", "Loader", "Tractor", "Truck", "Generator"]),
            "engine_brand": random.choice(["PERKINS", "CUMMINS", "DEUTZ", "CATERPILLAR", None]),
            "engine_type": f"{random.choice('ABCDEF')}{random.randint(100, 999)}-{random.randint(10, 99)}" if random.random() < 0.5 else None,
            "engine_energy": random.choices(ENGINES, ENGINE_WEIGHTS)[0] if random.random() < 0.6 else None,
            "production_date_start": prod_date,
            "is_ongoing": random.random() > 0.20,  # 20% 已停产
            # V2 新增字段
            "machine_category": machine_category,
        })
    return apps


def distribute_counts(n_products: int, target: int, min_n: int, max_n: int) -> list[int]:
    """均匀分配 target 到 n_products 个产品, 每个在 [min_n, max_n]
       WHY: 保证每个产品至少 min_n 个, 总数精确 = target
       算法: 先保底 min_n, 剩余配额随机加到产品上 (不超过 max_n)
    """
    counts = [min_n] * n_products
    remaining = target - sum(counts)
    # 随机分配剩余配额, 每次给随机产品 +1 (不超过 max_n)
    # WHY 不用 shuffle: 大量产品时 shuffle 浪费, 用 random.randint 随机选索引更高效
    guard = 0
    while remaining > 0 and guard < target * 10:
        guard += 1
        idx = random.randint(0, n_products - 1)
        if counts[idx] < max_n:
            counts[idx] += 1
            remaining -= 1
    if remaining > 0:
        # 极端情况: 所有产品都到 max_n 了, 但 remaining > 0 (target > n_products * max_n)
        # 这种情况说明 target 设置不合理, 此处只截断不报错
        print(f"[WARN] distribute_counts: target={target} 超过上限 {n_products * max_n}, 实际分配 {sum(counts)}",
              file=sys.stderr)
    return counts


# ========== 图片占位生成 ==========

def make_placeholder_png(text: str, size: tuple[int, int] = (400, 400)) -> bytes:
    """生成简单的 PNG 占位图 (灰底 + 居中文字)
       WHY 不用 Pillow 直接绘图: 减少依赖. 但 Pillow 仍是首选, 缺失时退化为纯色 PNG.
    """
    try:
        from PIL import Image, ImageDraw, ImageFont
        img = Image.new("RGB", size, color=(220, 220, 220))
        draw = ImageDraw.Draw(img)
        # 居中绘制文字
        try:
            font = ImageFont.load_default()
        except Exception:
            font = None
        if font:
            bbox = draw.textbbox((0, 0), text, font=font)
            tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
            x = (size[0] - tw) // 2
            y = (size[1] - th) // 2
            draw.text((x, y), text, fill=(60, 60, 60), font=font)
        buf = io.BytesIO()
        img.save(buf, format="PNG")
        return buf.getvalue()
    except ImportError:
        # 退化: 返回最小 PNG (1x1 灰像素) — 仅用于无 Pillow 环境
        # WHY 不抛异常: 图片上传是次要功能, jsonl 生成是主目标
        return bytes.fromhex(
            "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489"
            "0000000d49444154789c63f8cf00000001000100537f25a50000000049454e44ae426082"
        )


# ========== MinIO 上传 ==========

def upload_to_minio(objects: list[tuple[str, bytes]], endpoint: str, access_key: str,
                    secret_key: str, bucket: str, use_ssl: bool) -> tuple[int, int]:
    """上传对象列表到 MinIO, 返回 (成功数, 失败数)
       WHY 失败不抛异常: 单个对象上传失败不应中断整体流程, 调用方根据失败数决定是否重试
    """
    try:
        from minio import Minio
        from minio.error import S3Error
    except ImportError:
        print("[WARN] minio SDK 未安装, 跳过上传. 安装: pip install minio", file=sys.stderr)
        return 0, len(objects)

    client = Minio(endpoint, access_key=access_key, secret_key=secret_key, secure=use_ssl)
    # 确保 bucket 存在
    try:
        if not client.bucket_exists(bucket):
            client.make_bucket(bucket)
            print(f"[INFO] 已创建 bucket: {bucket}")
    except S3Error as e:
        print(f"[ERROR] MinIO bucket 检查/创建失败: {e}", file=sys.stderr)
        return 0, len(objects)

    success = 0
    failed = 0
    for key, data in objects:
        try:
            client.put_object(
                bucket_name=bucket,
                object_name=key,
                data=io.BytesIO(data),
                length=len(data),
                content_type="image/png",
            )
            success += 1
        except S3Error as e:
            print(f"[WARN] 上传失败 key={key}: {e}", file=sys.stderr)
            failed += 1
    return success, failed


# ========== 数据关系验证 (Task 5.2.3) ==========

def verify_relations(products: list, xrefs: list, apps: list, image_keys: list[str]) -> list[str]:
    """验证数据关系完整性, 返回错误信息列表 (空列表 = 通过)"""
    errors = []
    mr1_set = {p["mr_1"] for p in products}
    oem3_set = {x["oem_no_3"] for x in xrefs}

    # 1. MR.1 一对多 OEM 3 (每个 mr_1 至少 2 个 oem3)
    xrefs_by_mr1 = defaultdict(set)
    for x in xrefs:
        xrefs_by_mr1[x["mr_1"]].add(x["oem_no_3"])
    for mr1 in mr1_set:
        if len(xrefs_by_mr1[mr1]) < 2:
            errors.append(f"MR.1 {mr1} 关联 OEM 3 数 < 2 ({len(xrefs_by_mr1[mr1])})")

    # 2. xrefs 中 mr_1 必须在 products 中存在
    for x in xrefs:
        if x["mr_1"] not in mr1_set:
            errors.append(f"xref 行 mr_1={x['mr_1']} 不在 products 中")
            break

    # 3. apps 中 mr_1 必须在 products 中存在
    for a in apps:
        if a["mr_1"] not in mr1_set:
            errors.append(f"app 行 mr_1={a['mr_1']} 不在 products 中")
            break

    # 4. OEM 3 一对一主图 (每个 oem3 对应一张主图 key)
    primary_keys = {k for k in image_keys if "/primary/" in k}
    for oem3 in oem3_set:
        expected_key = f"products/primary/{oem3}/{oem3}-1.png"
        if expected_key not in primary_keys:
            errors.append(f"OEM 3 {oem3} 缺少主图 key={expected_key}")
            break

    # 5. MR.1 一对多详情图 (每个 mr1 至少 2 张详情图 slot=2,3)
    detail_keys_by_mr1 = defaultdict(set)
    for k in image_keys:
        if "/detail/" in k:
            # 解析 products/detail/{mr1}/{mr1}-{slot}.png
            #   split("/") = ["products", "detail", "{mr1}", "{mr1}-{slot}.png"]
            #   mr1 在 index 2 (不是 3)
            parts = k.split("/")
            if len(parts) >= 4:
                mr1 = parts[2]
                detail_keys_by_mr1[mr1].add(k)
    for mr1 in mr1_set:
        if len(detail_keys_by_mr1[mr1]) < 2:
            errors.append(f"MR.1 {mr1} 详情图数 < 2 ({len(detail_keys_by_mr1[mr1])})")
            break

    # 6. machine_category 覆盖 5 类 (apps 中)
    cats_in_data = {a["machine_category"] for a in apps if a["machine_category"]}
    missing_cats = set(MACHINE_CATEGORIES) - cats_in_data
    if missing_cats:
        errors.append(f"machine_category 未覆盖 5 类, 缺失: {missing_cats}")

    return errors


# ========== 主流程 ==========

def main():
    parser = argparse.ArgumentParser(description="V2 模拟数据生成器 (Task 5.2)")
    parser.add_argument("--skip-upload", action="store_true", help="跳过 MinIO 上传, 仅生成 jsonl")
    parser.add_argument("--out-dir", default="spike-test/output/v2_mock",
                        help="输出目录 (默认 spike-test/output/v2_mock)")
    args = parser.parse_args()

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    t0 = time.time()
    print(f"=== V2 模拟数据生成 (seed={SEED}) ===")
    print(f"输出目录: {out_dir}")

    # ========== 1. 生成 jsonl ==========
    N_PRODUCTS = 100
    TARGET_XREFS = 300
    TARGET_APPS = 500

    products_path = out_dir / "mock_products_v2.jsonl"
    xrefs_path = out_dir / "mock_xrefs_v2.jsonl"
    apps_path = out_dir / "mock_apps_v2.jsonl"

    products = []
    xrefs = []
    apps = []

    print(f"\n[1] 生成 {N_PRODUCTS} 产品 / {TARGET_XREFS} xrefs / {TARGET_APPS} apps ...")
    # 先生成全部产品
    for i in range(1, N_PRODUCTS + 1):
        p = gen_product(i)
        products.append(p)

    # V2 修复: 预分配每个产品的 xref/app 数量, 保证每个 mr1 至少 2 个 xref / 3 个 app
    #   之前 bug: 按 [2,5] 随机分配, 前 90 个产品就用完了 300 配额, 最后 10 个产品 0 xref
    #   修复: distribute_counts 先保底 min_n, 再随机补齐到 target, 保证每个产品都有 xref
    xref_counts = distribute_counts(N_PRODUCTS, TARGET_XREFS, min_n=2, max_n=5)
    app_counts = distribute_counts(N_PRODUCTS, TARGET_APPS, min_n=3, max_n=8)
    print(f"    预分配: xref_counts 范围 [{min(xref_counts)}, {max(xref_counts)}] 总和 {sum(xref_counts)}")
    print(f"    预分配: app_counts  范围 [{min(app_counts)}, {max(app_counts)}] 总和 {sum(app_counts)}")

    for i in range(N_PRODUCTS):
        p = products[i]
        xrefs.extend(gen_xrefs_for_product(p, n=xref_counts[i], start_seq=len(xrefs)))
        apps.extend(gen_apps_for_product(p, n=app_counts[i], start_seq=len(apps)))

    with open(products_path, "w", encoding="utf-8") as fp:
        for p in products:
            fp.write(json.dumps(p, ensure_ascii=False) + "\n")
    with open(xrefs_path, "w", encoding="utf-8") as fx:
        for x in xrefs:
            fx.write(json.dumps(x, ensure_ascii=False) + "\n")
    with open(apps_path, "w", encoding="utf-8") as fa:
        for a in apps:
            fa.write(json.dumps(a, ensure_ascii=False) + "\n")

    print(f"    products: {len(products)} 行 -> {products_path}")
    print(f"    xrefs:    {len(xrefs)} 行 -> {xrefs_path}")
    print(f"    apps:     {len(apps)} 行 -> {apps_path}")

    # ========== 2. 生成图片占位 ==========
    print(f"\n[2] 生成图片占位 (300 主图 + 200 详情图) ...")
    image_objects: list[tuple[str, bytes]] = []  # (key, png_bytes)
    image_keys: list[str] = []

    # 2a. 300 张主图: 每个 OEM 3 一张
    oem3_list = [x["oem_no_3"] for x in xrefs]
    for oem3 in oem3_list:
        key = f"products/primary/{oem3}/{oem3}-1.png"
        data = make_placeholder_png(oem3)
        image_objects.append((key, data))
        image_keys.append(key)

    # 2b. 200 张详情图: 每个 MR.1 2 张 (slot=2, 3)
    for p in products:
        mr1 = p["mr_1"]
        for slot in (2, 3):
            key = f"products/detail/{mr1}/{mr1}-{slot}.png"
            data = make_placeholder_png(f"{mr1}-{slot}")
            image_objects.append((key, data))
            image_keys.append(key)

    print(f"    主图: {len(oem3_list)} 张")
    print(f"    详情图: {len(products) * 2} 张")
    print(f"    总计: {len(image_objects)} 张")

    # ========== 3. 数据关系验证 ==========
    print(f"\n[3] 数据关系验证 ...")
    errors = verify_relations(products, xrefs, apps, image_keys)
    if errors:
        print(f"    [FAIL] 发现 {len(errors)} 个关系错误:")
        for e in errors[:10]:
            print(f"      - {e}")
        if len(errors) > 10:
            print(f"      ... 还有 {len(errors) - 10} 个错误")
        # WHY 不退出: 让用户看到完整错误后自行决定, 同时保留 jsonl 供调试
    else:
        print(f"    [OK] 所有关系验证通过")

    # 统计报告
    cat_counter = Counter(a["machine_category"] for a in apps if a["machine_category"])
    print(f"\n[4] 数据统计:")
    print(f"    machine_category 分布: {dict(cat_counter)}")
    print(f"    xrefs 中 is_published=True: {sum(1 for x in xrefs if x['is_published'])}/{len(xrefs)}")
    print(f"    apps 中 is_ongoing=True: {sum(1 for a in apps if a['is_ongoing'])}/{len(apps)}")

    # ========== 4. MinIO 上传 ==========
    if args.skip_upload:
        print(f"\n[5] 跳过 MinIO 上传 (--skip-upload)")
    else:
        endpoint = os.environ.get("MINIO_ENDPOINT", "localhost:9000")
        access_key = os.environ.get("MINIO_ACCESS_KEY")
        secret_key = os.environ.get("MINIO_SECRET_KEY")
        bucket = os.environ.get("MINIO_BUCKET", "sakurafilter")
        use_ssl = os.environ.get("MINIO_USE_SSL", "false").lower() == "true"

        if not access_key or not secret_key:
            print(f"\n[5] MinIO 上传跳过: 缺少 MINIO_ACCESS_KEY 或 MINIO_SECRET_KEY 环境变量")
            print(f"    (如需上传, 请先 source .env 或 set 这些变量; 或使用 --skip-upload 明确跳过)")
        else:
            print(f"\n[5] 上传 {len(image_objects)} 张图片到 MinIO ({endpoint}/{bucket}) ...")
            t_upload = time.time()
            success, failed = upload_to_minio(
                image_objects, endpoint, access_key, secret_key, bucket, use_ssl
            )
            print(f"    上传完成: 成功 {success}, 失败 {failed}, 耗时 {time.time() - t_upload:.1f}s")

    # ========== 5. 生成 manifest ==========
    manifest = {
        "seed": SEED,
        "generated_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "counts": {
            "products": len(products),
            "xrefs": len(xrefs),
            "apps": len(apps),
            "primary_images": len(oem3_list),
            "detail_images": len(products) * 2,
        },
        "files": {
            "products": str(products_path),
            "xrefs": str(xrefs_path),
            "apps": str(apps_path),
        },
        "machine_category_distribution": dict(cat_counter),
        "verification_errors": errors,
        "skip_upload": args.skip_upload,
    }
    manifest_path = out_dir / "_manifest.json"
    with open(manifest_path, "w", encoding="utf-8") as fm:
        json.dump(manifest, fm, ensure_ascii=False, indent=2)
    print(f"\n[6] Manifest: {manifest_path}")

    print(f"\n=== 完成, 总耗时 {time.time() - t0:.1f}s ===")
    if errors:
        sys.exit(1)  # 关系验证失败时返回非零退出码, 便于 CI 检测


if __name__ == "__main__":
    main()
