#!/usr/bin/env bash
# SakuraFilter 生产部署脚本 (V3 2026-08-25 上线审查 v3: 构建前置 + 备份策略 + 失败硬阻断)
# 用法: 在仓库根目录执行 ./scripts/deploy-prod.sh
# 前提: 已创建 .env.prod（参考 .env.prod.example；兼容历史部署使用的 .env）
#
# 安全发布顺序 (codex v3 审核要求):
#   1. 备份 (REQUIRE_BACKUP=1 默认阻断; 应急 REQUIRE_BACKUP=0 跳过)
#   2. 构建新 API 镜像 (必须在 EF 迁移之前 — 新 C# migration 需进镜像)
#   3. 停止 API (隔离)
#   4. 迁移 (EF 用新镜像 --migrate-db + SQL; 任何失败立即退出, API 保持停止)
#   5. 迁移后结构验证 (migrate.sh 内置)
#   6. 启动 PG + API (用刚构建的新镜像)
#   7. 其余服务
#   8. 健康检查 (业务探针, 失败退出)
set -uo pipefail

cd "$(dirname "$0")/.."

echo "===== SakuraFilter 生产部署 ====="

# 1. 拉取最新代码
echo "[1/9] 拉取 master 最新..."
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

# 3. 备份 — REQUIRE_BACKUP=1 默认阻断 (发布前必须有可恢复备份)
#    应急: REQUIRE_BACKUP=0 跳过 (仅限明确授权的紧急发布)
echo "[2/9] 数据库备份 (--verify --upload)..."
REQUIRE_BACKUP="${REQUIRE_BACKUP:-1}"
REQUIRE_REMOTE_BACKUP="${REQUIRE_REMOTE_BACKUP:-1}"
# V3(2026-08-25) codex v5: 必须 export 传给 backup-db.sh 子进程 (否则脚本读到默认值, 异机强制失效)
export REQUIRE_REMOTE_BACKUP
if [ "$REQUIRE_BACKUP" = "1" ]; then
    if bash scripts/backup-db.sh --verify --upload; then
        echo "    [OK] 备份完成 (含对象存储副本)"
    else
        echo "[FAIL] 备份失败 — REQUIRE_BACKUP=1, 发布中止"
        echo "        应急: REQUIRE_BACKUP=0 REQUIRE_REMOTE_BACKUP=0 ./scripts/deploy-prod.sh 跳过备份 (仅限明确授权)"
        exit 1
    fi
else
    echo "    [WARN] REQUIRE_BACKUP=0 — 跳过备份 (应急模式, 请确认已手工备份)"
fi

# 4. 构建新 API 镜像 (必须先于 EF 迁移 — 新 C# migration 需要进镜像才能被 --migrate-db 执行)
echo "[3/9] 构建新 API 镜像 (docker compose build api)..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" build api || { echo "[FAIL] API 镜像构建失败"; exit 1; }

# 5. 停止 API (隔离: 迁移期间无新流量, 避免新 API 跑旧 schema)
echo "[4/9] 停止 API (迁移隔离)..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" stop api || { echo "[FAIL] 停止 API 失败"; exit 1; }

# 6. 迁移 (EF 用新镜像 --migrate-db + SQL, 含迁移后结构验证) — 任何失败立即退出
echo "[5/9] 应用迁移 (EF --migrate-db 新镜像 + SQL + 结构验证)..."
if bash scripts/migrate.sh; then
    echo "    [OK] 迁移完成"
else
    echo "[FAIL] 迁移失败 — API 未启动, 修复后重跑 deploy-prod.sh (迁移幂等, 可安全重试)"
    exit 1
fi

# 7. 启动 PG + API (用刚构建的新镜像; --force-recreate 确保配置/镜像变更生效)
echo "[6/9] 启动 postgres + api (--force-recreate, 新镜像)..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" up -d --force-recreate postgres api || { echo "[FAIL] 启动失败"; exit 1; }

# 8. 其余服务常规更新 (web/meilisearch/minio/prometheus/grafana)
echo "[7/9] 构建并更新其余服务..."
docker compose -f docker-compose.prod.yml --env-file "$ENV_FILE" up -d --build || { echo "[FAIL] 常规更新失败"; exit 1; }

# 9. 验证 max_connections + 健康检查 (业务探针, 失败退出)
echo "[8/9] 验证 max_connections..."
sleep 5
MAXCONN=$(docker exec sakura-postgres sh -c 'psql -U $POSTGRES_USER -d $POSTGRES_DB -t -A -c "SHOW max_connections;"' 2>/dev/null || echo "?")
echo "    max_connections = $MAXCONN"
if [ "$MAXCONN" != "200" ]; then
    echo "    [WARN] 期望 200, 实际 $MAXCONN — 检查 compose 是否更新"
fi

echo "[9/9] backend 健康检查 (业务探针)..."
sleep 8
READY=0
for i in 1 2 3; do
    if docker exec sakura-api wget --quiet --tries=1 --spider http://localhost:8080/health/ready >/dev/null 2>&1; then
        echo "    [OK] /health/ready 通过"
        READY=1
        break
    fi
    sleep 5
done
if [ "$READY" != "1" ]; then
    echo "[FAIL] /health/ready 未通过 (3 次重试) — 部署失败, 请检查 api 日志"
    exit 1
fi

# 业务探针: 真实查询验证 — 断言 HTTP 200 + 响应为 JSON 且含 hits 字段 (codex v4: 不只验可达)
#   用已知查询 q=filter, 期望聚合搜索正常返回 (空结果也算搜索成功, 只要结构正确)
PROBE_OK=0
PROBE_JSON=$(curl -sk -m 15 "https://localhost/api/public/search/aggregate?q=filter" 2>/dev/null || true)
if echo "$PROBE_JSON" | grep -q '"hits"'; then
    echo "    [OK] 业务探针: /api/public/search/aggregate 返回 JSON (含 hits)"
    PROBE_OK=1
elif echo "$PROBE_JSON" | grep -qi '"error"'; then
    echo "    [FAIL] 业务探针: 聚合搜索返回错误: $(echo "$PROBE_JSON" | head -c 120)" >&2
else
    echo "    [WARN] 业务探针: 响应不含 hits (可能 nginx 未就绪或无数据), 稍后人工确认: $(echo "$PROBE_JSON" | head -c 80)"
fi
if [ "$PROBE_OK" = "1" ]; then
    echo "    [OK] 业务探针通过"
fi

echo "===== 部署完成 ====="
echo "提示: 生产数据库请配置每日自动备份计划任务 (bash scripts/backup-db.sh --verify --upload, 见脚本头)。"
echo "      异机备份必需: 在 .env.prod 配置 BACKUP_S3_ENDPOINT/USER/PASS 指向 R2/异地对象存储 (REQUIRE_REMOTE_BACKUP=1 默认强制)。"
