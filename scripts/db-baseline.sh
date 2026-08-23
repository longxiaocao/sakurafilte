#!/usr/bin/env bash
# Day 10+ P0.2: EF Core Migrations baseline 一键脚本 (Linux/macOS)
#   用途: 在 spike_test_v3 数据库 (本地/CI) seed 4 个老 EF Core migration 记录,
#         让 EF Core Migrate 只跑新加的 migration, 避免 DROP/ALTER 失败.
#
# 前提条件:
#   1) PG 16+ 已启动, spike_test_v3 数据库已创建
#      - 默认连接: localhost:5432 / postgres / 784533
#   2) Python 3.10+ + psycopg2-binary 已安装
#      - pip install psycopg2-binary
#   3) 后端 (dotnet run) 未启动, 避免 EF Core Migrate 抢跑
#
# 退出码:
#   0 = baseline seed 成功
#   1 = baseline seed 参数错
#   2 = baseline seed DB 连接失败
#
# 后续步骤:
#   cd backend/src/SakuraFilter.Api
#   dotnet run -c Debug
#   # EF Core Migrate 会自动只跑未应用的 migration

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
BASELINE_SCRIPT="$REPO_ROOT/spike-test/_ef_migrations_baseline.py"

if [ ! -f "$BASELINE_SCRIPT" ]; then
  echo "[error] 找不到 baseline 脚本: $BASELINE_SCRIPT" >&2
  exit 1
fi

# 默认参数: spike_test_v3 / 4 个老 migration (与 _ef_migrations_baseline.py DEFAULT_MIGRATIONS 一致)
# 可通过环境变量覆盖:
#   PG_HOST=localhost PG_PORT=5432 PG_DB=spike_test_v3 PG_USER=postgres PG_PASSWORD=784533
echo "=== EF Core Migrations baseline seed (本地开发) ==="
echo "Repo:   $REPO_ROOT"
echo "Script: $BASELINE_SCRIPT"
echo

python "$BASELINE_SCRIPT" \
  --pg-host="${PG_HOST:-localhost}" \
  --pg-port="${PG_PORT:-5432}" \
  --pg-db="${PG_DB:-spike_test_v3}" \
  --pg-user="${PG_USER:-postgres}" \
  --pg-password="${PG_PASSWORD:-784533}"

EXIT_CODE=$?

echo
if [ $EXIT_CODE -eq 0 ]; then
  echo "✓ baseline seed 成功. 现在可以 dotnet run 启动后端."
  echo "  cd backend/src/SakuraFilter.Api"
  echo "  dotnet run -c Debug"
else
  echo "✗ baseline seed 失败 (exit=$EXIT_code)" >&2
  echo "  退出码: 1=参数错, 2=DB 连接失败" >&2
fi

exit $EXIT_CODE
