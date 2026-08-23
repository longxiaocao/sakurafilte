# EF Core Migrations Baseline 自动化 (P0.2)

> Day 10+ P0.2 任务产物。本文说明为什么需要 baseline、怎么用、怎么回滚、添加新 migration 怎么办。

---

## 目录

1. [为什么需要 baseline](#1-为什么需要-baseline)
2. [使用流程](#2-使用流程)
3. [添加新 migration 的步骤](#3-添加新-migration-的步骤)
4. [回滚流程](#4-回滚流程)
5. [故障排查](#5-故障排查)
6. [相关文件清单](#6-相关文件清单)

---

## 1. 为什么需要 baseline

### 1.1 历史背景

SakuraFilter 项目的 schema 历史 (Day 1-9) 完全用 SQL migration 文件维护 (`backend/migrations/00X_*.sql`)。`xref_oem_brand` 之前的 4 个 EF Core migration (Day 10 起) 是后加的, 与历史 SQL migration 创建的 `products` 表 schema 不一致:

| 元素 | EF Core 8 (PascalCase + `ix_` 前缀) | SQL migration (snake_case + `idx_`/`uq_` 前缀) |
|------|--------------------------------------|------------------------------------------------|
| `products.oem_no_normalized` UNIQUE 索引 | `IX_products_oem_no_normalized` | `uq_products_oem_normalized` |
| `products` 复合索引 | `IX_products_type_d1` (默认名) | `idx_products_type_d1` (显式命名) |
| `products.is_published` 默认值 | 通过 `HasDefaultValue(true)` 设 | 早期 SQL 显式 `DEFAULT true` |
| `products.is_discontinued` 默认值 | 通过 `HasDefaultValue(false)` 设 | 早期 SQL 无此列, EF Core 8 才有 |

### 1.2 为什么冲突

EF Core 8 的 `Migrate` 命令启动时:
1. SELECT `__EFMigrationsHistory` 拿已应用列表 (表名硬编码, `UseSnakeCaseNamingConvention` 不改内置表名)
2. 拿到已应用列表后, 对比当前 ModelSnapshot 与数据库 schema 差异
3. 对每个未应用的 migration 执行 `Up()`, 内部是生成 `CREATE INDEX` / `ALTER TABLE ... DROP COLUMN` / `ADD CONSTRAINT` SQL
4. **如果 `products` 表已存在 (SQL migration 建过) 且索引名/列名与 EF Core 期望的不一致 → DROP/ALTER 失败 → Migrate 整个事务回滚 → 启动失败**

### 1.3 为什么 `UseSnakeCaseNamingConvention` 没用

`EFCore.NamingConventions` 库的 `UseSnakeCaseNamingConvention()` 只改 **用户表/列/索引** 的命名, **不改** EF Core 8 内置的 `__EFMigrationsHistory` 表名 (这个表名在 `MigrationsAssembly` 内部硬编码为 PascalCase)。所以 baseline 必须写到 PascalCase `__EFMigrationsHistory`, 不能写到 snake_case `__efmigrationshistory` 或 `__ef_migrations_history` (Day 10 v1/v2 踩过这个坑)。

### 1.4 解决方案: Baseline Seed

手工在 PascalCase `__EFMigrationsHistory` 插入 4 行已应用记录, 让 EF Core Migrate 跳过这 4 个老 migration, 只跑新加的 (如 `AddXrefOemBrandDict`)。

---

## 2. 使用流程

### 2.1 本地开发 (Windows PowerShell)

```powershell
# 步骤 1: 确认 PG 已启动, spike_test_v3 数据库已创建
# 步骤 2: 确认后端 (dotnet run) 未启动
# 步骤 3: 执行 baseline seed
.\scripts\db-baseline.ps1

# (可选) 验证用, 不实际执行只打印 SQL
.\scripts\db-baseline.ps1 -DryRun

# 步骤 4: 启动后端
cd backend\src\SakuraFilter.Api
dotnet run -c Debug
```

### 2.2 本地开发 (Linux / macOS / WSL)

```bash
./scripts/db-baseline.sh

# (可选) 验证用
./scripts/db-baseline.sh --dry-run  # 注: 当前 .sh 不支持 --dry-run, 需改 python 直跑
```

### 2.3 直接调用 Python 脚本 (高级)

```bash
# 默认参数: spike_test_v3 / 4 个老 migration
python spike-test/_ef_migrations_baseline.py

# 验证用
python spike-test/_ef_migrations_baseline.py --dry-run

# 自定义 PG 连接
python spike-test/_ef_migrations_baseline.py \
  --pg-host=localhost --pg-port=5432 \
  --pg-db=spike_test_v3 --pg-user=postgres --pg-password=784533

# 自定义 migration 列表
python spike-test/_ef_migrations_baseline.py \
  --migrations=20260702025150_InitialCreate,20260702051540_AddUniqueIndexOnOemNoNormalized

# 自定义 product version
python spike-test/_ef_migrations_baseline.py --product-version=8.0.11
```

### 2.4 CI 流程 (GitHub Actions)

`/.github/workflows/e2e.yml` 已加入 `Run EF Core Migrations baseline seed` 步骤, 在 `services.postgres` 启动后、`dotnet run` 前自动执行, 默认参数 (CI 全新 DB 无 history 表 → 4 个老 migration 全 INSERT)。

CI 配置片段:

```yaml
services:
  postgres:
    image: postgres:16
    env:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: 784533
      POSTGRES_DB: spike_test_v3
    ports: [5432:5432]
    options: --health-cmd pg_isready ...

steps:
  - ...
  - name: Run EF Core Migrations baseline seed
    working-directory: spike-test
    run: python _ef_migrations_baseline.py
  - name: Run API server (background)
    run: |
      cd backend/src/SakuraFilter.Api
      dotnet run -c Release --no-build --urls "http://localhost:5148" & ...
```

### 2.5 生产环境

**生产环境禁用 baseline seed**。生产环境的 schema 演进应该:
- SQL migration 通过 `psql -f backend/migrations/00X_*.sql` 顺序应用
- EF Core migration 通过 `dotnet ef database update` 应用
- 两者严格按时间顺序, **不在生产用 baseline seed 跳过 migration**

如果生产已经 baseline 过 (Day 10 前的 spike 环境), 后续新 EF Core migration 直接 `dotnet ef database update` 即可, 不要再跑 baseline 脚本。

---

## 3. 添加新 migration 的步骤

### 3.1 添加 EF Core Migration (Day 11+ 例子)

```bash
cd backend/src/SakuraFilter.Infrastructure
# 1) 修改 Entity / DbContext
# 2) 生成 migration
dotnet ef migrations add AddXxxFeature --startup-project ../SakuraFilter.Api
# 3) 检查生成的文件: 20260702080000_AddXxxFeature.cs + Designer.cs
```

### 3.2 是否需要更新 baseline 列表?

**视情况而定**:

| 场景 | 是否需要更新 baseline | 说明 |
|------|-----------------------|------|
| 全新 EF Core migration (新 feature / 新表) | ❌ 不需要 | 新 migration 让 EF Core 正常跑即可 |
| 改 `products` 表结构 (新列 / 改索引) | ✅ **必须更新** | EF Core 8 会 DROP/ALTER 老 schema, 与 SQL migration 冲突 |
| 改 `xref_oem_brand` 表结构 | ❌ 不需要 | Day 10 之后新加的, EF Core 8 + SQL migration 还没冲突 |
| 改 `__EFMigrationsHistory` 字段 | ❌ 不需要 | EF Core 内部表, 不归我们管 |

### 3.3 更新 baseline 列表的方法

如果新 migration 会改老 SQL migration 建过的表 (`products` / `cross_references` / `machine_applications` / `product_history` / `product_images` / `system_settings` / `search_index_*` / `etl_progress_log`), 必须把新 migration 的 id 也加到 baseline 列表中。

**两种方法**:

#### 方法 A: 改 Python 脚本默认参数 (推荐)

编辑 `spike-test/_ef_migrations_baseline.py`:

```python
DEFAULT_MIGRATIONS = [
    "20260702025150_InitialCreate",
    "20260702051540_AddUniqueIndexOnOemNoNormalized",
    "20260702052101_AddIsPublishedDefaultTrue",
    "20260702052628_AddProductsDefaultsV9",
    "20260702080000_AddXxxFeature",  # 新加的
]
```

#### 方法 B: 一次性命令行参数 (不推荐, 容易漏改)

```bash
python spike-test/_ef_migrations_baseline.py \
  --migrations=20260702025150_InitialCreate,...,20260702080000_AddXxxFeature
```

**推荐方法 A**, 因为 CI workflow 用默认参数, 改默认参数 CI 自动跟进。

### 3.4 验证 baseline 正确性

新增 migration 后, 在本地执行:

```bash
# 1) 删 products 表 (模拟全新 DB)
psql -h localhost -U postgres -d spike_test_v3 -c "DROP TABLE products CASCADE;"

# 2) 跑 baseline (会重建 __EFMigrationsHistory, 4/5 个老 migration 全 INSERT)
python spike-test/_ef_migrations_baseline.py

# 3) 启动后端, EF Core Migrate 应只跑新 migration
cd backend/src/SakuraFilter.Api
dotnet run -c Debug
# 期望日志: "Applying migration 20260702080000_AddXxxFeature" (而不是 4 个老 migration)
```

---

## 4. 回滚流程

### 4.1 强制 EF Core 重跑全部 migration (调试用)

```bash
# 1) 删 __EFMigrationsHistory 表
psql -h localhost -U postgres -d spike_test_v3 \
  -c 'DROP TABLE IF EXISTS "__EFMigrationsHistory";'

# 2) 启动后端, EF Core Migrate 会从第 1 个 migration 开始重跑
cd backend/src/SakuraFilter.Api
dotnet run -c Debug
```

### 4.2 回滚到上一个 EF Core migration

```bash
# 1) 回滚最后一个 EF Core migration (生成 Down() SQL)
cd backend/src/SakuraFilter.Infrastructure
dotnet ef migrations remove --startup-project ../SakuraFilter.Api

# 2) 删 __EFMigrationsHistory 对应行, 让 EF Core Migrate 重跑
psql -h localhost -U postgres -d spike_test_v3 \
  -c 'DELETE FROM "__EFMigrationsHistory" WHERE migration_id = '"'"'20260702080000_AddXxxFeature'"'"';'

# 3) 重启后端
cd ../SakuraFilter.Api
dotnet run -c Debug
```

### 4.3 删 spike_test_v3 重建 (彻底回滚)

```bash
# 1) 停后端
# 2) 删 + 重建数据库
psql -h localhost -U postgres -c "DROP DATABASE spike_test_v3;"
psql -h localhost -U postgres -c "CREATE DATABASE spike_test_v3;"

# 3) 重跑所有 SQL migration (00X_*.sql, 按文件顺序)
for f in backend/migrations/00*.sql; do
  psql -h localhost -U postgres -d spike_test_v3 -f "$f"
done

# 4) 跑 baseline seed
python spike-test/_ef_migrations_baseline.py

# 5) 启动后端
cd backend/src/SakuraFilter.Api
dotnet run -c Debug
```

---

## 5. 故障排查

### 5.1 启动后端报 `42P10: there is no unique or exclusion constraint matching the ON CONFLICT specification`

**根因**: `products.oem_no_normalized` UNIQUE 索引不存在。ETL `INSERT ... ON CONFLICT (oem_no_normalized)` 失败。

**解决方案**:

```sql
-- 1) 检查 UNIQUE 索引是否存在
SELECT indexname, indexdef FROM pg_indexes
WHERE tablename = 'products' AND indexname LIKE '%oem_no_normalized%';
-- 期望: 含 "UNIQUE INDEX uq_products_oem_normalized ..." (SQL migration 8 创建)

-- 2) 如果不存在, 手动加
CREATE UNIQUE INDEX IF NOT EXISTS uq_products_oem_normalized
  ON products (oem_no_normalized);
```

### 5.2 启动后端报 `23502: null value in column "is_published" violates not-null constraint`

**根因**: `is_published` / `is_discontinued` / `created_at` 列无默认值, ETL INSERT 不显式插入这些列。

**解决方案**:

```sql
-- 加默认值 (Day 9.12 v8/v9 修复)
ALTER TABLE products ALTER COLUMN is_published SET DEFAULT true;
ALTER TABLE products ALTER COLUMN is_discontinued SET DEFAULT false;
ALTER TABLE products ALTER COLUMN created_at SET DEFAULT now();
```

### 5.3 启动后端报 `42P07: relation "__EFMigrationsHistory" already exists` (lowercase)

**根因**: 历史 EF Core 版本创建了 snake_case `__efmigrationshistory` 表 (Day 10 v1 踩坑)。

**解决方案**:

```sql
-- 删错表, 让 EF Core 重建 PascalCase
DROP TABLE IF EXISTS "__efmigrationshistory";
DROP TABLE IF EXISTS "__ef_migrations_history";
-- 然后跑 baseline seed
```

`_ef_migrations_baseline.py` 会自动 DROP 这两个错表, 跑一次即可。

### 5.4 启动后端报 `42703: column "ix_xxx" does not exist` (DROP INDEX 失败)

**根因**: EF Core 8 试图 DROP 老 SQL migration 创建的 `idx_xxx` 索引, 但用了 `ix_` 前缀。

**解决方案**: 跑 baseline seed 跳过老 migration, 让 EF Core 8 只跑新 migration。新 migration 不改 `products` 表就不会触发这个错误。

### 5.5 `psycopg2.OperationalError: connection to server failed`

**根因**: PG 未启动 / 端口错 / 密码错。

**解决方案**:

```bash
# 检查 PG 状态
pg_isready -h localhost -p 5432

# 检查密码
psql -h localhost -U postgres -d spike_test_v3 -c "SELECT 1;"
# 提示输入密码: 784533 (默认)
```

`_ef_migrations_baseline.py --pg-password=xxx` 可改密码。

### 5.6 CI workflow 报 `Run EF Core Migrations baseline seed` 失败

**根因**: 大概率是 `services.postgres` 未就绪 (健康检查未过)。

**解决方案**: 在 baseline step 之前加 `pg_isready` 循环等待:

```yaml
- name: Wait for postgres
  run: |
    for i in $(seq 1 30); do
      pg_isready -h localhost -p 5432 && break
      sleep 1
    done
```

(目前 `services.postgres` 已配 `--health-cmd pg_isready --health-retries 5`, 默认会等, 如仍失败再手动加 wait step。)

---

## 6. 相关文件清单

| 文件 | 用途 |
|------|------|
| `spike-test/_ef_migrations_baseline.py` | 主脚本 (参数化、幂等、dry-run) |
| `spike-test/_apply_efmigrations_baseline_day10_v3.py` | Day 10 临时版本 (已被通用版替代, 待删除) |
| `scripts/db-baseline.sh` | Linux/macOS 一键脚本 |
| `scripts/db-baseline.ps1` | Windows PowerShell 一键脚本 |
| `.github/workflows/e2e.yml` | CI workflow (含 baseline seed 步骤) |
| `docs/ef-migrations-baseline.md` | 本文档 |
| `backend/src/SakuraFilter.Infrastructure/Data/Migrations/` | EF Core 8 migration 文件 (5 个) |
| `backend/migrations/00X_*.sql` | 历史 SQL migration (18 个) |

---

> 文档维护: Day 10+ P0.2 Task 2 / SubTask 2.4
> 最后更新: 2026-07-02
