"""P2 迁移策略回归测试

用法:
  python _test_p2_migration.py

流程:
  1. grep 验证 backend/migrations/ 下剩余 SQL 文件头含 "一次性脚本,不可重跑" 注释
  2. grep 验证 Program.cs 含 EnsureEfmigrationsHistorySeededAsync
  3. grep 验证 appsettings.json 含 "AutoMigrateOnStartup": false
  4. grep 验证 appsettings.Development.json 含 "AutoMigrateOnStartup": true
  5. grep 验证 Program.cs 含 SetCommandTimeout(60) 和 MigrateAsync

 WHY 此测试: P2-7.1 之前 migration 文件可重跑导致重复 CREATE 报错,
   现在所有手写 SQL 标注 "一次性脚本,不可重跑" + EF Core 迁移由
   EnsureEfmigrationsHistorySeededAsync + MigrateAsync 兜底
"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent


def read_file(rel_path):
    """读取项目相对路径文件, 返回文本; 不存在返回空字符串"""
    p = Path(REPO / rel_path)
    if not p.exists():
        return ""
    return p.read_text(encoding="utf-8")


def list_sql_files(migrations_dir):
    """列出 migrations 目录下所有 .sql 文件 (相对项目路径)"""
    md = Path(REPO / migrations_dir)
    if not md.exists():
        return []
    return [f"{migrations_dir}/{f.name}" for f in sorted(md.glob("*.sql"))]


def main():
    print("=" * 70)
    print("P2 迁移策略回归测试")
    print("=" * 70)

    pass_cnt = 0
    fail_cnt = 0
    warn_cnt = 0

    # ===== 用例 1: backend/migrations/ 下 SQL 文件头含 "一次性脚本,不可重跑" =====
    print("\n[用例 1] backend/migrations/ 下 SQL 文件头含 '一次性脚本,不可重跑' 注释")
    sql_files = list_sql_files("backend/migrations")
    if not sql_files:
        print(f"  [FAIL] 未找到任何 .sql 文件 (目录 backend/migrations 不存在或为空)")
        fail_cnt += 1
    else:
        missing = []
        for rel in sql_files:
            content = read_file(rel)
            # 检查文件头前 200 字符内是否含 "一次性脚本" 和 "不可重跑"
            head = content[:200]
            if "一次性脚本" not in head or "不可重跑" not in head:
                missing.append(rel)
        if not missing:
            print(f"  [PASS] {len(sql_files)} 个 SQL 文件头均含 '一次性脚本,不可重跑' 注释")
            pass_cnt += 1
        else:
            print(f"  [FAIL] {len(missing)} 个 SQL 文件头缺少注释:")
            for f in missing[:5]:
                print(f"         - {f}")
            fail_cnt += 1

    # ===== 用例 2: Program.cs 含 EnsureEfmigrationsHistorySeededAsync =====
    print("\n[用例 2] Program.cs 含 EnsureEfmigrationsHistorySeededAsync (EF 迁移历史兜底)")
    content = read_file("backend/src/SakuraFilter.Api/Program.cs")
    if "EnsureEfmigrationsHistorySeededAsync" in content:
        print(f"  [PASS] 找到 EnsureEfmigrationsHistorySeededAsync 调用/定义")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 EnsureEfmigrationsHistorySeededAsync")
        fail_cnt += 1

    # ===== 用例 3: appsettings.json 含 "AutoMigrateOnStartup": false =====
    print('\n[用例 3] appsettings.json 含 "AutoMigrateOnStartup": false (生产环境禁用自动迁移)')
    content = read_file("backend/src/SakuraFilter.Api/appsettings.json")
    # 匹配 "AutoMigrateOnStartup": false (容忍空格)
    if re.search(r'"AutoMigrateOnStartup"\s*:\s*false', content):
        print(f"  [PASS] appsettings.json: AutoMigrateOnStartup=false")
        pass_cnt += 1
    else:
        print(f"  [FAIL] appsettings.json 未设置 AutoMigrateOnStartup=false")
        fail_cnt += 1

    # ===== 用例 4: appsettings.Development.json 含 "AutoMigrateOnStartup": true =====
    print('\n[用例 4] appsettings.Development.json 含 "AutoMigrateOnStartup": true (开发环境启用)')
    content = read_file("backend/src/SakuraFilter.Api/appsettings.Development.json")
    if re.search(r'"AutoMigrateOnStartup"\s*:\s*true', content):
        print(f"  [PASS] appsettings.Development.json: AutoMigrateOnStartup=true")
        pass_cnt += 1
    else:
        print(f"  [FAIL] appsettings.Development.json 未设置 AutoMigrateOnStartup=true")
        fail_cnt += 1

    # ===== 用例 5: Program.cs 含 SetCommandTimeout(60) 和 MigrateAsync =====
    print("\n[用例 5] Program.cs 含 SetCommandTimeout(60) 和 MigrateAsync")
    content = read_file("backend/src/SakuraFilter.Api/Program.cs")
    has_timeout = bool(re.search(r"SetCommandTimeout\s*\(\s*60\s*\)", content))
    has_migrate = "MigrateAsync" in content
    if has_timeout and has_migrate:
        print(f"  [PASS] SetCommandTimeout(60) 和 MigrateAsync 均存在")
        pass_cnt += 1
    else:
        print(f"  [FAIL] SetCommandTimeout(60)={has_timeout} MigrateAsync={has_migrate}")
        fail_cnt += 1

    # ===== 汇总 =====
    print("\n" + "=" * 70)
    print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
    print("===== 验证完成 =====")
    if fail_cnt > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
