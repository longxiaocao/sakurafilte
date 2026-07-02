"""
SakuraFilter 数据清洗脚本 (Day 5)
=================================
输入: 新思路.xlsx (3 个核心 sheet: 产品区 / OEM区 / 机型区)
输出: spike-test/output/cleaned/{products.jsonl, xrefs.jsonl, apps.jsonl}

源数据 Sheet 结构 (实测):
- 产品区: 23 列, 2132 行 (产品主数据, 含尺寸/技术参数)
- OEM区: 4 列, 2017 行 (交叉引用, 1 个产品对应 N 个替代)
- 机型区: 9 列, 1585 行 (机型适配, 1 个产品对应 N 个适配)

3 表通过 'OEM NO.2' 关联 (注意: 'OEM NO.2' 是产品表的 OEM,不是替代号)

处理的 5 大脏点:
1. 幽灵表头与分隔线 - 含 'machine brand' 的行, 全为 '-' 的分隔行
2. 尺寸带单位后缀 - '178.0 mm' -> 178.0
3. 欧洲小数格式 - '5,5' -> 5.5
4. 日期带特殊符号 - '2008-08-01>' -> 拆分为 start_date + is_ongoing
5. 大量冗余空列 - 设备适配信息 (机型/引擎) 拆出到独立数组字段 (本脚本已通过多 sheet 拆分处理)

使用: python etl_clean.py <input.xlsx> <output_dir>
"""
import sys
import re
import json
import logging
from pathlib import Path
from datetime import datetime
import pandas as pd

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    handlers=[
        logging.FileHandler('etl_clean.log', encoding='utf-8'),
        logging.StreamHandler()
    ]
)
log = logging.getLogger(__name__)

stats = {
    'products_read': 0,
    'products_emitted': 0,
    'xrefs_read': 0,
    'xrefs_emitted': 0,
    'apps_read': 0,
    'apps_emitted': 0,
    'apps_aligned_dropped': 0,   # Day 7: 与 products OEM 集对齐时丢弃
    'xrefs_aligned_dropped': 0,  # Day 7: 与 products OEM 集对齐时丢弃
    'dropped_header': 0,
    'dropped_separator': 0,
    'parsed_unit_suffix': 0,
    'parsed_eu_decimal': 0,
    'parsed_special_date': 0,
    'oem_normalized': 0,
    'oem_display_kept': 0,
}


# Day 7.5: 各 sheet 必填列(实际数据中真实存在,大小写敏感校验)
# WHY: 真实 Excel 列名可能是 'machine brand' (小写) 而非代码中查的 'Machine Brand' (大写),
#      大小写不敏感的 get() 已能容忍,但加启动时严格校验能更早暴露 schema 漂移
REQUIRED_COLUMNS = {
    '产品区': ['OEM NO.2', 'product name 3', 'Dimension 1 (D1)'],
    'OEM区': ['OEM NO.2', ' OEM Brand', 'OEM NO.3'],
    '机型区': ['OEM NO.2', 'Machine Brand', 'Machine Model', 'Engine Brand', 'Engine Type', 'Engine Energy'],
}

# Day 11 改进 3: 期望列清单 (与 Product.cs / MachineApplication.cs 实体字段对齐)
#   - validate_columns 只校验 REQUIRED_COLUMNS (必填, 缺失抛异常)
#   - audit_columns 对比 EXPECTED_COLUMNS (全量期望, 缺失只 warning)
#   - 防止再出现 product_name_1/2 漏读那种事故 (源有列, 清洗脚本没读, ETL 也没读)
EXPECTED_COLUMNS = {
    '产品区': [
        'OEM NO.2', 'Product Name 1', 'Product Name 2', 'product name 3', 'Remark',
        'Dimension 1 (D1)', 'Dimension 2 (D2)', 'Dimension 3 (D3)',
        'Height 1 (H1)', 'Height 2 (H2)', 'Height 3 (H3)',
        'Thread 1 (D7)', 'Thread 2 (D8)', 'Media', 'Seal Material',
        'Efficiency 1', 'Efficiency 2',
        'Bypass Valve Setting (LR)', 'Bypass Valve Setting (HR)',
        'Δ Collapse Pressure', 'Temperature Range', 'Bypass Pressure',
    ],
    'OEM区': ['OEM NO.2', ' OEM Brand', 'Product Name 1', 'OEM NO.3'],
    '机型区': [
        'OEM NO.2', 'Machine Brand', 'Machine Model', 'Model Name',
        'Engine Brand', 'Engine Type', 'Engine Energy', 'Production Date',
    ],
}


