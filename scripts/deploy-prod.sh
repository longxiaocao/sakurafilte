#!/usr/bin/env bash
# SakuraFilter 生产部署脚本
# 用法: 在仓库根目录执行 ./scripts/deploy-prod.sh
# 前提: 已创建 .env.prod（参考 .env.prod.example；兼容历史部署使用的 .env）
#
# 覆盖: 连接池修复 (max_connections=200 + 池 120) + 常规更新
# WHY: postgres 的 command 参数变更必须 --force-recreate 才生效, restart 不生效
set -uo pipefail

cd "$(dirname "$0")/.."

echo "===== SakuraFilter 生产部署 ====="

# 1. 拉取最新代码
echo "[1/6] 拉取 master 最新..."
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

# 3. 重建 PG + API (配置变更生效的关键)
echo "[2/6] 构建并重建 postgres + api (--build --force-recreate)..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" up -d --build --force-recreate postgres api || { echo "[FAIL] 重建失败"; exit 1; }

# 4. 其余服务常规更新 (web/meilisearch/minio/prometheus/grafana)
echo "[3/6] 构建并更新其余服务..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" up -d --build || { echo "[FAIL] 常规更新失败"; exit 1; }

# 5. 验证 max_connections (连接池修复生效标志)
echo "[4/6] 验证 max_connections..."
sleep 5
MAXCONN=$(docker exec sakura-postgres sh -c 'psql -U $POSTGRES_USER -d $POSTGRES_DB -t -A -c "SHOW max_connections;"' 2>/dev/null || echo "?")
echo "    max_connections = $MAXCONN"
if [ "$MAXCONN" = "200" ]; then
    echo "    [OK] 连接池修复已生效"
else
    echo "    [WARN] 期望 200, 实际 $MAXCONN — 检查 compose 是否更新 / 是否被其他配置覆盖"
fi

# 6. 健康检查
echo "[5/6] backend 健康检查..."
sleep 5
if docker exec sakura-api wget --quiet --tries=1 --spider http://localhost:8080/health/ready >/dev/null 2>&1; then
    echo "    [OK] /health/ready 通过"
else
    echo "    [WARN] /health/ready 未通过 (端口/服务名不同? 按实际调整)"
fi

echo "[6/6] 完成。如 max_connections 与健康检查均 OK, 连接池修复正式生效。"
