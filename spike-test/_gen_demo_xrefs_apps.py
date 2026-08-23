# 演示数据生成器: 基于现有客户产品 (products.jsonl) 生成模拟 OEM 替代 + 机型适配
# 用法: python _gen_demo_xrefs_apps.py
# 输出: output/cleaned_customer/xrefs.jsonl + apps.jsonl (覆盖, ETL 格式)
# 设计:
#   - 产品主数据保留客户真实数据 (1949 条), 仅模拟关联数据 (xrefs/apps) — 演示前台 OEM/机型搜索
#   - 唯一约束 (演练实证): xrefs 全局 (oem_brand, oem_no_3) 唯一; apps 产品内 (machine_brand, machine_model) 唯一
#   - machine_type 不写 (null 允许, 合法枚举仅 agriculture/commercial/construction/industrial/others)
#   - is_published=true (演示数据默认上架)
# 客户数据到位后: 用真实 Excel full-load 重导替换 (流程不变)
import json
import random
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, str(Path(__file__).parent))
from _gen_source_xlsx import (  # noqa: E402  复用品牌/机型池与 OEM NO.3 生成
    MACHINE_BRANDS, MACHINE_BRAND_WEIGHTS, MODEL_NAMES,
    ENGINE_BRANDS, ENGINE_BRAND_WEIGHTS, ENGINE_ENERGIES, ENGINE_ENERGY_WEIGHTS,
    OEM_BRANDS, OEM_BRAND_WEIGHTS, gen_oem_no_3, gen_production_date,
)

PRODUCTS_JSONL = Path(__file__).parent / "output" / "cleaned_customer" / "products.jsonl"
OUT_DIR = Path(__file__).parent / "output" / "cleaned_customer"

USED_OEM3: set[tuple[str, str]] = set()  # 全局 (brand, oem3) 唯一


def gen_xrefs(mr1: str) -> list[dict]:
    """为一个产品生成 5-20 个 OEM 替代 (ETL 格式)"""
    n = random.randint(5, 20)
    rows = []
    for _ in range(n):
        brand = random.choices(OEM_BRANDS, OEM_BRAND_WEIGHTS)[0]
        while True:
            oem3 = gen_oem_no_3(brand, random.randint(1, 999999))
            if (brand, oem3) not in USED_OEM3:
                USED_OEM3.add((brand, oem3))
                break
        rows.append({
            "mr_1": mr1,
            "is_published": True,
            "product_name_1": None,  # 演示数据不造产品名 (xref 可空, ETL 允许)
            "oem_brand": brand,
            "oem_no_3": oem3,
            "oem_2": None,
            "sort_order": 0,
            "machine_type": None,
        })
    return rows


def gen_apps(mr1: str) -> list[dict]:
    """为一个产品生成 1-30 个机型适配 (ETL 格式, 产品内 brand+model 唯一)"""
    n = random.randint(1, 30)
    used: set[tuple[str, str]] = set()
    rows = []
    for _ in range(n):
        while True:
            brand = random.choices(MACHINE_BRANDS, MACHINE_BRAND_WEIGHTS)[0]
            model = f"M{random.randint(100, 999)}"
            if (brand, model) not in used:
                used.add((brand, model))
                break
        start, ongoing = gen_production_date_iso()
        rows.append({
            "mr_1": mr1,
            "machine_brand": brand,
            "machine_model": model,
            "model_name": random.choice(MODEL_NAMES),
            "engine_brand": random.choices(ENGINE_BRANDS, ENGINE_BRAND_WEIGHTS)[0],
            "engine_type": f"E{random.randint(1, 99)}",
            "engine_energy": random.choices(ENGINE_ENERGIES, ENGINE_ENERGY_WEIGHTS)[0],
            "production_date_start": start if start else None,
            "is_ongoing": ongoing,
        })
    return rows


def gen_production_date_iso() -> tuple[str | None, bool]:
    """生成生产日期 (ISO 格式 + is_ongoing), 替代 _gen_source_xlsx 的 '>' 后缀格式"""
    year = random.randint(2005, 2024)
    month = random.randint(1, 12)
    day = random.randint(1, 28)
    start = f"{year}-{month:02d}-{day:02d}"
    ongoing = random.random() < 0.6  # 60% 持续生产
    return start, ongoing


def main() -> None:
    mr1s = []
    seen = set()
    with open(PRODUCTS_JSONL, encoding="utf-8") as f:
        for line in f:
            mr1 = json.loads(line).get("mr_1")
            # 去重: products.jsonl 含客户数据复制行 (同 mr_1 多行), 仅按唯一 mr_1 生成关联
            if mr1 and mr1 not in seen:
                seen.add(mr1)
                mr1s.append(mr1)
    print(f"产品数: {len(mr1s)}", flush=True)

    xrefs, apps = [], []
    for i, mr1 in enumerate(mr1s, 1):
        xrefs.extend(gen_xrefs(mr1))
        apps.extend(gen_apps(mr1))
        if i % 500 == 0:
            print(f"  进度 {i}/{len(mr1s)} (xrefs={len(xrefs)}, apps={len(apps)})", flush=True)

    for name, items in [("xrefs", xrefs), ("apps", apps)]:
        path = OUT_DIR / f"{name}.jsonl"
        with open(path, "w", encoding="utf-8") as f:
            for item in items:
                f.write(json.dumps(item, ensure_ascii=False) + "\n")
        print(f"写出 {path}: {len(items)} 行", flush=True)

    # 唯一性校验
    from collections import Counter
    dup_x = sum(1 for _, c in Counter((x["oem_brand"], x["oem_no_3"]) for x in xrefs).items() if c > 1)
    dup_a = sum(1 for _, c in Counter((a["mr_1"], a["machine_brand"], a["machine_model"]) for a in apps).items() if c > 1)
    print(f"唯一性: xrefs 重复 {dup_x} (应 0), apps 重复 {dup_a} (应 0)", flush=True)


if __name__ == "__main__":
    main()
