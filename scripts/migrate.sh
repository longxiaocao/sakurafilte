#!/usr/bin/env bash
# =============================================================
# 生产 SQL 迁移执行器 (V3 2026-08-25 上线审查: 修复"迁移无自动应用通道")
#
# 背景: db-init/db-migrate compose 服务已禁用 (8/22 有意, 防重建清库),
#       新增 backend/migrations/*.sql 迁移只能手工 psql 应用, 易漏跑.
# 方案: 用 schema_migrations 记录表对比"目录文件 vs 已应用", 执行未应用的
#       SQL 迁移并记录. 幂等: 已应用的自动跳过.
#
# 用法:
#   bash scripts/migrate.sh --baseline    # 首次: 当前 DB 结构与代码匹配, 将现有迁移全部标记已应用 (不执行)
#   bash scripts/migrate.sh               # 应用全部未应用迁移
#   bash scripts/migrate.sh --list        # 仅列出未应用迁移, 不执行
#
# 前置: postgres 容器运行中; 需要 PG 用户/库 (默认读 .env.prod)
# =============================================================
set -euo pipefail

cd "$(dirname "$0")/.."

# --- 读取连接信息 ---
ENV_FILE=".env.prod"
if [ -f "$ENV_FILE" ]; then
    PG_USER=$(grep -oP '^POSTGRES_USER=\K.*' "$ENV_FILE" | tr -d '"')
    PG_DB=$(grep -oP '^POSTGRES_DB=\K.*' "$ENV_FILE" | tr -d '"')
else
    PG_USER="${PG_USER:-sakura}"
    PG_DB="${PG_DB:-sakurafilter}"
fi
PG_CONTAINER="${PG_CONTAINER:-sakura-postgres}"
MIGRATIONS_DIR="backend/migrations"

MODE="${1:-apply}"
if [ "${1:-}" = "--baseline" ]; then MODE="baseline"; fi
if [ "${1:-}" = "--list" ]; then MODE="list"; fi

if ! docker ps --format '{{.Names}}' | grep -q "^${PG_CONTAINER}$"; then
    echo "❌ postgres 容器 ${PG_CONTAINER} 未运行 — 请先 docker compose up -d postgres" >&2
    exit 1
fi

psql() { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" "$@"; }

# 确保记录表存在
psql -c "CREATE TABLE IF NOT EXISTS schema_migrations (filename TEXT PRIMARY KEY, applied_at TIMESTAMPTZ DEFAULT now());" >/dev/null

applied=0; skipped=0; pending=""
for f in "$MIGRATIONS_DIR"/*.sql; do
    name="$(basename "$f")"
    is_applied=$(psql -t -A -c "SELECT 1 FROM schema_migrations WHERE filename='${name}';")
    if [ "$is_applied" = "1" ]; then
        skipped=$((skipped+1))
        continue
    fi
    pending="$pending $name"
    if [ "$MODE" = "apply" ]; then
        echo "[APPLY] $name"
        psql -v ON_ERROR_STOP=1 < "$f" || { echo "❌ $name 执行失败, 已中止 (未记录, 修复后重跑)" >&2; exit 1; }
        psql -c "INSERT INTO schema_migrations(filename) VALUES ('${name}');" >/dev/null
        applied=$((applied+1))
    elif [ "$MODE" = "baseline" ]; then
        # 首次部署基线: DB 结构已与代码匹配 (历史手工应用), 只标记不执行
        echo "[BASELINE] $name"
        psql -c "INSERT INTO schema_migrations(filename) VALUES ('${name}') ON CONFLICT DO NOTHING;" >/dev/null
        applied=$((applied+1))
    fi
done

echo "-----------------------------------------"
case "$MODE" in
    apply)    echo "迁移完成: 应用 $applied, 跳过 $skipped"; [ -n "$pending" ] && echo "已应用: $pending" ;;
    baseline) echo "基线完成: 标记 $applied 个迁移为已应用 (后续新迁移将自动应用)"; [ -n "$pending" ] && echo "标记: $pending" ;;
    list)     echo "未应用迁移:$pending (共 $skipped 已应用)"; [ -z "$pending" ] && echo "(无待应用迁移)" ;;
esac
