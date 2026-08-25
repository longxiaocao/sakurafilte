#!/usr/bin/env bash
# SakuraFilter 生产部署脚本 (V3 2026-08-25 上线审查 v2: 安全发布顺序)
# 用法: 在仓库根目录执行 ./scripts/deploy-prod.sh
# 前提: 已创建 .env.prod（参考 .env.prod.example；兼容历史部署使用的 .env）
#
# 安全发布顺序 (codex 审核要求):
#   1. 备份 (backup-db.sh --verify --upload, 失败显式警告但继续 — 备份是预防性)
#   2. 停止 API (隔离: 迁移期间无新流量打到旧 schema)
#   3. 迁移 (migrate.sh: EF + SQL, 失败立即退出 — 阻断部署, API 保持停止)
#   4. 启动 PG + API (迁移成功后)
#   5. 其余服务 + 健康检查 (失败退出)
set -uo pipefail

cd "$(dirname "$0")/.."

echo "===== SakuraFilter 生产部署 ====="

# 1. 拉取最新代码
echo "[1/8] 拉取 master 最新..."
git fetch origin || { echo "[FAIL] git fetch 失败"; exit 1; }
git pull origin master || { echo "[FAIL] git pull 失败 (有未提交改动? 用 git status 检查)"; exit 1; }

# 2. env 检查。优先 .env.prod，兼容早期文档中的 .env，禁止混用两份不同密钥。
if [ -f .env.prod ]; then
    ENV_FILE=.env.prod
elif [ -f .env ]; then
    ENV_FILE=.env
else
    echo "[FAIL] 缺少 .env.prod（或兼容的 .env）— 请 cp .env.prod.example .env.prod 并填入真实密钥"
    exit 1
fi
echo "    使用环境文件: $ENV_FILE"

# 3. 备份 (预防性; 失败显式警告但不阻断 — 不能让备份失败阻塞发布)
echo "[2/8] 数据库备份 (--verify --upload)..."
if bash scripts/backup-db.sh --verify --upload; then
    echo "    [OK] 备份完成 (含对象存储副本)"
else
    echo "    [WARN] 备份失败 (见上方) — 发布继续, 但请尽快修复备份通道"
fi

# 4. 停止 API (隔离: 迁移期间无新流量, 避免新 API 跑旧 schema)
echo "[3/8] 停止 API (迁移隔离)..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" stop api || { echo "[FAIL] 停止 API 失败"; exit 1; }

# 5. 迁移 (EF + SQL) — 失败立即退出, API 保持停止, 不启动不完整发布
echo "[4/8] 应用迁移 (EF --migrate-db + SQL)..."
if bash scripts/migrate.sh; then
    echo "    [OK] 迁移完成"
else
    echo "[FAIL] 迁移失败 — API 未启动, 修复后重跑 deploy-prod.sh (迁移幂等, 可安全重试)"
    exit 1
fi

# 6. 重建 PG + API (配置变更生效的关键; 迁移成功后启动)
echo "[5/8] 构建并重建 postgres + api (--build --force-recreate)..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" up -d --build --force-recreate postgres api || { echo "[FAIL] 重建失败"; exit 1; }

# 7. 其余服务常规更新 (web/meilisearch/minio/prometheus/grafana)
echo "[6/8] 构建并更新其余服务..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" up -d --build || { echo "[FAIL] 常规更新失败"; exit 1; }

# 8. 验证 max_connections + 健康检查 (业务探针, 失败退出)
echo "[7/8] 验证 max_connections..."
sleep 5
MAXCONN=$(docker exec sakura-postgres sh -c 'psql -U $POSTGRES_USER -d $POSTGRES_DB -t -A -c "SHOW max_connections;"' 2>/dev/null || echo "?")
echo "    max_connections = $MAXCONN"
if [ "$MAXCONN" != "200" ]; then
    echo "    [WARN] 期望 200, 实际 $MAXCONN — 检查 compose 是否更新"
fi

echo "[8/8] backend 健康检查 (业务探针)..."
sleep 8
for i in 1 2 3; do
    if docker exec sakura-api wget --quiet --tries=1 --spider http://localhost:8080/health/ready >/dev/null 2>&1; then
        echo "    [OK] /health/ready 通过"
        break
    fi
    [ "$i" = "3" ] && { echo "[FAIL] /health/ready 未通过 (3 次重试) — 部署失败, 请检查 api 日志"; exit 1; }
    sleep 5
done

echo "===== 部署完成 ====="
echo "提示: 生产数据库请配置每日自动备份计划任务 (bash scripts/backup-db.sh --verify --upload, 见脚本头)。"