def validate_columns(sheet_name: str, actual_cols: list[str], strict: bool = True) -> list[str]:
    """Day 7.5: 启动时校验必填列是否存在(大小写不敏感,空格归一)
    返回缺失列名列表;strict=True 时任一缺失就抛异常
    """
    required = REQUIRED_COLUMNS.get(sheet_name, [])
    actual_norm = {str(c).strip().lower(): str(c).strip() for c in actual_cols}
    missing = []
    for req in required:
        if req.strip().lower() not in actual_norm:
            missing.append(req)
    if missing and strict:
        raise ValueError(
            f"[列名校验失败] sheet '{sheet_name}' 缺少必填列: {missing}\n"
            f"   实际列: {list(actual_cols)}"
        )
    elif missing:
        log.warning(f"sheet '{sheet_name}' 缺少必填列: {missing}")
    else:
        log.info(f"sheet '{sheet_name}' 列名校验通过 ({len(required)} 项必填列)")
    return missing


def audit_columns(sheet_name: str, actual_cols: list[str]) -> None:
    """Day 11 改进 3: 源数据溯源 — 打印实际列名 + 与期望清单差集对比

    WHY: 之前 product_name_1/2 在 Excel 源数据中存在, 但 etl_clean.py 没读取,
         ETL 也没写入, 导致 products 表这两列全 NULL。持续多轮未发现。
         本函数启动时打印实际列名清单, 并与 EXPECTED_COLUMNS 做差集:
          - 实际有期望无: 源数据新增字段, 可能需要补充读取逻辑 (info)
          - 期望有实际无: 源数据缺字段, 数据质量问题 (warning)
    不抛异常, 只日志报告 (strict 校验由 validate_columns 负责)
    """
    expected = EXPECTED_COLUMNS.get(sheet_name, [])
    if not expected:
        return
    actual_norm = {str(c).strip().lower(): str(c).strip() for c in actual_cols}
    expected_norm = {c.strip().lower(): c.strip() for c in expected}

    # 实际有, 期望无 (源新增字段)
    extra = [actual_norm[k] for k in actual_norm if k not in expected_norm]
    # 期望有, 实际无 (源缺字段)
    missing_optional = [expected_norm[k] for k in expected_norm if k not in actual_norm]

    log.info(f"=== {sheet_name} 源数据列溯源 ===")
    log.info(f"  实际列 ({len(actual_cols)}): {list(actual_cols)}")
    if extra:
        log.info(f"  源新增字段 ({len(extra)}, 可能需补充读取逻辑): {extra}")
    if missing_optional:
        log.warning(f"  ⚠ 期望字段缺失 ({len(missing_optional)}, 数据将置 NULL): {missing_optional}")
    if not extra and not missing_optional:
        log.info(f"  ✓ 期望字段全齐 ({len(expected)} 项)")


def is_header_row(row: pd.Series) -> bool:
    """脏点 1: 表头行 (含 'machine brand' 文本)"""
    return any('machine brand' in str(v).lower() for v in row.values if pd.notna(v))


def is_separator_row(row: pd.Series) -> bool:
    """脏点 1: 全为 '-' 或 NaN 的分隔行"""
    non_empty = [str(v).strip() for v in row.values if pd.notna(v)]
    return len(non_empty) == 0 or all(v == '-' for v in non_empty)


def clean_decimal(val) -> float | None:
    """脏点 2 + 3: 剥离 ' mm' 单位 + 替换欧洲小数 ',' -> '.'"""
    if pd.isna(val):
        return None
    s = str(val).strip()
    if not s or s == '-':
        return None
    s = re.sub(r'\s*mm\s*$', '', s, flags=re.IGNORECASE)
    s = s.replace(',', '.')
    try:
        return float(s)
    except ValueError:
        return None


def clean_string(val) -> str | None:
    if pd.isna(val):
        return None
    s = str(val).strip()
    if not s or s == '-':
        return None
    return s


def clean_date(val) -> tuple[datetime | None, bool]:
    """脏点 4: '2008-08-01>' -> (date, is_greater_than)"""
    if pd.isna(val):
        return None, False
    s = str(val).strip()
    if not s or s == '-':
        return None, False
    is_greater = s.endswith('>')
    if is_greater:
        s = s[:-1].strip()
    for fmt in ('%Y-%m-%d', '%Y/%m/%d', '%d.%m.%Y', '%Y%m%d'):
        try:
            return datetime.strptime(s, fmt), is_greater
        except ValueError:
            continue
    return None, is_greater


def normalize_oem(s: str) -> str:
    """OEM 标准化: SA 42359 -> SA42359, 去除空格/连字符/下划线"""
    return re.sub(r'[\s\-_/]', '', s).upper()


