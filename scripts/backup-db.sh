#!/usr/bin/env bash
# =============================================================
# 生产数据库每日备份脚本 (pg_dump 自定义格式, 保留 7 天)
#
# 用法:
#   bash scripts/backup-db.sh              # 备份一次
#   bash scripts/backup-db.sh --verify     # 备份 + pg_restore -l 校验归档可读
#
# 调度 (Windows 生产机):
#   用任务计划程序注册每日 03:00 运行 (沙箱内无法注册系统级计划任务,
#   需在"任务计划程序" GUI 手动添加或使用 schtasks 提权执行):
#     Program:  C:\Program Files\Git\bin\bash.exe
#     Arguments: -lc "cd /f/sakurafilter-real && bash scripts/backup-db.sh"
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
    OBJS=$(docker run --rm -v "F:/sakurafilter-real/${BACKUP_DIR}:/backup:ro" \
        postgres:16-alpine pg_restore -l "/backup/$(basename "$OUT_FILE")" 2>/dev/null | wc -l)
    echo "✅ 归档校验通过: $OBJS 个对象"
fi

# 清理超过 KEEP_DAYS 的旧备份 (只删本库文件, 不误删其他)
find "$BACKUP_DIR" -name "${PG_DB}_*.dump" -mtime "+${KEEP_DAYS}" -delete
echo "==> 已清理 ${KEEP_DAYS} 天前的 ${PG_DB} 备份"

# 列出当前保留
echo "==> 当前备份:"
ls -lht "$BACKUP_DIR"/"${PG_DB}"_*.dump 2>/dev/null | awk '{print "   ", $5, $9}'
