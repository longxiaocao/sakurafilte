# -*- coding: utf-8 -*-
"""Day 10 baseline 修复 v3: 写入 PascalCase __EFMigrationsHistory (EF Core 实际查的表)

为什么 v1/v2 失败:
  - v1 seed 到 __efmigrationshistory (lowercase, 漏下划线)
  - v2 seed 到 __ef_migrations_history (snake_case)
  - 两者都不是 EF Core 8 实际查的表 — EF Core 查 "__EFMigrationsHistory" (PascalCase, quoted, case-sensitive)
  - UseSnakeCaseNamingConvention() 不改内置表名 (它由 MigrationsAssembly 硬编码)

验证: pg 表 __EFMigrationsHistory (quoted) 是 EF Core 唯一会查的表
  - 历史 SQL migration 创建此表时只有 1 行 (InitialCreate)
  - 我需要再插入 3 行: AddUniqueIndexOnOemNoNormalized / AddIsPublishedDefaultTrue / AddProductsDefaultsV9
  - 不插入 AddXrefOemBrandDict,让 EF Core Migrate 实际执行
"""
import psycopg2

conn = psycopg2.connect(
    host="localhost", port=5432, dbname="spike_test_v3",
    user="postgres", password="784533"
)
cur = conn.cursor()

CORRECT_TABLE = "__EFMigrationsHistory"  # PascalCase, quoted
# v2 创建的错表清理 (snake_case)
WRONG_SNAKE = "__ef_migrations_history"

# 1) 清理 v2 错表
cur.execute(f"DROP TABLE IF EXISTS {WRONG_SNAKE}")
print(f"  [cleanup] 删 v2 错表 {WRONG_SNAKE}")

# 2) 确保 PascalCase 表存在 (历史 SQL migration 018 已建)
cur.execute(f"""
    CREATE TABLE IF NOT EXISTS "{CORRECT_TABLE}" (
        migration_id varchar(150) NOT NULL,
        product_version varchar(32) NOT NULL,
        CONSTRAINT "PK_{CORRECT_TABLE}" PRIMARY KEY (migration_id)
    )
""")
conn.commit()

# 3) Seed 3 个老 migration (Insert 跳过 InitialCreate,因已存在)
to_seed = [
    ("20260702051540_AddUniqueIndexOnOemNoNormalized", "8.0.10"),
    ("20260702052101_AddIsPublishedDefaultTrue", "8.0.10"),
    ("20260702052628_AddProductsDefaultsV9", "8.0.10"),
]
# AddXrefOemBrandDict 故意不 seed,让 EF Core Migrate 实际执行
for mid, ver in to_seed:
    cur.execute(f"""
        INSERT INTO "{CORRECT_TABLE}" (migration_id, product_version)
        VALUES (%s, %s)
        ON CONFLICT (migration_id) DO NOTHING
    """, (mid, ver))
    print(f"  [seed] {mid} → {'inserted' if cur.rowcount else 'exists'}")
conn.commit()

# 4) 验证
cur.execute(f'SELECT migration_id, product_version FROM "{CORRECT_TABLE}" ORDER BY migration_id')
print(f'\n{CORRECT_TABLE} (PascalCase) 当前 ({cur.rowcount} 行):')
for r in cur.fetchall():
    print(f'  - {r[0]}  v={r[1]}')

# 5) 检查 xref_oem_brand
cur.execute("""
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables WHERE table_name = 'xref_oem_brand'
    )
""")
print(f'\nxref_oem_brand: {"已存在" if cur.fetchone()[0] else "不存在 (待 AddXrefOemBrandDict 创建)"}')

conn.close()
print("\nv3 baseline seed 完成. 重启后端, EF Core Migrate 应只跑 AddXrefOemBrandDict")
