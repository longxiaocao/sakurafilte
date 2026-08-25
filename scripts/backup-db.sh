#!/usr/bin/env bash
# =============================================================
# 生产数据库每日备份脚本 (pg_dump 自定义格式, 保留 7 天)
#
# 用法:
#   bash scripts/backup-db.sh              # 备份一次
#   bash scripts/backup-db.sh --verify     # 备份 + pg_restore -l 校验归档可读
#   bash scripts/backup-db.sh --upload     # 备份 + 上传 MinIO 桶 (异机/多副本)
#   bash scripts/backup-db.sh --verify --upload  # 全开 (推荐每日任务)
#
# 调度 (Windows 生产机):
#   用任务计划程序注册每日 03:00 运行 (沙箱内无法注册系统级计划任务,
#   需在"任务计划程序" GUI 手动添加或使用 schtasks 提权执行):
#     Program:  C:\Program Files\Git\bin\bash.exe
#     Arguments: -lc "cd /f/sakurafilter-real && bash scripts/backup-db.sh --verify --upload"
#
# 恢复示例 (灾难恢复):
#   docker exec -i sakura-postgres pg_restore -U postgres -d sakurafilter \
#     -j 4 < _backups/sakurafilter_YYYYmmdd_HHMMSS.dump
# =============================================================
set -euo pipefail

cd "$(dirname "$0")/.."

ENV_FILE=".env.prod"
if [ ! -f "$ENV_FILE" ]; then
    echo "❌ 未找到 $ENV_FILE (备份脚本需读取 POSTGRES_* 连接信息)" >&2
    exit 1
fi

PG_USER=$(grep -oP '^POSTGRES_USER=\K.*' "$ENV_FILE" | tr -d '"')
PG_DB=$(grep -oP '^POSTGRES_DB=\K.*' "$ENV_FILE" | tr -d '"')
CONTAINER="${PG_CONTAINER:-sakura-postgres}"
BACKUP_DIR="${BACKUP_DIR:-_backups}"
KEEP_DAYS="${KEEP_DAYS:-7}"

VERIFY=""
UPLOAD=""
for a in "$@"; do
    [ "$a" = "--verify" ] && VERIFY=1
    # V3(2026-08-25) 上线审查: --upload 将备份上传 MinIO 桶 (异机/多副本, S3 兼容)
    [ "$a" = "--upload" ] && UPLOAD=1
done
[ "${1:-}" = "--verify" ] && VERIFY=1

STAMP=$(date +%Y%m%d_%H%M%S)
OUT_FILE="${BACKUP_DIR}/${PG_DB}_${STAMP}.dump"
TMP_IN_CONTAINER="/tmp/sakura_backup_${STAMP}.dump"

mkdir -p "$BACKUP_DIR"

echo "==> 备份 $PG_DB (容器 $CONTAINER) → $OUT_FILE"

docker exec "$CONTAINER" pg_dump -U "$PG_USER" -d "$PG_DB" -Fc \
    -f "$TMP_IN_CONTAINER"

docker cp "$CONTAINER:$TMP_IN_CONTAINER" "$OUT_FILE"
docker exec "$CONTAINER" rm -f "$TMP_IN_CONTAINER"

SIZE=$(du -h "$OUT_FILE" | cut -f1)
echo "✅ 备份完成: $OUT_FILE ($SIZE)"

if [ -n "$VERIFY" ]; then
    echo "==> 校验归档可读 (pg_restore -l)..."
    # 🔧 fix(2026-08-24 审核): 原硬编码 "F:/sakurafilter-real/${BACKUP_DIR}" 换目录/机器即挂载失败,
    #   备份可恢复门禁失效。脚本已 cd 到仓库根, 用 $(pwd) 动态推导, 兼容任意安装位置。
    BACKUP_ABS="$(pwd)/${BACKUP_DIR}"
    OBJS=$(docker run --rm -v "${BACKUP_ABS}:/backup:ro" \
        postgres:16-alpine pg_restore -l "/backup/$(basename "$OUT_FILE")" 2>/dev/null | wc -l)
    echo "✅ 归档校验通过: $OBJS 个对象"
