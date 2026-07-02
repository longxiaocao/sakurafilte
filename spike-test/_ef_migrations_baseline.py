# -*- coding: utf-8 -*-
"""Day 10+ P0.2: EF Core Migrations baseline seed (产品化通用版)

背景 (从 Day 10 v3 升级):
  本地 spike_test_v3 跑 EF Core Migrate 时, 4 个老 migration (InitialCreate /
  AddUniqueIndexOnOemNoNormalized / AddIsPublishedDefaultTrue / AddProductsDefaultsV9)
  试图 DROP INDEX / ALTER COLUMN 已存在的 products 表, 索引名/列名都与 SQL
  migration 创建的不一致:
    - EF Core 默认 `ix_*` 前缀, SQL migration 用 `idx_*` / `uq_*`
    - EF Core 8 + EFCore.NamingConventions 不改内置表名, 仍写 PascalCase
      `__EFMigrationsHistory`, 而历史 SQL migration 18 也是 PascalCase,
      索引/列名却完全不同 (DB 实际存在的是 SQL 版的)

解决方案: 手工在 PascalCase `__EFMigrationsHistory` 插入 4 行已应用记录,
  让 EF Core Migrate 跳过老 migration, 只跑新加的 `AddXrefOemBrandDict` 等。

本脚本 (v4 通用版) 特性:
  - 参数化 `--migrations <id1,id2,...>` (默认 4 个老 migration)
  - 参数化 `--product-version` (默认 8.0.10)
  - 参数化 PG 连接 (`--pg-host/--pg-port/--pg-db/--pg-user/--pg-password`)
  - `--dry-run` 仅打印 SQL 不执行
  - 内部:
      1) 清理错误位置的 snake_case 表 (`__efmigrationshistory` / `__ef_migrations_history`)
      2) 创建 PascalCase `__EFMigrationsHistory` 表 (如不存在)
      3) INSERT 4 个老 migration (ON CONFLICT DO NOTHING, 幂等)
      4) 打印 verbose 进度
  - 退出码: 0=成功, 1=参数错, 2=DB 连接失败

用法 (本地):
  python spike-test/_ef_migrations_baseline.py
  python spike-test/_ef_migrations_baseline.py --dry-run
  python spike-test/_ef_migrations_baseline.py --migrations 20260702025150_InitialCreate,... --pg-db=spike_test_v3

用法 (CI):
  步骤 1: services.postgres 启动
  步骤 2: Run this script (默认参数, 全新 DB)
  步骤 3: dotnet run (EF Core Migrate 只跑新 migration)

回滚 (强制 EF Core 重跑全部 migration):
  DROP TABLE "__EFMigrationsHistory";
  dotnet run   # EF Core Migrate 会重跑所有 migration
"""
import argparse
import sys
from typing import List

import psycopg2

# === 默认 4 个老 migration (Day 10 反复踩坑的根因) ===
# EF Core 8 + EFCore.NamingConventions 不会改以下表名, 仍查 PascalCase __EFMigrationsHistory
# SQL migration 18 已经建过这个表 (含 InitialCreate 1 行), 但 SQL migration 创建的索引/列名
# 与 EF Core 8 期望的不一致 (ix_ vs idx_, uq_ vs IX_), 所以 EF Core 一上来就 DROP/ALTER 失败.
DEFAULT_MIGRATIONS = [
    "20260702025150_InitialCreate",
    "20260702051540_AddUniqueIndexOnOemNoNormalized",
    "20260702052101_AddIsPublishedDefaultTrue",
    "20260702052628_AddProductsDefaultsV9",
]
DEFAULT_PRODUCT_VERSION = "8.0.10"

# EF Core 8 实际查询的表 (PascalCase, 大小写敏感, 必须 quoted)
CORRECT_TABLE = "__EFMigrationsHistory"
# 历史错误位置的 snake_case 表 (从 EFCore.NamingConventions 早期版本 / 自定义脚本可能创建)
WRONG_TABLES = ["__efmigrationshistory", "__ef_migrations_history"]


