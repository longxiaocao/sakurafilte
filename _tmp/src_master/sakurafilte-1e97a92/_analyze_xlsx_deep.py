"""
深度体检三大数据 sheet: 产品区, OEM区, 机型区
"""
import pandas as pd
import re
from collections import Counter

FILE = r"d:\projects\sakurafilter\新思路.xlsx"

print("=" * 80)
print("【一】产品区 (2133 行) — 数据卫生体检")
print("=" * 80)
df_p = pd.read_excel(FILE, sheet_name="产品区")
print(f"列名 ({len(df_p.columns)}):")
for i, c in enumerate(df_p.columns, 1):
    print(f"  {i:2d}. {c}")

# 检测脏点
print("\n[A] OEM NO.2 重复 / 唯一性:")
print(f"    总行: {len(df_p)} | 唯一 OEM 数: {df_p['OEM NO.2'].nunique()} | 空值: {df_p['OEM NO.2'].isna().sum()}")
print(f"    重复 OEM 样本: {df_p['OEM NO.2'].value_counts().head(3).to_dict()}")

print("\n[B] 尺寸列 'D1' 含单位后缀 'mm' 比例:")
d1 = df_p['Dimension 1 (D1)'].dropna()
d1_str = d1.astype(str)
mm_count = d1_str.str.contains('mm', na=False).sum()
print(f"    样本: {d1_str.head(5).tolist()}")
print(f"    含 'mm' 后缀: {mm_count}/{len(d1_str)} ({mm_count/len(d1_str)*100:.1f}%)")

print("\n[C] Type 字段分布 (前 10):")
print(df_p['Type'].value_counts(dropna=False).head(10).to_string())

print("\n[D] 'product name 3' 分布 (注意与 'Product Name 1/2' 不一致):")
print(df_p['product name 3'].value_counts(dropna=False).head(10).to_string())

print("\n[E] 关键字段空值率:")
for c in ['OEM NO.2','Remark','product name 3','Dimension 1 (D1)','Height 1 (H1)','Type','Media']:
    miss = df_p[c].isna().sum()
    print(f"    {c:30s} 空值 {miss:4d}/{len(df_p)} ({miss/len(df_p)*100:.1f}%)")


print("\n" + "=" * 80)
print("【二】OEM区 (2018 行) — 交叉引用结构")
print("=" * 80)
df_o = pd.read_excel(FILE, sheet_name="OEM区")
df_o.columns = [c.strip() for c in df_o.columns]   # 'OEM Brand' 有前导空格
print(f"列名: {df_o.columns.tolist()}")
print(f"总行: {len(df_o)} | 唯一 OEM NO.2: {df_o['OEM NO.2'].nunique()}")
print(f"唯一品牌数: {df_o['OEM Brand'].nunique()}")
print("\n[A] Product Name 1 分布 (前 10):")
print(df_o['Product Name 1'].value_counts(dropna=False).head(10).to_string())
print("\n[B] Top 10 品牌:")
print(df_o['OEM Brand'].value_counts().head(10).to_string())
print("\n[C] OEM NO.3 样本 (看格式):")
for v in df_o['OEM NO.3'].dropna().head(10).tolist():
    print(f"    {repr(v)}")
print("\n[D] 同一 OEM NO.2 对应的交叉数 (Top 5):")
print(df_o['OEM NO.2'].value_counts().head(5).to_string())


print("\n" + "=" * 80)
print("【三】机型区 (1651 行) — 27 列宽表, 车辆/机械适配")
print("=" * 80)
df_m = pd.read_excel(FILE, sheet_name="机型区")
print(f"总行: {len(df_m)} | 总列: {len(df_m.columns)}")
print("\n[A] machine brand 分布 (前 10):")
print(df_m['machine brand'].value_counts().head(10).to_string())
print("\n[B] Production date 样本 (检验 '>' 等特殊符号):")
samples = df_m['Production date'].dropna().astype(str).head(20).tolist()
for s in samples:
    print(f"    {repr(s)}")
print("\n[C] Power / Engine displacement 样本 (检验欧洲小数 ','):")
print("    Power 样本:", df_m['Power'].dropna().astype(str).head(5).tolist())
print("    Displacement 样本:", df_m['Engine displacement'].dropna().astype(str).head(5).tolist())
print("\n[D] 关键列空值率:")
for c in ['machine brand','machine model','OEM NO.2','Engine brand','Engine type','Engine energy','Production date','Power','Serial number (from)','Serial number (to)','Engine displacement']:
    miss = df_m[c].isna().sum()
    print(f"    {c:30s} 空值 {miss:4d}/{len(df_m)} ({miss/len(df_m)*100:.1f}%)")

print("\n[E] Engine energy (燃料) 分布:")
print(df_m['Engine energy'].value_counts(dropna=False).head(10).to_string())

print("\n[F] modelname (设备大类) 分布:")
print(df_m['modelname'].value_counts(dropna=False).head(10).to_string())


print("\n" + "=" * 80)
print("【四】数据交叉性检查 — 三表之间能否 JOIN?")
print("=" * 80)
# OEM NO.2 是主键候选
p_oems = set(df_p['OEM NO.2'].dropna().astype(str))
o_oems = set(df_o['OEM NO.2'].dropna().astype(str))
m_oems = set(df_m['OEM NO.2'].dropna().astype(str))
print(f"产品区 OEM 数: {len(p_oems)}")
print(f"OEM区   OEM 数: {len(o_oems)}")
print(f"机型区  OEM 数: {len(m_oems)}")
print(f"三者交集: {len(p_oems & o_oems & m_oems)}")
print(f"仅在 OEM区 (无产品数据): {len(o_oems - p_oems)}")
print(f"仅在产品区 (无交叉引用): {len(p_oems - o_oems)}")
print(f"仅在机型区 (无主产品): {len(m_oems - p_oems)}")