fi

# 清理超过 KEEP_DAYS 的旧备份 (只删本库文件, 不误删其他)
find "$BACKUP_DIR" -name "${PG_DB}_*.dump" -mtime "+${KEEP_DAYS}" -delete
echo "==> 已清理 ${KEEP_DAYS} 天前的 ${PG_DB} 备份"

# V3(2026-08-25) 上线审查 v2 (codex): 异机/多副本 — 备份上传对象存储
#   - endpoint 默认用容器网络 http://minio:9000 (mc 在 minio 容器内执行, 127.0.0.1:55900 不通)
#   - 异机备份: 设置 BACKUP_S3_ENDPOINT 指向 R2/异地 MinIO (S3 兼容), 并配 BACKUP_S3_USER/BACKUP_S3_PASS
#   - 上传失败必须返回非零退出码 (任务计划监控退出码, 不能静默成功)
if [ -n "$UPLOAD" ]; then
    MINIO_ALIAS="backup-src"
    MINIO_BUCKET="${BACKUP_MINIO_BUCKET:-sakurafilter-backups}"
    # 默认本机 MinIO 容器 (多副本, 非异机); 异机请配 BACKUP_S3_ENDPOINT (R2 等)
    MINIO_ENDPOINT="${BACKUP_S3_ENDPOINT:-http://minio:9000}"
    MINIO_USER="${BACKUP_S3_USER:-$(grep -oP '^MINIO_ROOT_USER=\K.*' "$ENV_FILE" | tr -d '"')}"
    MINIO_PASS="${BACKUP_S3_PASS:-$(grep -oP '^MINIO_ROOT_PASSWORD=\K.*' "$ENV_FILE" | tr -d '"')}"
    if docker exec sakura-minio sh -c 'command -v mc >/dev/null' 2>/dev/null; then
        echo "==> 上传备份到 $MINIO_ENDPOINT / $MINIO_BUCKET ..."
        # mc cp 不支持 stdin 管道 → 先 docker cp 进 minio 容器, 再 mc cp, 最后清理
        TMP_IN_MC="/tmp/$(basename "$OUT_FILE")"
        if docker cp "$OUT_FILE" "sakura-minio:$TMP_IN_MC" \
            && docker exec sakura-minio mc alias set "$MINIO_ALIAS" "$MINIO_ENDPOINT" "$MINIO_USER" "$MINIO_PASS" >/dev/null 2>&1 \
            && docker exec sakura-minio mc mb --ignore-existing "$MINIO_ALIAS/$MINIO_BUCKET" >/dev/null 2>&1 \
            && docker exec sakura-minio mc cp "$TMP_IN_MC" "$MINIO_ALIAS/$MINIO_BUCKET/" >/dev/null; then
            docker exec sakura-minio rm -f "$TMP_IN_MC" 2>/dev/null || true
            echo "✅ 已上传副本: $MINIO_BUCKET/$(basename "$OUT_FILE")"
            if [ -z "${BACKUP_S3_ENDPOINT:-}" ]; then
                echo "⚠️ 注意: 目标为本机 MinIO (多副本), 非真正异机 — 建议设置 BACKUP_S3_ENDPOINT 指向 R2/异地存储"
            fi
        else
            docker exec sakura-minio rm -f "$TMP_IN_MC" 2>/dev/null || true
            echo "❌ 对象存储上传失败 (endpoint=$MINIO_ENDPOINT bucket=$MINIO_BUCKET) — 本地备份仍有效, 但按失败处理" >&2
            exit 1
        fi
    else
        echo "❌ MinIO 容器无 mc — 请求了 --upload 但无法上传, 按失败处理 (可手动上传 $OUT_FILE 至 R2)" >&2
        exit 1
    fi
fi

# 列出当前保留
echo "==> 当前备份:"
ls -lht "$BACKUP_DIR"/"${PG_DB}"_*.dump 2>/dev/null | awk '{print "   ", $5, $9}'
