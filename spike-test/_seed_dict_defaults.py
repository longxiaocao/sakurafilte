# -*- coding: utf-8 -*-
"""Day 10+ P2.3 seed 脚本: dict_type 默认值 + dict_machine 4 大类分配

任务 P2.3 (Task 8.1):
  1) dict_type: 固定 5 值 (oil/fuel/air/cabin/others) ON CONFLICT DO UPDATE SET sort_order
     排序按 P2.3 计划: oil=1, fuel=2, air=3, cabin=4, others=99
  2) dict_machine: 已有 brand 按规则分配 category (4 大类)
     - Agriculture 关键词: tractor, agri, farm, kubota, john deere, case ih, new holland, fendt, massey
     - Commercial  关键词: truck, volvo, scania, mercedes, man, iveco, daf, renault, hino, isuzu, daihatsu
     - Construction 关键词: excavator, loader, bulldozer, caterpillar, cat, komatsu, hitachi, jcb, doosan, volvo ce, kubota ce
     - 其他 brand: 默认 'others'
     - 大小写不敏感, 关键词匹配 (substring)
  3) 独立运行: python _seed_dict_defaults.py
  4) 幂等: 可重复执行, dict_type 用 ON CONFLICT (type) DO UPDATE SET sort_order
                   dict_machine 只更新 category 字段, 不动其它字段

依赖: dict_machine.machine_category 列已通过 EF Migration AddMachineCategory 添加
  (运行前需先启动后端一次, 让 Migrate() 自动应用, 或手动 psql 应用)
"""
import re
import sys
import psycopg2

PG = dict(host="localhost", port=5432, dbname="spike_test_v3",
          user="postgres", password="784533")

# ========== 1) dict_type seed ==========
# P2.3 排序: oil=1, fuel=2, air=3, cabin=4, others=99
#   others=99 让其永远排最后, 兼容 5 值扩展
DEFAULTS_TYPE = [
    ("oil", 1),
    ("fuel", 2),
    ("air", 3),
    ("cabin", 4),
    ("others", 99),
]

# ========== 2) dict_machine category 关键词分类 ==========
# 关键词匹配 (大小写不敏感, substring), 命中即归入对应 category
# WHY 关键词而非精确匹配: dict_machine 现有 brand 字符串差异大 (大小写、空格、型号混入), 关键词覆盖率更高
CATEGORY_KEYWORDS = {
    "Agriculture": [
        "tractor", "agri", "farm", "kubota", "john deere", "case ih",
        "new holland", "fendt", "massey", "claas", "same", "lamborghini",
        "mccormick", "yanmar", "iseki", "shibaura",
    ],
    "Commercial": [
        "truck", "volvo truck", "scania", "mercedes", "man", "iveco",
        "daf truck", "renault truck", "hino", "isuzu truck", "daihatsu",
        "fuso", "ud truck", "volvo fh", "volvo fm",
    ],
    "Construction": [
        "excavator", "loader", "bulldozer", "caterpillar", "cat ",
        "komatsu", "hitachi", "jcb", "doosan", "volvo ce", "volvo construction",
        "kubota construction", "kubota ce", "hyundai", "kobelco", "sumitomo",
        "takeuchi", "bobcat", "case ce",
    ],
}

VALID_CATEGORIES = {"Agriculture", "Commercial", "Construction", "others"}


def classify_brand(brand: str) -> str:
    """根据 brand 字符串分类为 4 大类, 默认 'others'"""
    if not brand:
        return "others"
    b = brand.lower().strip()
    # 按 Commercial/Agriculture/Construction 顺序匹配, 避免 volvo 同时匹配 truck + ce 的歧义
    # WHY Commercial 优先: volvo truck 应归 Commercial, volvo ce 应归 Construction
    for cat in ("Commercial", "Agriculture", "Construction"):
        for kw in CATEGORY_KEYWORDS[cat]:
            if kw.lower() in b:
                return cat
    return "others"