def clean_products_sheet(df: pd.DataFrame) -> list[dict]:
    """处理 产品区 sheet"""
    out = []
    for _, row in df.iterrows():
        stats['products_read'] += 1

        if is_header_row(row):
            stats['dropped_header'] += 1
            continue
        if is_separator_row(row):
            stats['dropped_separator'] += 1
            continue

        # 关键: Type 在 'product name 3' 列,不是 'Type' 列
        oem_display = clean_string(row.get('OEM NO.2'))
        if not oem_display:
            continue
        oem_norm = normalize_oem(oem_display)
        stats['oem_normalized'] += 1
        stats['oem_display_kept'] += 1

        type_val = clean_string(row.get('product name 3')) or 'UNKNOWN'

        d1 = clean_decimal(row.get('Dimension 1 (D1)'))
        if d1 is not None:
            stats['parsed_unit_suffix'] += 1

        prod_date, is_ongoing = clean_date(row.get('Production Date'))  # 机型区有,产品区无
        if prod_date is not None:
            stats['parsed_special_date'] += 1

        out.append({
            'oem_no_display': oem_display,
            'oem_no_normalized': oem_norm,
            'type': type_val,
            # WHY 新增: Excel 规范分区 1 主信息区 Product Name 1/2 (产品主名/副名)
            #   - 与 cross_references.product_name_1 区分: xref 自带的 product_name_1 是该 brand 对应名
            #   - 业务规则: 当 product_name_1 和 product_name_3 同时存在时前端只显示 product_name_1
            'product_name_1': clean_string(row.get('Product Name 1')),
            'product_name_2': clean_string(row.get('Product Name 2')),
            'product_name_3': clean_string(row.get('product name 3')),
            'remark': clean_string(row.get('Remark')),
            'd1_mm': d1,
            'd2_mm': clean_decimal(row.get('Dimension 2 (D2)')),
            'd3_mm': clean_decimal(row.get('Dimension 3 (D3)')),
            'h1_mm': clean_decimal(row.get('Height 1 (H1)')),
            'h2_mm': clean_decimal(row.get('Height 2 (H2)')),
            'h3_mm': clean_decimal(row.get('Height 3 (H3)')),
            'd7_thread': clean_string(row.get('Thread 1 (D7)')),
            'd8_thread': clean_string(row.get('Thread 2 (D8)')),
            'media': clean_string(row.get('Media')),
            'sealing_material': clean_string(row.get('Seal Material')),
            'efficiency_1': clean_string(row.get('Efficiency 1')),
            'efficiency_2': clean_string(row.get('Efficiency 2')),
            'bypass_valve_lr': clean_decimal(row.get('Bypass Valve Setting (LR)')),
            'bypass_valve_hr': clean_decimal(row.get('Bypass Valve Setting (HR)')),
            'collapse_pressure_bar': clean_decimal(row.get('Δ Collapse Pressure')),
            'temp_range': clean_string(row.get('Temperature Range')),
            'bypass_pressure': clean_decimal(row.get('Bypass Pressure')),
        })
        stats['products_emitted'] += 1
    return out


def clean_xrefs_sheet(df: pd.DataFrame) -> list[dict]:
    """处理 OEM区 sheet (交叉引用)"""
    out = []
    for _, row in df.iterrows():
        stats['xrefs_read'] += 1
        if is_header_row(row) or is_separator_row(row):
            continue

        oem = clean_string(row.get('OEM NO.2'))
        if not oem:
            continue
        brand = clean_string(row.get(' OEM Brand'))  # 注意前导空格
        if not brand:
            continue

        out.append({
            'product_oem': normalize_oem(oem),
            'product_name_1': clean_string(row.get('Product Name 1')),
            'oem_brand': brand,
            'oem_no_3': clean_string(row.get('OEM NO.3')),
        })
        stats['xrefs_emitted'] += 1
    return out


def clean_apps_sheet(df: pd.DataFrame) -> list[dict]:
    """处理 机型区 sheet (机型适配)
    Day 7 修复: 实际 Excel 列名是小写 ('machine brand'/'machine model'),
    原脚本查 'Machine Brand' 返回 None,导致 machine_brand 全为 None 被 SQL 过滤掉
    解决: 大小写不敏感查列
    """
    # Day 7: 构造小写 -> 原列名 的映射
    col_map = {str(c).strip().lower(): str(c).strip() for c in df.columns}

    def get(row, name: str):
        """大小写不敏感取列"""
        real = col_map.get(name.lower())
        return row.get(real) if real else None

    out = []
    for _, row in df.iterrows():
        stats['apps_read'] += 1
        if is_header_row(row) or is_separator_row(row):
            continue

        oem = clean_string(get(row, 'OEM NO.2'))
        if not oem:
            continue

        prod_date, is_ongoing = clean_date(get(row, 'Production Date'))
        if prod_date is not None:
            stats['parsed_special_date'] += 1

        out.append({
            'product_oem': normalize_oem(oem),
            'machine_brand': clean_string(get(row, 'Machine Brand')),
            'machine_model': clean_string(get(row, 'Machine Model')),
            'model_name': clean_string(get(row, 'Model Name')),
            'engine_brand': clean_string(get(row, 'Engine Brand')),
            'engine_type': clean_string(get(row, 'Engine Type')),
            'engine_energy': clean_string(get(row, 'Engine Energy')),
            'production_date_start': prod_date.isoformat() if prod_date else None,
            'is_ongoing': is_ongoing,
        })
        stats['apps_emitted'] += 1
    return out


