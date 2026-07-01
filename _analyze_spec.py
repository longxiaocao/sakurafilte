"""
分析 4 个规格表: 后台新增产品格式, 后台搜索统筹, 对比界面, 前端展示内容, 各分区管理界面
"""
import pandas as pd

FILE = r"d:\projects\sakurafilter\新思路.xlsx"

for sn in ["后台新增产品格式", "后台搜索统筹", "对比界面", "前端展示内容", "各分区新增内容的管理界面"]:
    df = pd.read_excel(FILE, sheet_name=sn, header=None)
    print("=" * 80)
    print(f"【{sn}】 {df.shape[0]} 行 x {df.shape[1]} 列")
    print("=" * 80)
    # 完整打印, 不截断
    for i, row in df.iterrows():
        cells = [str(x) if pd.notna(x) else '' for x in row.tolist()]
        cells = [c for c in cells if c.strip()]  # 去空
        if cells:
            print(f"  R{i+1}: " + " | ".join(cells)[:200])
    print()
