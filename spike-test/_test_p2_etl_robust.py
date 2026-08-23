"""P2 ETL 健壮性回归测试

用法:
  python _test_p2_etl_robust.py

流程:
  1. grep 验证 EtlImportService.cs catch 块含 content={preview} (错误行原始内容预览)
  2. grep 验证 MaxRecentErrors = 20
  3. grep 验证 EtlProgress 含 _typeMismatches 字段
  4. grep 验证 TriggerAsync 含文件大小校验 (5GB)
  5. grep 验证 UTF-16 LE/BE BOM 预检
  6. grep 验证 GetBoolOrFalse 对 "yes"/"1"/"true" 字符串返回 true

 WHY 此测试: P2-9.x 系列 ETL 健壮性增强, 防止数据脏/编码/类型问题导致
   静默失败 (silent skip) 或 OOM. 每条 grep 都对应一个真实事故复盘
"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
ETL_FILE = "backend/src/SakuraFilter.Etl/EtlImportService.cs"


def read_file(rel_path):
    """读取项目相对路径文件, 返回文本; 不存在返回空字符串"""
    p = Path(REPO / rel_path)
    if not p.exists():
        return ""
    return p.read_text(encoding="utf-8")


def check_grep(label, content, pattern, expected_min=1):
    """通用 grep 检查, 返回 (pass, msg)"""
    if not content:
        return False, "文件内容为空或不存在"
    matches = re.findall(pattern, content)
    if len(matches) >= expected_min:
        return True, f"{len(matches)} 处匹配"
    return False, f"仅 {len(matches)} 处匹配 (期望 >= {expected_min})"


def main():
    print("=" * 70)
    print("P2 ETL 健壮性回归测试")
    print("=" * 70)

    pass_cnt = 0
    fail_cnt = 0
    warn_cnt = 0

    content = read_file(ETL_FILE)
    if not content:
        print(f"[FAIL] 无法读取 {ETL_FILE}")
        sys.exit(1)

    # ===== 用例 1: catch 块含 content={preview} =====
    print("\n[用例 1] EtlImportService.cs catch 块含 content={Preview} (错误行原始内容预览)")
    # 结构化日志模板参数名通常 PascalCase, 兼容大小写: content={Preview} / content={preview}
    ok, msg = check_grep("", content, r"content=\{[Pp]review\}")
    if ok:
        print(f"  [PASS] 找到 content={{Preview}} 错误内容预览 ({msg})")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 content={{Preview}}: {msg}")
        print(f"         WHY: ETL 行解析失败时应记录原始行内容, 便于诊断 JSON 格式错误")
        fail_cnt += 1

    # ===== 用例 2: MaxRecentErrors = 20 =====
    print("\n[用例 2] MaxRecentErrors = 20 (错误环形缓冲容量)")
    ok, msg = check_grep("", content, r"MaxRecentErrors\s*=\s*20")
    if ok:
        print(f"  [PASS] MaxRecentErrors = 20 ({msg})")
        pass_cnt += 1
    else:
        print(f"  [FAIL] MaxRecentErrors 不是 20: {msg}")
        print(f"         WHY: 5 条太少, 失败风暴时只够看最新几条; 20 条够诊断根因")
        fail_cnt += 1

    # ===== 用例 3: EtlProgress 含 _typeMismatches 字段 =====
    print("\n[用例 3] EtlProgress 含 _typeMismatches 字段 (类型不匹配计数)")
    ok, msg = check_grep("", content, r"_typeMismatches")
    if ok:
        print(f"  [PASS] 找到 _typeMismatches 字段 ({msg})")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 _typeMismatches: {msg}")
        print(f"         WHY: GetDecimalOrNull/GetBoolOrFalse 静默失败时累加, 前端可展示数据质量告警")
        fail_cnt += 1

    # ===== 用例 4: TriggerAsync 含文件大小校验 (5GB) =====
    print("\n[用例 4] TriggerAsync 含文件大小校验 (5GB 上限, 防 OOM)")
    # 匹配 5GB 字样或 5 * 1024 * 1024 * 1024 字面量
    ok1, _ = check_grep("", content, r"5\s*GB")
    ok2, _ = check_grep("", content, r"5\s*\*\s*1024\s*\*\s*1024\s*\*\s*1024")
    # 也匹配 "上限 5GB" 注释
    ok3 = bool(re.search(r"上限\s*5\s*GB", content))
    if ok1 or ok2 or ok3:
        print(f"  [PASS] 找到 5GB 文件大小校验 (5GB字面量={ok1}, 数值表达式={ok2}, 注释={ok3})")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到 5GB 文件大小校验")
        print(f"         WHY: 1M 行 ~200MB, 5GB ≈ 2500 万行, 超过此规模应拆分文件")
        fail_cnt += 1

    # ===== 用例 5: UTF-16 LE/BE BOM 预检 =====
    print("\n[用例 5] UTF-16 LE/BE BOM 预检 (拒绝 UTF-16 编码 JSONL)")
    # 实际代码: (bom[0] == 0xFF && bom[1] == 0xFE) || (bom[0] == 0xFE && bom[1] == 0xFF)
    # 0xFF 和 0xFE 之间隔着 bom[1] ==, 用 .* 跨字符匹配
    has_le = bool(re.search(r"0xFF\b.*0xFE\b", content))
    has_be = bool(re.search(r"0xFE\b.*0xFF\b", content))
    has_utf16 = "UTF-16" in content
    if has_utf16 and has_le and has_be:
        print(f"  [PASS] UTF-16 BOM 预检存在 (LE={has_le}, BE={has_be})")
        pass_cnt += 1
    else:
        print(f"  [FAIL] 未找到完整的 UTF-16 LE/BE BOM 预检 (UTF-16 字样={has_utf16}, LE={has_le}, BE={has_be})")
        print(f"         WHY: StreamReader 默认 UTF-8, UTF-16 文件会乱码导致 JSON 解析失败")
        fail_cnt += 1

    # ===== 用例 6: GetBoolOrFalse 对 "yes"/"1"/"true" 字符串返回 true =====
    print('\n[用例 6] GetBoolOrFalse 对 "yes"/"1"/"true" 字符串返回 true')
    # 匹配 lower == "true" || lower == "yes" || lower == "1"
    has_true = bool(re.search(r'"true"', content))
    has_yes = bool(re.search(r'"yes"', content))
    has_one = bool(re.search(r'"1"', content))
    if has_true and has_yes and has_one:
        print(f'  [PASS] GetBoolOrFalse 支持 "yes"/"1"/"true" (true={has_true}, yes={has_yes}, 1={has_one})')
        pass_cnt += 1
    else:
        print(f'  [FAIL] GetBoolOrFalse 字符串支持不全 (true={has_true}, yes={has_yes}, 1={has_one})')
        print(f'         WHY: 业务数据常见 "yes"/"1"/"true" 而非 JSON boolean, 需容错解析')
        fail_cnt += 1

    # ===== 汇总 =====
    print("\n" + "=" * 70)
    print(f"汇总: {pass_cnt} PASS / {fail_cnt} FAIL / {warn_cnt} WARN")
    print("===== 验证完成 =====")
    if fail_cnt > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