def main():
    if len(sys.argv) < 3:
        print("用法: python etl_clean.py <input.xlsx> <output_dir>")
        sys.exit(1)

    in_path = Path(sys.argv[1])
    out_dir = Path(sys.argv[2])
    out_dir.mkdir(parents=True, exist_ok=True)

    log.info(f"读取: {in_path}")
    xls = pd.ExcelFile(in_path)
    log.info(f"Sheet 列表: {xls.sheet_names}")

    # 1) 产品区
    if '产品区' in xls.sheet_names:
        df_p = pd.read_excel(xls, sheet_name='产品区')
        df_p.columns = [str(c).strip() for c in df_p.columns]
        log.info(f"产品区: {len(df_p)} 行, {len(df_p.columns)} 列")
        validate_columns('产品区', list(df_p.columns), strict=True)  # Day 7.5
        audit_columns('产品区', list(df_p.columns))  # Day 11 改进 3: 源数据溯源
        products = clean_products_sheet(df_p)
    else:
        products = []

    # 2) OEM区
    if 'OEM区' in xls.sheet_names:
        df_x = pd.read_excel(xls, sheet_name='OEM区')
        df_x.columns = [str(c) for c in df_x.columns]  # 保留前导空格以便识别 ' OEM Brand'
        log.info(f"OEM区: {len(df_x)} 行, {len(df_x.columns)} 列")
        validate_columns('OEM区', list(df_x.columns), strict=True)  # Day 7.5
        audit_columns('OEM区', list(df_x.columns))  # Day 11 改进 3: 源数据溯源
        xrefs = clean_xrefs_sheet(df_x)
    else:
        xrefs = []

    # 3) 机型区
    if '机型区' in xls.sheet_names:
        df_a = pd.read_excel(xls, sheet_name='机型区')
        df_a.columns = [str(c).strip() for c in df_a.columns]
        log.info(f"机型区: {len(df_a)} 行, {len(df_a.columns)} 列")
        validate_columns('机型区', list(df_a.columns), strict=True)  # Day 7.5
        audit_columns('机型区', list(df_a.columns))  # Day 11 改进 3: 源数据溯源
        apps = clean_apps_sheet(df_a)
    else:
        apps = []

    # 4) Day 7: 与 products OEM 集对齐 — 过滤掉 product_oem 不在 products 中的 xrefs/apps
    #    源数据:apps 引用了产品区不存在的 OEM(机型区/产品区来源 sheet 不同),
    #    不对齐会导致 ETL 时 96% 行被 skip,progress 报告看不出真实效果
    product_oem_set = {p['oem_no_normalized'] for p in products}
    before_x = len(xrefs); before_a = len(apps)
    xrefs = [x for x in xrefs if x['product_oem'] in product_oem_set]
    apps = [a for a in apps if a['product_oem'] in product_oem_set]
    stats['xrefs_aligned_dropped'] = before_x - len(xrefs)
    stats['apps_aligned_dropped'] = before_a - len(apps)
    log.info(f"对齐: xrefs 丢弃 {before_x - len(xrefs)} 条, apps 丢弃 {before_a - len(apps)} 条 (产品集 {len(product_oem_set)} 个 OEM)")

    # 5) 写出 JSONL
    for name, items in [('products', products), ('xrefs', xrefs), ('apps', apps)]:
        path = out_dir / f'{name}.jsonl'
        with open(path, 'w', encoding='utf-8') as f:
            for item in items:
                f.write(json.dumps(item, ensure_ascii=False) + '\n')
        log.info(f"写出: {path} ({len(items)} 条)")

    # 6) 汇总
    summary_path = out_dir / '_clean_summary.json'
    with open(summary_path, 'w', encoding='utf-8') as f:
        json.dump({'stats': stats, 'sheets_processed': [s for s in ['产品区', 'OEM区', '机型区'] if s in xls.sheet_names]}, f, indent=2, ensure_ascii=False)
    log.info(f"汇总: {summary_path}")
    log.info(f"最终统计: {json.dumps(stats, indent=2, ensure_ascii=False)}")


if __name__ == '__main__':
    main()