def parse_args() -> argparse.Namespace:
    """解析 CLI 参数"""
    p = argparse.ArgumentParser(
        prog="_ef_migrations_baseline.py",
        description="EF Core Migrations baseline seed (Day 10+ P0.2)",
    )
    p.add_argument(
        "--migrations",
        type=str,
        default=",".join(DEFAULT_MIGRATIONS),
        help=f"逗号分隔的 migration_id 列表 (默认: 4 个老 migration)",
    )
    p.add_argument(
        "--product-version",
        type=str,
        default=DEFAULT_PRODUCT_VERSION,
        help=f"EF Core product version (默认: {DEFAULT_PRODUCT_VERSION})",
    )
    p.add_argument("--pg-host", type=str, default="localhost", help="PG host (默认: localhost)")
    p.add_argument("--pg-port", type=int, default=5432, help="PG port (默认: 5432)")
    p.add_argument("--pg-db", type=str, default="spike_test_v3", help="PG database (默认: spike_test_v3)")
    p.add_argument("--pg-user", type=str, default="postgres", help="PG user (默认: postgres)")
    p.add_argument(
        "--pg-password",
        type=str,
        default="784533",
        help="PG password (默认: 784533, 仅本地 spike_test_v3 用)",
    )
    p.add_argument(
        "--dry-run",
        action="store_true",
        help="仅打印 SQL 不执行 (验证脚本逻辑)",
    )
    args = p.parse_args()

    # 解析 migration 列表
    migrations = [m.strip() for m in args.migrations.split(",") if m.strip()]
    if not migrations:
        print("[error] --migrations 不能为空", file=sys.stderr)
        sys.exit(1)
    args.migration_list = migrations
    return args


def connect(args: argparse.Namespace):
    """连接 PG, 失败返回 None"""
    try:
        conn = psycopg2.connect(
            host=args.pg_host,
            port=args.pg_port,
            dbname=args.pg_db,
            user=args.pg_user,
            password=args.pg_password,
            connect_timeout=5,
        )
        return conn
    except psycopg2.Error as e:
        print(f"[error] 连接 PG 失败: {args.pg_host}:{args.pg_port}/{args.pg_db} as {args.pg_user}: {e}")
        return None


def drop_wrong_tables(cur, dry_run: bool) -> None:
    """清理错误位置的 snake_case __efmigrationshistory / __ef_migrations_history 表"""
    for tbl in WRONG_TABLES:
        sql = f'DROP TABLE IF EXISTS "{tbl}"'
        if dry_run:
            print(f"  [dry-run] {sql}")
        else:
            cur.execute(sql)
            print(f"  [cleanup] 删错表 {tbl!r} (rowcount={cur.rowcount})")


def ensure_correct_table(cur, dry_run: bool) -> None:
    """确保 PascalCase __EFMigrationsHistory 存在 (EF Core 8 实际查的表)"""
    sql = f"""
        CREATE TABLE IF NOT EXISTS "{CORRECT_TABLE}" (
            migration_id varchar(150) NOT NULL,
            product_version varchar(32) NOT NULL,
            CONSTRAINT "PK_{CORRECT_TABLE}" PRIMARY KEY (migration_id)
        )
    """
    if dry_run:
        # 简化打印 (单行, 避免 multiline)
        print(f"  [dry-run] CREATE TABLE IF NOT EXISTS \"{CORRECT_TABLE}\" (...)")
    else:
        cur.execute(sql)
        print(f"  [ensure] PascalCase \"{CORRECT_TABLE}\" 表存在")


def seed_migrations(cur, migrations: List[str], product_version: str, dry_run: bool) -> dict:
    """INSERT 老 migration 记录, ON CONFLICT DO NOTHING 幂等

    返回 dict: {migration_id: "inserted" | "exists"}
    """
    result = {}
    for mid in migrations:
        sql = f"""
            INSERT INTO "{CORRECT_TABLE}" (migration_id, product_version)
            VALUES (%s, %s)
            ON CONFLICT (migration_id) DO NOTHING
        """
        if dry_run:
            print(f"  [dry-run] INSERT {mid} (product_version={product_version}) ON CONFLICT DO NOTHING")
            result[mid] = "dry-run"
        else:
            cur.execute(sql, (mid, product_version))
            # cur.rowcount: 1=inserted, 0=existed (ON CONFLICT 触发)
            result[mid] = "inserted" if cur.rowcount == 1 else "exists"
            print(f"  [seed] {mid}  →  {result[mid]}")
    if not dry_run:
        # 提交
        #  注: 在 psycopg2 默认 autocommit=False, 需要显式 commit
        #  但 commit 在外层 main() 控制, 这里只输出
        pass
    return result


