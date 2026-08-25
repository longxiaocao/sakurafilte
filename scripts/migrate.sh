#!/usr/bin/env bash
# =============================================================
# 生产迁移执行器 (SQL + EF Core) (V3 2026-08-25 上线审查: 修复"迁移无自动应用通道")
#
# 背景: db-init/db-migrate compose 服务已禁用 (8/22 有意, 防重建清库),
#       新增迁移只能手工应用, 易漏跑.
# 通道:
#   1) SQL 迁移: backend/migrations/*.sql, 用 schema_migrations 表对比目录 vs 已应用
#   2) EF Core C# 迁移: __EFMigrationsHistory 由 EF 管理, 用 API 镜像 --migrate-db 模式执行
#      (与 compose db-init 服务等价: MigrateAsync 只跑 pending, 幂等)
# 顺序: EF 先 → SQL 后 (与 db-init/db-migrate 原编排一致)
#
# 用法:
#   bash scripts/migrate.sh --baseline --confirm  # 首次: 标记现有 SQL 迁移已应用 (需确认+结构校验)
#   bash scripts/migrate.sh                       # 应用全部未应用迁移 (EF + SQL)
#   bash scripts/migrate.sh --list                # 仅列出未应用迁移, 不执行
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
    PG_PASS=$(grep -oP '^POSTGRES_PASSWORD=\K.*' "$ENV_FILE" | tr -d '"')
else
    PG_USER="${PG_USER:-sakura}"
    PG_DB="${PG_DB:-sakurafilter}"
    PG_PASS="${PG_PASSWORD:-}"
fi
PG_CONTAINER="${PG_CONTAINER:-sakura-postgres}"
MIGRATIONS_DIR="backend/migrations"

MODE="${1:-apply}"
if [ "${1:-}" = "--baseline" ]; then MODE="baseline"; fi
if [ "${1:-}" = "--list" ]; then MODE="list"; fi
# --baseline 是高风险人工操作: 必须显式 --confirm, 否则拒绝执行
if [ "$MODE" = "baseline" ] && [ "${2:-}" != "--confirm" ]; then
    echo "❌ --baseline 会把当前所有 SQL 迁移标记为已应用 (不校验结构), 高风险人工操作 — 必须追加 --confirm" >&2
    exit 1
fi

if ! docker ps --format '{{.Names}}' | grep -q "^${PG_CONTAINER}$"; then
    echo "❌ postgres 容器 ${PG_CONTAINER} 未运行 — 请先 docker compose up -d postgres" >&2
    exit 1
fi

if ! docker ps --format '{{.Names}}' | grep -q "^${PG_CONTAINER}$"; then
    echo "❌ postgres 容器 ${PG_CONTAINER} 未运行 — 请先 docker compose up -d postgres" >&2
    exit 1
fi

psql() { docker exec -i "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" "$@"; }

# --- 0. EF Core C# 迁移 (先于 SQL, 与 db-init/db-migrate 原编排一致) ---
#   __EFMigrationsHistory 由 EF 管理; 用 API 镜像 --migrate-db 模式执行 (MigrateAsync 幂等, 只跑 pending)
#   apply 模式才执行; list 模式只提示
if [ "$MODE" = "apply" ]; then
    echo "==> [1/2] EF Core C# 迁移 (--migrate-db)..."
    API_IMAGE=$(grep -oP 'image: \Ksakurafilter-api:[0-9.]+' docker-compose.prod.yml | head -1)
    # compose 网络名 = {项目名}_sakura-net (项目名默认目录名, 动态探测)
    NET_NAME=$(docker network ls --format '{{.Name}}' | grep '_sakura-net$' | head -1 || true)
    if [ -n "$API_IMAGE" ] && [ -n "$PG_PASS" ] && [ -n "$NET_NAME" ]; then
        CONN="Host=postgres;Port=5432;Database=${PG_DB};Username=${PG_USER};Password=${PG_PASS}"
        if docker run --rm --network "$NET_NAME" -e "ConnectionStrings__Postgres=${CONN}" \
            "$API_IMAGE" dotnet SakuraFilter.Api.dll --migrate-db; then
            echo "    [OK] EF 迁移完成"
        else
            echo "❌ EF 迁移失败 (--migrate-db) — 已中止" >&2
            exit 1
        fi
    else
        echo "⚠️ 无法自动执行 EF 迁移 (API_IMAGE=$API_IMAGE NET_NAME=$NET_NAME PG_PASS=${PG_PASS:+set}) — 请手动: docker compose run --rm api --migrate-db"
    fi
elif [ "$MODE" = "list" ]; then
    echo "==> [1/2] EF Core 迁移: 由 API --migrate-db 模式管理 (__EFMigrationsHistory), 与 SQL 通道独立"
fi

# --- 确保 SQL 记录表存在 ---
psql -c "CREATE TABLE IF NOT EXISTS schema_migrations (filename TEXT PRIMARY KEY, applied_at TIMESTAMPTZ DEFAULT now());" >/dev/null

# --baseline 结构校验: 关键业务表/列存在才允许标记 (防误在不完整环境执行后永久跳过缺失迁移)
if [ "$MODE" = "baseline" ]; then
    CHECKS=(
        "SELECT 1 FROM products LIMIT 1"
        "SELECT 1 FROM cross_references LIMIT 1"
        "SELECT 1 FROM machine_applications LIMIT 1"
        "SELECT 1 FROM etl_progress_log LIMIT 1"
    )
    FAILED=0
    for sql in "${CHECKS[@]}"; do
        if ! psql -t -A -c "$sql" >/dev/null 2>&1; then
            echo "❌ 结构校验失败: $sql" >&2
            FAILED=1
        fi
    done
    if [ "$FAILED" = "1" ]; then
        echo "❌ baseline 中止: 数据库结构不完整 (缺关键表) — 不要标记, 先修复环境" >&2
        exit 1
    fi
    echo "    [OK] 关键表校验通过 (products/cross_references/machine_applications/etl_progress_log)"
fi

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
    apply)    echo "迁移完成: EF + SQL 应用 $applied, SQL 跳过 $skipped"; [ -n "$pending" ] && echo "SQL 已应用: $pending" ;;
    baseline) echo "基线完成: 标记 $applied 个 SQL 迁移为已应用 (后续新迁移将自动应用)" ;;
    list)     echo "SQL 未应用:$pending (共 $skipped 已应用)"; [ -z "$pending" ] && echo "(无待应用 SQL 迁移)" ;;
esac