def seed_dict_type(cur, conn) -> dict:
    """dict_type seed 5 个默认值, ON CONFLICT (type) DO UPDATE SET sort_order

    Day 11 fix v1: 推后 sort_order=0 的历史脏数据
    WHY: 历史 40+ 行 dict_type sort_order=0 (EF Core HasDefaultValue(0) 默认),
         后端 by-type 端点 ORDER BY sort_order ASC 把它们排到前 5 个,
         导致 case 2 期望 ["oil","fuel","air","cabin","others"] 失败
         实际返 ["ACTIVATED CARBON FILTER", "Air", "AIR DRYER", "AIR FILTER", "AIR/OIL SEPARATOR"]
    策略: 把 sort_order=0 的非 P2.3 行推后到 100+ (按 id ASC 分配),
         保证 P2.3 五类 (sort_order 1/2/3/4/99) 永远排前面
    """
    cur.execute("SELECT COUNT(*) FROM dict_type")
    before = cur.fetchone()[0]
    inserted = 0
    updated = 0
    unchanged = 0
    for v, so in DEFAULTS_TYPE:
        # 1) 查当前 sort_order
        cur.execute("SELECT sort_order FROM dict_type WHERE type = %s", (v,))
        row = cur.fetchone()
        if row is None:
            cur.execute("""
                INSERT INTO dict_type (type, sort_order, created_at, updated_at)
                VALUES (%s, %s, now(), now())
            """, (v, so))
            inserted += 1
        elif row[0] != so:
            cur.execute("""
                UPDATE dict_type SET sort_order = %s, updated_at = now()
                WHERE type = %s
            """, (so, v))
            updated += 1
        else:
            unchanged += 1
    # 2) 推后 sort_order=0 的历史脏数据 (非 P2.3 五类)
    #    用 100 + id 保证 id 小的行 sort_order 也小, 顺序稳定
    #    排除 5 个 P2.3 canonical type, 排除 deleted_at IS NOT NULL
    p23_types = tuple(t for t, _ in DEFAULTS_TYPE)
    cur.execute("""
        UPDATE dict_type
        SET sort_order = 100 + id, updated_at = now()
        WHERE deleted_at IS NULL
          AND sort_order = 0
          AND type NOT IN %s
    """, (p23_types,))
    moved = cur.rowcount
    conn.commit()
    cur.execute("SELECT COUNT(*) FROM dict_type")
    after = cur.fetchone()[0]
    return {
        "before": before, "after": after,
        "inserted": inserted, "updated": updated, "unchanged": unchanged,
        "moved_zero": moved,  # 推后 sort_order=0 的历史行数
    }


def seed_dict_machine_category(cur, conn) -> dict:
    """dict_machine 已有 brand 按规则分配 category, 跳过 category 已正确分类的行"""
    # 1) 统计目标 (排除 deleted_at IS NOT NULL)
    cur.execute("SELECT COUNT(*) FROM dict_machine WHERE deleted_at IS NULL")
    total = cur.fetchone()[0]
    # 2) 拉所有 active brand
    cur.execute("""
        SELECT id, machine_brand, machine_category
        FROM dict_machine
        WHERE deleted_at IS NULL
    """)
    rows = cur.fetchall()
    updated = 0
    unchanged = 0
    breakdown = {c: 0 for c in VALID_CATEGORIES}
    for (rid, brand, current_cat) in rows:
        target = classify_brand(brand)
        breakdown[target] += 1
        if current_cat != target:
            cur.execute("""
                UPDATE dict_machine
                SET machine_category = %s, updated_at = now()
                WHERE id = %s
            """, (target, rid))
            updated += 1
        else:
            unchanged += 1
    conn.commit()
    return {
        "total": total, "updated": updated, "unchanged": unchanged,
        "breakdown": breakdown,
    }


def main():
    print(f"=== Day 10+ P2.3 dict defaults seed ===")
    conn = psycopg2.connect(**PG)
    cur = conn.cursor()
    # 1) dict_type
    print(f"\n--- dict_type ---")
    print(f"  defaults = {DEFAULTS_TYPE}")
    res_type = seed_dict_type(cur, conn)
    print(f"  table before/after  = {res_type['before']} / {res_type['after']}")
    print(f"  inserted (new)      = {res_type['inserted']}")
    print(f"  updated (sortOrder) = {res_type['updated']}")
    print(f"  unchanged           = {res_type['unchanged']}")
    print(f"  moved sort_order=0  = {res_type['moved_zero']} (历史脏数据推后到 100+)")
    # 验证最终排序
    cur.execute("""
        SELECT type, sort_order FROM dict_type
        WHERE deleted_at IS NULL
        ORDER BY sort_order, type
    """)
    types = cur.fetchall()
    print(f"  final order (active):")
    for t, so in types:
        print(f"    sort={so:>3}  type={t}")

    # 2) dict_machine category
    print(f"\n--- dict_machine category ---")
    res_machine = seed_dict_machine_category(cur, conn)
    print(f"  total (active)      = {res_machine['total']}")
    print(f"  updated             = {res_machine['updated']}")
    print(f"  unchanged           = {res_machine['unchanged']}")
    print(f"  breakdown:")
    for cat in sorted(VALID_CATEGORIES, key=lambda c: (c == "others", c)):
        print(f"    {cat:<15} = {res_machine['breakdown'][cat]}")
    conn.close()
    print(f"\n=== done ===")
    return 0


if __name__ == "__main__":
    sys.exit(main())