def show_history(cur, dry_run: bool) -> None:
    """SELECT 当前 __EFMigrationsHistory 内容 (验证)"""
    sql = f'SELECT migration_id, product_version FROM "{CORRECT_TABLE}" ORDER BY migration_id'
    if dry_run:
        print(f"  [dry-run] SELECT ... FROM \"{CORRECT_TABLE}\" ORDER BY migration_id")
        return
    cur.execute(sql)
    rows = cur.fetchall()
    print(f"\n{CORRECT_TABLE} (PascalCase) 当前 {len(rows)} 行:")
    for r in rows:
        print(f"  - {r[0]}  v={r[1]}")


def check_xref_table(cur, dry_run: bool) -> None:
    """检查 xref_oem_brand 表是否存在 (验证 EF Core 还未跑 AddXrefOemBrandDict)"""
    sql = """
        SELECT EXISTS (
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = 'xref_oem_brand'
        )
    """
    if dry_run:
        print("  [dry-run] SELECT EXISTS (information_schema.tables WHERE table_name='xref_oem_brand')")
        return
    cur.execute(sql)
    exists = cur.fetchone()[0]
    status = "已存在" if exists else "不存在 (待 AddXrefOemBrandDict 创建)"
    print(f"\nxref_oem_brand: {status}")


def main() -> int:
    args = parse_args()
    print(f"=== EF Core Migrations baseline seed (P0.2) ===")
    print(
        f"PG: {args.pg_host}:{args.pg_port}/{args.pg_db} as {args.pg_user}  |  "
        f"dry_run={args.dry_run}"
    )
    print(f"Migrations ({len(args.migration_list)}):")
    for m in args.migration_list:
        print(f"  - {m}")
    print(f"Product version: {args.product_version}")
    print()

    conn = connect(args)
    if conn is None:
        # 退出码 2 = DB 连接失败
        return 2

    try:
        cur = conn.cursor()

        # Step 1: 清理错误位置的 snake_case 表
        print("[step 1] 清理错位置 snake_case 表")
        drop_wrong_tables(cur, dry_run=args.dry_run)

        # Step 2: 确保 PascalCase 表存在
        print("\n[step 2] 确保 PascalCase __EFMigrationsHistory 存在")
        ensure_correct_table(cur, dry_run=args.dry_run)

        # Step 3: INSERT 老 migration 记录
        print("\n[step 3] INSERT 老 migration (ON CONFLICT DO NOTHING)")
        result = seed_migrations(cur, args.migration_list, args.product_version, dry_run=args.dry_run)

        # Step 4: 验证
        print("\n[step 4] 验证当前 __EFMigrationsHistory")
        show_history(cur, dry_run=args.dry_run)
        check_xref_table(cur, dry_run=args.dry_run)

        if not args.dry_run:
            conn.commit()
            inserted = sum(1 for v in result.values() if v == "inserted")
            existed = sum(1 for v in result.values() if v == "exists")
            print(f"\n[summary] inserted={inserted}, existed={existed}, dry_run={args.dry_run}")
            print("[done] baseline seed 完成. 现在可以 dotnet run 启动后端, EF Core Migrate 只跑未应用的 migration")
        else:
            print(f"\n[summary] dry-run 模式, 未执行任何 DML/DDL")
            print("[done] dry-run 完成. 去掉 --dry-run 实际执行")
        cur.close()
    except psycopg2.Error as e:
        conn.rollback()
        print(f"[error] SQL 执行失败: {e}")
        return 1
    finally:
        conn.close()

    return 0


if __name__ == "__main__":
    sys.exit(main())
