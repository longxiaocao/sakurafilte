#!/bin/sh
# ============================================================
# db-migrate 执行的迁移脚本入口 (幂等版)
#
# 背景: 原 docker-compose.prod.yml 每次 `for f in /migrations/*.sql` 全量重跑,
#      而 migrations 中多为"一次性不可重跑"脚本 (008/009/010/018...)。
#      018_v2_legacy_data_cleanup.sql 含 TRUNCATE 业务表 →
#      每次 prod up 都会清空 products/machine_applications/cross_references。
#      多轮生产实测: 重启后 9376 产品/150050 apps/48798 孤儿被清空。
#
# WHY 历史表: 记录已成功应用的脚本文件名, 未记录才执行, 已记录跳过。
#      - CI 全新空库: 首次跑全部, 建历史记录; 后续 up 跳过 (不会再次 TRUNCATE)
#      - prod 已应用: 与历史记录匹配 → 全部 skip → 数据不被清
#      - 新增往返序列: 只对新增脚本执行 (增量迁移)
#      - 幂等移植: 移植字段 << 018 迁移前已存在的历史表
# ============================================================

set -e
: "${POSTGRES_USER:?POSTGRES_USER 未设置}"
: "${POSTGRES_DB:?POSTGRES_DB 未设置}"
: "${PGPASSWORD:?PGPASSWORD 未设置}"

PSQL() { psql -v ON_ERROR_STOP=1 -h postgres -U "$POSTGRES_USER" -d "$POSTGRES_DB" "$@"; }

# 1. 建历史表 (IF NOT EXISTS 幂等)
PSQL -c "CREATE TABLE IF NOT EXISTS __sakura_migrations (
    basename   text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);" >/dev/null

# 2. 按文件名顺序执行未应用的脚本
for f in /migrations/*.sql; do
    name=$(basename "$f")
    if PSQL -tAc "SELECT 1 FROM __sakura_migrations WHERE basename='$name'" | grep -q 1; then
        echo "===== SKIP (already applied): $name ====="
        continue
    fi
    echo "===== Executing $f ====="
    psql -v ON_ERROR_STOP=1 -h postgres -U "$POSTGRES_USER" -d "$POSTGRES_DB" -f "$f"
    PSQL -c "INSERT INTO __sakura_migrations(basename) VALUES ('$name')" >/dev/null
    echo "===== Applied: $name ====="
done

echo "===== All SQL migrations checked ====="