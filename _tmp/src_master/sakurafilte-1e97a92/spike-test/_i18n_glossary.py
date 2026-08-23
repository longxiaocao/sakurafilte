"""
SakuraFilter 过滤器行业专业术语词典
======================================
WHY: 现有 _i18n_auto_replace.py 对所有中文都生成 "[EN] {原文[:30]}" 占位,
     英文翻译需要专业过滤器术语, 人工成本高. 本模块提供:
     1. 词条级词典 (zh_term → en_term) - 直接替换
     2. 短语级词典 (zh_phrase → en_phrase) - 整句替换
     3. 词典驱动的翻译函数 translate_zh_to_en() - 自动选择最佳匹配

术语来源:
  - Donaldson / Fleetguard / Baldwin 官方术语表
  - ISO 16889 (液压过滤多次通过法) / ISO 4406 (清洁度代码)
  - Sakura Filter 官网 (sakurafilter.com) 字段命名约定
  - verifiedfilters.com 交叉引用方法论
  - 国内过滤行业术语标准 (senjieguolv.com)

使用方式:
  from _i18n_glossary import translate_zh_to_en
  en = translate_zh_to_en("机油滤清器交叉引用")
  # → "Oil Filter Cross-Reference"

维护:
  新增术语时, 优先加入 PHRASES (完整短语), 再考虑 TERMS (单词).
  保持 key 简短、canonical, value 符合行业标准.
"""
from __future__ import annotations
import re
import sys
from typing import Optional, Dict, List, Tuple


# ============================================================
# 1. 完整短语映射 (优先匹配, 整句替换)
#    WHY: 短语级匹配避免"机油"→"oil machine"的错位翻译
# ============================================================
PHRASES: List[Tuple[str, str]] = [
    # === 滤清器类型 (Filter Type) - 对应 sakurafilter.com Filter Type 字段 ===
    ("机油滤清器", "Oil Filter"),
    ("柴油滤清器", "Fuel Filter"),
    ("燃油滤清器", "Fuel Filter"),
    ("汽油滤清器", "Fuel Filter"),
    ("空气滤清器", "Air Filter"),
    ("空调滤清器", "Cabin Air Filter"),
    ("空调滤", "Cabin Air Filter"),
    ("机油格", "Oil Filter"),
    ("柴油格", "Fuel Filter"),
    ("空气格", "Air Filter"),
    ("液压油滤清器", "Hydraulic Oil Filter"),
    ("液压油滤芯", "Hydraulic Filter Element"),
    ("液压滤芯", "Hydraulic Filter Element"),
    ("水滤清器", "Water Filter"),
    ("冷却液滤清器", "Coolant Filter"),
    ("尿素滤清器", "Urea Filter"),
    ("油气分离器", "Oil-Gas Separator"),
    ("油水分离器", "Oil-Water Separator"),
    ("燃油水分离器", "Fuel Water Separator"),
    ("柴油水分离", "Diesel Water Separation"),

    # === 字段标题 (Section Titles) - 对应 AdminProductFormView 分区 ===
    ("基础信息", "Basic Information"),
    ("尺寸规格", "Dimensions"),
    ("性能参数", "Performance Specifications"),
    ("包装信息", "Packaging"),
    ("替代 OEM", "OEM Cross-Reference"),
    ("适配车型", "Machine Applications"),
    ("图片画廊", "Image Gallery"),
    ("规格参数", "Specifications"),
    ("产品名称", "Product Name"),
    ("产品编号", "Part Number"),
    ("产品类型", "Filter Type"),
    ("产品描述", "Description"),
    ("备注信息", "Remarks"),

    # === 过滤性能术语 ===
    ("过滤效率", "Filtration Efficiency"),
    ("过滤精度", "Filtration Rating"),
    ("过滤比", "Beta Ratio"),
    ("β值", "Beta Ratio"),
    ("绝对精度", "Absolute Rating"),
    ("公称精度", "Nominal Rating"),
    ("微米等级", "Micron Rating"),
    ("过滤介质", "Filter Media"),
    ("滤材", "Filter Media"),
    ("过滤面积", "Filter Area"),
    ("纳污容量", "Dirt-Holding Capacity"),
    ("容尘量", "Dirt-Holding Capacity"),
    ("初始压降", "Initial Pressure Drop"),
    ("压差", "Differential Pressure"),
    ("压降", "Pressure Drop"),
    ("最大压差", "Max Differential Pressure"),
    ("最大工作压力", "Max Working Pressure"),
    ("最大允许压差", "Max Allowable Differential Pressure"),
    ("爆破压力", "Burst Pressure"),
    ("耐压", "Pressure Resistance"),
    ("破裂压力", "Collapse Pressure"),
    ("抗压强度", "Collapse Pressure Rating"),
    ("额定流量", "Rated Flow"),
    ("公称流量", "Nominal Flow"),
    ("旁通阀", "Bypass Valve"),
    ("旁通压力", "Bypass Pressure"),
    ("旁通阀开启压力", "Bypass Valve Opening Pressure"),
    ("单向阀", "Check Valve"),
    ("止回阀", "Check Valve"),
    ("防回流阀", "Anti-Drainback Valve"),
    ("防干烧阀", "Anti-Drainback Valve"),
    ("密封材料", "Seal Material"),
    ("密封圈", "Seal / O-Ring"),
    ("工作温度", "Operating Temperature"),
    ("温度范围", "Temperature Range"),
    ("适用温度", "Applicable Temperature"),
    ("使用寿命", "Service Life"),
    ("更换周期", "Replacement Interval"),
    ("保养周期", "Maintenance Interval"),

    # === 过滤介质类型 ===
    ("纸质滤芯", "Paper Element"),
    ("玻纤滤芯", "Glass Fiber Element"),
    ("玻璃纤维", "Glass Fiber"),
    ("合成纤维", "Synthetic Fiber"),
    ("不锈钢网", "Stainless Steel Mesh"),
    ("金属网", "Metal Mesh"),
    ("活性炭", "Activated Carbon"),
    ("活性炭滤", "Activated Carbon"),
    ("纤维素", "Cellulose"),
    ("无纺布", "Non-Woven Fabric"),
    ("聚酯纤维", "Polyester Fiber"),
    ("熔喷", "Melt-Blown"),
    ("PP 棉", "PP Cotton"),
    ("PP棉", "PP Cotton"),

    # === 尺寸字段 (D1-D8, H1-H4) ===
    # 简写优先, 完整描述仅在 PHRASES 未命中时由 TERMS 兜底
    ("外径 OD", "Outer Diameter"),
    ("内径 ID", "Inner Diameter"),
    ("总高 OAL", "Overall Height"),
    ("高度", "Height"),
    ("长度", "Length"),
    ("宽度", "Width"),
    ("螺纹", "Thread"),
    ("接口螺纹", "Connection Thread"),
    ("接口尺寸", "Connection Size"),
    ("顶帽外径", "End Cap OD"),
    ("顶帽内径", "End Cap ID"),
    ("骨架外径", "Center Tube OD"),
    ("骨架内径", "Center Tube ID"),

    # === 包装字段 ===
    ("装箱数量", "Qty per Carton"),
    ("装箱数", "Qty per Carton"),
    ("每箱数量", "Qty per Carton"),
    ("每箱件数", "Pieces per Carton"),
    ("单件重量", "Unit Weight"),
    ("净重", "Net Weight"),
    ("毛重", "Gross Weight"),
    ("箱体长度", "Carton Length"),
    ("箱体宽度", "Carton Width"),
    ("箱体高度", "Carton Height"),
    ("包装体积", "Package Volume"),
    ("单箱体积", "Carton Volume"),
    ("母箱数量", "Master Box Qty"),
    ("母箱重量", "Master Box Weight"),
    ("母箱长度", "Master Box Length"),
    ("母箱宽度", "Master Box Width"),
    ("母箱高度", "Master Box Height"),
    ("母箱体积", "Master Box Volume"),

    # === 业务操作 (ElMessage 高频) ===
    ("已保存", "Saved"),
    ("已创建", "Created"),
    ("已删除", "Deleted"),
    ("已更新", "Updated"),
    ("已停售", "Discontinued"),
    ("已发布", "Published"),
    ("已停用", "Deactivated"),
    ("已启用", "Activated"),
    ("保存成功", "Save succeeded"),
    ("保存失败", "Save failed"),
    ("删除成功", "Delete succeeded"),
    ("删除失败", "Delete failed"),
    ("创建成功", "Create succeeded"),
    ("创建失败", "Create failed"),
    ("更新成功", "Update succeeded"),
    ("更新失败", "Update failed"),
    ("加载失败", "Load failed"),
    ("加载中", "Loading"),
    ("加载成功", "Load succeeded"),
    ("导入成功", "Import succeeded"),
    ("导入失败", "Import failed"),
    ("导出成功", "Export succeeded"),
    ("导出失败", "Export failed"),
    ("上传成功", "Upload succeeded"),
    ("上传失败", "Upload failed"),
    ("确认删除", "Confirm Delete"),
    ("确认操作", "Confirm Action"),
    ("操作成功", "Operation succeeded"),
    ("操作失败", "Operation failed"),

    # === 业务校验提示 ===
    ("产品已存在", "Product already exists"),
    ("产品不存在", "Product not found"),
    ("数据已被修改", "Data has been modified"),
    ("数据已被其他管理员修改", "Data has been modified by another admin"),
    ("请检查 OEM 号", "Please check the OEM number"),
    ("请先登录", "Please login first"),
    ("请输入", "Please enter"),
    ("请选择", "Please select"),
    ("请上传", "Please upload"),
    ("不能为空", "cannot be empty"),
    ("格式错误", "Invalid format"),
    ("超出范围", "Out of range"),
    ("请刷新后重试", "Please refresh and retry"),
    ("系统繁忙", "System busy"),
    ("网络异常", "Network error"),
    ("服务器异常", "Server error"),
    ("权限不足", "Insufficient permissions"),
    ("禁止操作", "Operation forbidden"),

    # === OEM 交叉引用 ===
    ("OEM 编号", "OEM Number"),
    ("OEM 号", "OEM Number"),
    ("原厂编号", "OEM Part Number"),
    ("替代品牌", "Alternative Brand"),
    ("替代型号", "Alternative Part Number"),
    ("替代件", "Replacement"),
    ("互换件", "Interchange"),
    ("等效件", "Equivalent"),
    ("兼容件", "Compatible"),
    ("交叉引用", "Cross-Reference"),
    ("交叉参考", "Cross-Reference"),

    # === 机器/引擎/车型应用 ===
    ("机器品牌", "Machine Brand"),
    ("机器型号", "Machine Model"),
    ("机器名称", "Machine Name"),
    ("发动机品牌", "Engine Brand"),
    ("发动机型号", "Engine Model"),
    ("发动机类型", "Engine Type"),
    ("适用车型", "Applicable Vehicle"),
    ("适配发动机", "Applicable Engine"),
    ("整车厂", "Vehicle Manufacturer"),
    ("制造商", "Manufacturer"),
    ("生产年份", "Year"),
    ("排量", "Displacement"),
    ("燃油类型", "Fuel Type"),
    ("汽油", "Gasoline"),
    ("柴油", "Diesel"),
    ("电动", "Electric"),
    ("混合动力", "Hybrid"),

    # === ETL 相关 ===
    ("触发成功", "Trigger succeeded"),
    ("已触发", "Triggered"),
    ("正在执行", "Running"),
    ("执行完成", "Completed"),
    ("执行失败", "Failed"),
    ("用户主动取消", "User cancelled"),
    ("管理员强制取消", "Admin override"),
    ("任务超时", "Task timeout"),
    ("系统关闭", "System shutdown"),
    ("其他原因", "Other reason"),
    ("已暂停", "Paused"),
    ("已恢复", "Resumed"),
    ("干运行", "Dry Run"),
    ("试运行", "Dry Run"),
    ("模拟运行", "Dry Run"),
    ("校验完成", "Validation completed"),
    ("正在处理", "Processing"),
    ("处理成功", "Processed successfully"),
    ("处理失败", "Processing failed"),
    ("回滚完成", "Rollback completed"),
    ("导入中", "Importing"),
    ("导入完成", "Import completed"),
    ("全量加载", "Full Load"),
    ("增量更新", "Incremental Update"),
    ("仅插入", "Insert Only"),
    ("更新或插入", "Upsert"),

    # === 字典/管理 ===
    ("新增产品", "Add Product"),
    ("编辑产品", "Edit Product"),
    ("产品列表", "Product List"),
    ("产品详情", "Product Details"),
    ("产品名称 1", "Product Name 1"),
    ("产品名称 2", "Product Name 2"),
    ("类型", "Type"),
    ("MR.1", "MR.1"),
    ("OEM 2", "OEM 2"),
    ("是否发布", "Publish Status"),
    ("公开", "Published"),
    ("隐藏", "Hidden"),
    ("已下架", "Delisted"),
    ("停售", "Discontinued"),
    ("在售", "On Sale"),
    ("停产", "Discontinued"),
    ("草稿", "Draft"),
    ("待审核", "Pending Review"),
    ("已通过", "Approved"),
    ("已拒绝", "Rejected"),

    # === 通用 UI ===
    ("搜索", "Search"),
    ("重置", "Reset"),
    ("清空", "Clear"),
    ("取消", "Cancel"),
    ("确认", "Confirm"),
    ("提交", "Submit"),
    ("返回", "Back"),
    ("关闭", "Close"),
    ("打开", "Open"),
    ("查看", "View"),
    ("详情", "Details"),
    ("编辑", "Edit"),
    ("删除", "Delete"),
    ("添加", "Add"),
    ("新增", "Add"),
    ("移除", "Remove"),
    ("清空重试", "Clear and Retry"),
    ("刷新", "Refresh"),
    ("刷新页面", "Refresh Page"),
    ("导出", "Export"),
    ("导入", "Import"),
    ("批量删除", "Batch Delete"),
    ("批量导入", "Batch Import"),
    ("全选", "Select All"),
    ("反选", "Invert Selection"),
    ("上一页", "Previous"),
    ("下一页", "Next"),
    ("首页", "First"),
    ("末页", "Last"),
    ("跳转", "Go"),
    ("每页", "Per Page"),
    ("条", "items"),
    ("第", "Page"),
    ("页", ""),
    ("共", "Total"),
    ("总计", "Total"),
    ("暂无数据", "No data"),
    ("暂无结果", "No results"),
    ("未找到", "Not found"),
    ("未找到匹配结果", "No matching results"),
    ("加载中,请稍候", "Loading, please wait"),
    ("正在加载", "Loading"),
    ("处理中", "Processing"),

    # === 错误页 ===
    ("页面加载失败", "Page load failed"),
    ("系统遇到了意外错误", "An unexpected error occurred"),
    ("复制错误", "Copy Error"),
    ("复制成功", "Copied"),
    ("查看技术详情", "View technical details"),
    ("时间", "Timestamp"),
    ("请联系管理员", "Please contact administrator"),
    ("跳到主内容", "Skip to main content"),

    # === 主题/导航 ===
    ("主题切换", "Toggle Theme"),
    ("浅色", "Light"),
    ("深色", "Dark"),
    ("切换到浅色", "Switch to Light"),
    ("切换到深色", "Switch to Dark"),
    ("产品搜索", "Product Search"),
    ("OEM 查询", "OEM Lookup"),
    ("产品管理", "Product Management"),
    ("字典管理", "Dictionary Management"),
    ("用户管理", "User Management"),
    ("ETL 触发", "ETL Trigger"),
    ("产品对比", "Product Comparison"),
    ("性能", "Performance"),
    ("帮助", "Help"),
    ("进入后台", "Enter Admin"),
    ("退出后台", "Exit Admin"),
    ("退出登录", "Logout"),
    ("修改密码", "Change Password"),
    ("登录成功", "Login successful"),
    ("登录失败", "Login failed"),
    ("用户名或密码错误", "Invalid username or password"),
    ("账号已被禁用", "Account disabled"),
    ("账号已锁定", "Account locked"),

    # === 表单验证 (高频) ===
    ("请输入用户名", "Please enter username"),
    ("请输入密码", "Please enter password"),
    ("请输入", "Please enter"),
    ("请选择文件", "Please select a file"),
    ("文件过大", "File too large"),
    ("文件类型不支持", "File type not supported"),
    ("请填写完整", "Please complete all fields"),
    ("两次密码不一致", "Passwords do not match"),

    # === 字典管理 ===
    ("字典名称", "Dictionary Name"),
    ("字典类型", "Dictionary Type"),
    ("字典值", "Dictionary Value"),
    ("排序", "Sort Order"),
    ("排序号", "Sort Index"),
    ("状态", "Status"),
    ("创建时间", "Created At"),
    ("更新时间", "Updated At"),
    ("创建人", "Created By"),
    ("更新人", "Updated By"),
    ("操作", "Action"),
    ("操作日志", "Action Log"),
    ("批量操作", "Batch Operation"),

    # === 产品状态字段 (Day 11+ P2.2) ===
    ("已停售", "Discontinued"),
    ("未发布", "Unpublished"),
    ("已下架", "Delisted"),

    # === admin 视图标题 ===
    ("产品名称", "Product Name"),
    ("产品编号", "Part Number"),
    ("品牌", "Brand"),
    ("类型", "Type"),
    ("状态", "Status"),
    ("操作", "Action"),
    ("图片", "Image"),
    ("缩略图", "Thumbnail"),
    ("大图", "Large Image"),
    ("上传", "Upload"),
    ("删除图片", "Delete Image"),
    ("Slot 非法", "Invalid Slot"),
    ("必须在 1-6 之间", "must be between 1-6"),
    ("上传成功", "Upload succeeded"),
    ("已删除", "Deleted"),
    ("Slot", "Slot"),
]


# ============================================================
# 2. 单术语映射 (短语匹配失败时使用, 按长度降序优先匹配长词)
#    WHY: 短词单独出现时, 需专业翻译 (如 "阀" → "Valve")
# ============================================================
TERMS: List[Tuple[str, str]] = [
    # 过滤技术
    ("滤清器", "Filter"),
    ("过滤器", "Filter"),
    ("滤芯", "Filter Element"),
    ("滤网", "Filter Screen / Strainer"),
    ("滤壳", "Filter Housing"),
    ("滤筒", "Filter Cartridge"),
    ("滤盘", "Filter Disc"),
    ("总成", "Assembly"),
    ("组件", "Component"),
    ("元件", "Element"),
    ("附件", "Accessory"),
    ("配件", "Accessory"),

    # 介质
    ("介质", "Media"),
    ("滤材", "Media"),
    ("滤纸", "Filter Paper"),
    ("玻纤", "Glass Fiber"),
    ("无纺", "Non-Woven"),
    ("活性炭", "Activated Carbon"),

    # 性能
    ("效率", "Efficiency"),
    ("精度", "Rating"),
    ("压差", "Differential Pressure"),
    ("压力", "Pressure"),
    ("流量", "Flow"),
    ("压降", "Pressure Drop"),
    ("耐压", "Pressure Resistance"),
    ("爆破", "Burst"),
    ("破裂", "Collapse"),
    ("纳污", "Dirt-Holding"),
    ("容尘", "Dirt-Holding"),
    ("旁通", "Bypass"),
    ("回油", "Return"),
    ("吸油", "Suction"),
    ("高压", "High Pressure"),
    ("低压", "Low Pressure"),
    ("中压", "Medium Pressure"),

    # 阀门
    ("旁通阀", "Bypass Valve"),
    ("单向阀", "Check Valve"),
    ("止回阀", "Check Valve"),
    ("防回流阀", "Anti-Drainback Valve"),
    ("溢流阀", "Relief Valve"),
    ("安全阀", "Safety Valve"),
    ("电磁阀", "Solenoid Valve"),
    ("节流阀", "Throttle Valve"),
    ("平衡阀", "Balance Valve"),
    ("换向阀", "Directional Valve"),

    # 密封
    ("密封", "Seal"),
    ("密封圈", "O-Ring"),
    ("密封件", "Sealing"),
    ("橡胶", "Rubber"),
    ("氟橡胶", "Fluororubber / Viton"),
    ("丁腈橡胶", "Nitrile Rubber / NBR"),
    ("硅胶", "Silicone"),
    ("金属", "Metal"),
    ("不锈钢", "Stainless Steel"),
    ("塑料", "Plastic"),
    ("尼龙", "Nylon"),
    ("聚四氟乙烯", "PTFE / Teflon"),

    # 尺寸
    ("外径", "OD"),
    ("内径", "ID"),
    ("总高", "OAL"),
    ("高度", "H"),
    ("长度", "L"),
    ("宽度", "W"),
    ("厚度", "Thickness"),
    ("直径", "Diameter"),
    ("半径", "Radius"),
    ("螺纹", "Thread"),
    ("接头", "Fitting"),
    ("接口", "Port"),
    ("进出", "In/Out"),
    ("入口", "Inlet"),
    ("出口", "Outlet"),

    # 包装
    ("包装", "Packaging"),
    ("纸箱", "Carton"),
    ("木箱", "Wooden Case"),
    ("托盘", "Pallet"),
    ("母箱", "Master Carton"),
    ("内盒", "Inner Box"),
    ("外箱", "Outer Carton"),
    ("体积", "Volume"),
    ("重量", "Weight"),
    ("净重", "Net Weight"),
    ("毛重", "Gross Weight"),

    # 业务
    ("产品", "Product"),
    ("产品名", "Product Name"),
    ("产品号", "Part Number"),
    ("产品编号", "Part Number"),
    ("产品类型", "Filter Type"),
    ("机器", "Machine"),
    ("机型", "Machine Model"),
    ("车型", "Vehicle Model"),
    ("发动机", "Engine"),
    ("引擎", "Engine"),
    ("品牌", "Brand"),
    ("厂商", "Manufacturer"),
    ("制造商", "Manufacturer"),
    ("供应商", "Supplier"),
    ("代理", "Distributor"),
    ("型号", "Model"),
    ("年份", "Year"),
    ("排量", "Displacement"),
    ("功率", "Power"),
    ("扭矩", "Torque"),
    ("转速", "RPM"),
    ("燃油", "Fuel"),
    ("汽油", "Gasoline"),
    ("柴油", "Diesel"),
    ("机油", "Engine Oil"),
    ("润滑油", "Lubricant"),
    ("液压油", "Hydraulic Oil"),
    ("冷却液", "Coolant"),
    ("水", "Water"),
    ("空气", "Air"),
    ("燃气", "Gas"),
    ("天然气", "Natural Gas"),
    ("LNG", "LNG"),
    ("CNG", "CNG"),

    # OEM
    ("原厂", "OEM"),
    ("副厂", "Aftermarket"),
    ("替代", "Replacement"),
    ("互换", "Interchange"),
    ("等效", "Equivalent"),
    ("兼容", "Compatible"),
    ("通用", "Universal"),
    ("原配", "Original Fit"),
    ("适配", "Fit"),
    ("应用", "Application"),

    # 状态
    ("停售", "Discontinued"),
    ("停产", "Discontinued"),
    ("下架", "Delisted"),
    ("在售", "On Sale"),
    ("上架", "Listed"),
    ("发布", "Publish"),
    ("草稿", "Draft"),
    ("待审", "Pending"),
    ("通过", "Approved"),
    ("拒绝", "Rejected"),
    ("有效", "Active"),
    ("无效", "Inactive"),
    ("启用", "Enable"),
    ("停用", "Disable"),
    ("激活", "Activate"),
    ("锁定", "Locked"),
    ("解锁", "Unlocked"),
    ("已选", "Selected"),
    ("未选", "Unselected"),
    ("默认", "Default"),
    ("自定义", "Custom"),
    ("全部", "All"),
    ("部分", "Partial"),
    ("空", "Empty"),
    ("满", "Full"),

    # 操作
    ("查询", "Query"),
    ("搜索", "Search"),
    ("筛选", "Filter"),
    ("排序", "Sort"),
    ("导入", "Import"),
    ("导出", "Export"),
    ("上传", "Upload"),
    ("下载", "Download"),
    ("保存", "Save"),
    ("取消", "Cancel"),
    ("提交", "Submit"),
    ("重置", "Reset"),
    ("清空", "Clear"),
    ("刷新", "Refresh"),
    ("加载", "Load"),
    ("处理", "Process"),
    ("执行", "Execute"),
    ("运行", "Run"),
    ("停止", "Stop"),
    ("暂停", "Pause"),
    ("恢复", "Resume"),
    ("完成", "Complete"),
    ("成功", "Succeeded"),
    ("失败", "Failed"),
    ("错误", "Error"),
    ("警告", "Warning"),
    ("提示", "Hint"),
    ("确认", "Confirm"),
    ("删除", "Delete"),
    ("移除", "Remove"),
    ("编辑", "Edit"),
    ("修改", "Modify"),
    ("更新", "Update"),
    ("新增", "Add"),
    ("添加", "Append"),
    ("插入", "Insert"),
    ("追加", "Append"),
    ("查看", "View"),
    ("详情", "Detail"),
    ("列表", "List"),
    ("对比", "Compare"),
    ("比较", "Compare"),
    ("预览", "Preview"),
    ("打印", "Print"),

    # 常用形容词/副词
    ("当前", "Current"),
    ("最近", "Recent"),
    ("最新", "Latest"),
    ("最早", "Earliest"),
    ("更多", "More"),
    ("较少", "Less"),
    ("至少", "At least"),
    ("最多", "At most"),
    ("精确", "Precise"),
    ("宽松", "Loose"),
    ("推荐", "Recommended"),
    ("常见", "Common"),
    ("标准", "Standard"),
    ("特殊", "Special"),
    ("可选", "Optional"),
    ("必填", "Required"),
    ("只读", "Read-only"),
    ("禁用", "Disabled"),
    ("只读", "Read-Only"),
    ("可见", "Visible"),
    ("隐藏", "Hidden"),
    ("展开", "Expand"),
    ("收起", "Collapse"),
    ("打开", "Open"),
    ("关闭", "Close"),
    ("存在", "Exists"),
    ("不存在", "Not found"),
    ("有效", "Valid"),
    ("无效", "Invalid"),
    ("异常", "Exception"),
    ("正常", "Normal"),
    ("立即", "Immediately"),
    ("稍后", "Later"),
    ("自动", "Auto"),
    ("手动", "Manual"),
    ("强制", "Force"),
    ("可选", "Optional"),

    # 常用动词/连接词 (补充)
    ("请", "Please"),
    ("先", "first"),
    ("再", "then"),
    ("也", "also"),
    ("并", "and"),
    ("或", "or"),
    ("但", "but"),
    ("在", "in"),
    ("到", "to"),
    ("从", "from"),
    ("为", "for"),
    ("与", "with"),
    ("中", ""),
    ("将", "will"),
    ("已", ""),
    ("被", "by"),
    ("的", ""),
    ("了", ""),
    ("仅", "Only"),
    ("例", "e.g."),
    ("后台", "background"),
    ("前台", "frontend"),
    ("执行", "Execute"),
    ("测试", "Test"),
    ("用例", "case"),
    ("示例", "Example"),
    ("样本", "Sample"),
    ("调试", "Debug"),
    ("日志", "Log"),
    ("信息", "Info"),
    ("追踪", "Trace"),
    ("性能", "Performance"),
    ("压力", "Pressure"),
    ("负载", "Load"),
    ("并发", "Concurrency"),
    ("同步", "Sync"),
    ("异步", "Async"),
    ("队列", "Queue"),
    ("缓存", "Cache"),
    ("索引", "Index"),
    ("表", "Table"),
    ("视图", "View"),
    ("函数", "Function"),
    ("接口", "API"),
    ("服务", "Service"),
    ("中间件", "Middleware"),
    ("模块", "Module"),
    ("组件", "Component"),
    ("插件", "Plugin"),
    ("框架", "Framework"),
    ("平台", "Platform"),
    ("系统", "System"),
    ("应用", "Application"),
    ("程序", "Program"),
    ("脚本", "Script"),
    ("命令", "Command"),
    ("终端", "Terminal"),
    ("控制台", "Console"),
    ("浏览器", "Browser"),
    ("页面", "Page"),
    ("弹窗", "Dialog"),
    ("抽屉", "Drawer"),
    ("面板", "Panel"),
    ("按钮", "Button"),
    ("图标", "Icon"),
    ("菜单", "Menu"),
    ("标签", "Tab"),
    ("导航", "Nav"),
    ("侧栏", "Sidebar"),
    ("工具栏", "Toolbar"),
    ("状态栏", "Status Bar"),
    ("标题栏", "Title Bar"),
    ("登录", "Login"),
    ("注销", "Logout"),
    ("注册", "Register"),
    ("账号", "Account"),
    ("密码", "Password"),
    ("用户名", "Username"),
    ("手机", "Mobile"),
    ("邮箱", "Email"),
    ("验证码", "Verification Code"),
    ("记住", "Remember"),
    ("忘记", "Forgot"),

    # 数字/单位
    ("是", "Yes"),
    ("否", "No"),
    ("开", "On"),
    ("关", "Off"),
    ("毫米", "mm"),
    ("厘米", "cm"),
    ("米", "m"),
    ("英寸", "inch"),
    ("克", "g"),
    ("千克", "kg"),
    ("磅", "lb"),
    ("升", "L"),
    ("毫升", "mL"),
    ("立方米", "m³"),
    ("微米", "μm"),
    ("纳米", "nm"),
    ("巴", "bar"),
    ("兆帕", "MPa"),
    ("千帕", "kPa"),
    ("帕", "Pa"),
    ("磅每平方英寸", "psi"),
    ("摄氏度", "°C"),
    ("华氏度", "°F"),
    ("百分比", "%"),
    ("转速", "rpm"),

    # 数量
    ("数", "Count"),
    ("数量", "Qty"),
    ("件数", "Pieces"),
    ("量", "Count"),
    ("个", "pcs"),
    ("件", "pcs"),
    ("箱", "carton"),
    ("套", "set"),
    ("包", "pack"),
    ("批", "batch"),
    ("次", "times"),
    ("条", "items"),
    ("行", "rows"),
    ("列", "columns"),
    ("页", "pages"),

    # ==== UI 元素补充 ====
    ("基础", "Basic"),
    ("尺寸", "Dimensions"),
    ("加入", "Add"),
    ("仅看差异", "Show Diff Only"),
    ("左移", "Move Left"),
    ("右移", "Move Right"),
    ("上移", "Move Up"),
    ("下移", "Move Down"),
    ("引用", "Reference"),
    ("软删", "Soft delete"),
    ("拖动", "Drag"),
    ("生效", "take effect"),
    ("安全锁", "Safety Lock"),
    ("区别于", "Different from"),
    ("暂存", "Staging"),
    ("写库", "Write DB"),
    ("实体", "Entity"),
    ("模式", "Mode"),
    ("绝对路径", "Absolute path"),
    ("显示", "Show"),
    ("点击", "Click"),
    ("双击", "Double Click"),
    ("右击", "Right Click"),
    ("长按", "Long Press"),
    ("悬停", "Hover"),
    ("弹出", "Popup"),
    ("收起", "Collapse"),
    ("展开", "Expand"),
    ("切换", "Toggle"),
    ("选择", "Select"),
    ("取消选择", "Deselect"),
    ("选中", "Selected"),
    ("未选中", "Unselected"),
    ("全部展开", "Expand All"),
    ("全部收起", "Collapse All"),
    ("下载", "Download"),
    ("复制", "Copy"),
    ("粘贴", "Paste"),
    ("剪切", "Cut"),
    ("撤销", "Undo"),
    ("重做", "Redo"),
    ("放大", "Zoom In"),
    ("缩小", "Zoom Out"),
    ("旋转", "Rotate"),
    ("适应", "Fit"),
    ("原始", "Original"),
    ("自适应", "Auto Fit"),
    ("全屏", "Fullscreen"),
    ("退出全屏", "Exit Fullscreen"),
    ("最小化", "Minimize"),
    ("最大化", "Maximize"),
    ("还原", "Restore"),
    ("帮助", "Help"),
    ("关于", "About"),
    ("设置", "Settings"),
    ("首选项", "Preferences"),
    ("选项", "Options"),
    ("配置", "Config"),
    ("参数", "Parameters"),
    ("属性", "Properties"),
    ("详情", "Details"),
    ("描述", "Description"),
    ("简介", "Summary"),
    ("标签", "Tag"),
    ("分类", "Category"),
    ("分组", "Group"),
    ("排序", "Sort"),
    ("筛选", "Filter"),
    ("分页", "Pagination"),
    ("跳页", "Jump to page"),
    ("首页", "First Page"),
    ("末页", "Last Page"),
    ("上一页", "Previous Page"),
    ("下一页", "Next Page"),
    ("每页", "Per Page"),
    ("总数", "Total"),
    ("总条数", "Total"),
    ("当前页", "Current Page"),
    ("加载", "Load"),
    ("重新加载", "Reload"),
    ("更多", "More"),
    ("加载更多", "Load More"),
    ("没有更多", "No More"),
    ("数据", "Data"),
    ("数据集", "Dataset"),
    ("数据库", "Database"),
    ("表", "Table"),
    ("列", "Column"),
    ("字段", "Field"),
    ("记录", "Record"),
    ("行", "Row"),
    ("空", "Empty"),
    ("空白", "Blank"),
    ("缺失", "Missing"),
    ("正确", "Correct"),
    ("错误", "Wrong"),
    ("失败", "Failed"),
    ("成功", "Succeeded"),
    ("通过", "Pass"),
    ("未通过", "Fail"),
    ("跳过", "Skip"),
    ("等待", "Wait"),
    ("等待中", "Waiting"),
    ("处理中", "Processing"),
    ("运行中", "Running"),
    ("已完成", "Completed"),
    ("未开始", "Not Started"),
    ("已取消", "Cancelled"),
    ("已暂停", "Paused"),
    ("异常", "Exception"),
    ("告警", "Alert"),
    ("通知", "Notification"),
    ("消息", "Message"),
    ("确认", "Confirm"),
    ("提示", "Hint"),
    ("说明", "Note"),
    ("注释", "Comment"),
    ("备注", "Remark"),
    ("版本", "Version"),
    ("修订", "Revision"),
    ("更新", "Update"),
    ("升级", "Upgrade"),
    ("降级", "Downgrade"),
    ("回滚", "Rollback"),
    ("发布", "Release"),
    ("部署", "Deploy"),
    ("安装", "Install"),
    ("卸载", "Uninstall"),
    ("启动", "Start"),
    ("停止", "Stop"),
    ("重启", "Restart"),
    ("关闭", "Shutdown"),
    ("启用", "Enable"),
    ("禁用", "Disable"),
    ("激活", "Activate"),
    ("冻结", "Freeze"),
    ("解锁", "Unlock"),
    ("锁定", "Lock"),
    ("已锁", "Locked"),
    ("未锁", "Unlocked"),
    ("锁定", "Lock"),
    ("保护", "Protect"),
    ("已保护", "Protected"),
    ("公开", "Public"),
    ("私有", "Private"),
    ("共享", "Share"),
    ("可见", "Visible"),
    ("不可见", "Invisible"),
    ("显示", "Show"),
    ("隐藏", "Hide"),
    ("已隐藏", "Hidden"),
    ("未隐藏", "Visible"),
    ("默认", "Default"),
    ("自定义", "Custom"),
    ("预设", "Preset"),
    ("模板", "Template"),
    ("示例", "Example"),
    ("样例", "Sample"),
    ("实例", "Instance"),
    ("实例化", "Instantiate"),
    ("链接", "Link"),
    ("跳转", "Navigate"),
    ("前往", "Go to"),
    ("返回", "Back"),
    ("前进", "Forward"),
    ("向上", "Up"),
    ("向下", "Down"),
    ("向左", "Left"),
    ("向右", "Right"),
    ("顶部", "Top"),
    ("底部", "Bottom"),
    ("中间", "Middle"),
    ("内部", "Inner"),
    ("外部", "Outer"),
    ("正面", "Front"),
    ("背面", "Back"),
    ("正面", "Front"),
    ("左", "Left"),
    ("右", "Right"),
    ("前", "Front"),
    ("后", "Back"),

    # 时间
    ("时间", "Time"),
    ("日期", "Date"),
    ("秒", "s"),
    ("分钟", "min"),
    ("小时", "h"),
    ("天", "days"),
    ("周", "weeks"),
    ("月", "months"),
    ("年", "years"),
    ("开始", "Start"),
    ("结束", "End"),
    ("起始", "From"),
    ("截止", "To"),
    ("之前", "Before"),
    ("之后", "After"),

    # 通用名词
    ("信息", "Information"),
    ("详情", "Details"),
    ("描述", "Description"),
    ("备注", "Remarks"),
    ("说明", "Notes"),
    ("名称", "Name"),
    ("编号", "Number"),
    ("代码", "Code"),
    ("标识", "ID"),
    ("属性", "Attribute"),
    ("值", "Value"),
    ("键", "Key"),
    ("类型", "Type"),
    ("分类", "Category"),
    ("等级", "Class"),
    ("级别", "Level"),
    ("阶段", "Stage"),
    ("步骤", "Step"),
    ("流程", "Process"),
    ("规则", "Rule"),
    ("策略", "Policy"),
    ("方案", "Plan"),
    ("配置", "Config"),
    ("设置", "Settings"),
    ("选项", "Options"),
    ("参数", "Parameter"),
    ("字段", "Field"),
    ("表单", "Form"),
    ("表格", "Table"),
    ("记录", "Record"),
    ("数据", "Data"),
    ("文件", "File"),
    ("文档", "Document"),
    ("图片", "Image"),
    ("照片", "Photo"),
    ("附件", "Attachment"),
    ("版本", "Version"),
    ("日志", "Log"),
    ("消息", "Message"),
    ("通知", "Notification"),
    ("告警", "Alert"),
    ("事件", "Event"),
    ("任务", "Task"),
    ("作业", "Job"),
    ("进度", "Progress"),
    ("状态", "Status"),
    ("结果", "Result"),
    ("输出", "Output"),
    ("输入", "Input"),
    ("用户", "User"),
    ("管理员", "Admin"),
    ("访客", "Guest"),
    ("游客", "Visitor"),
    ("客服", "Support"),
    ("服务", "Service"),
    ("支持", "Support"),
    ("联系", "Contact"),
    ("关于", "About"),
    ("首页", "Home"),
    ("上一步", "Previous"),
    ("下一步", "Next"),
    ("返回", "Back"),
    ("前进", "Forward"),
    ("顶部", "Top"),
    ("底部", "Bottom"),
    ("左侧", "Left"),
    ("右侧", "Right"),
    ("中间", "Middle"),
    ("外部", "External"),
    ("内部", "Internal"),
    ("权限", "Permission"),
    ("角色", "Role"),
    ("认证", "Auth"),
    ("授权", "Authorize"),
    ("登录", "Login"),
    ("注销", "Logout"),
    ("会话", "Session"),
    ("令牌", "Token"),
    ("密钥", "Key"),
    ("证书", "Certificate"),
    ("签名", "Signature"),
    ("加密", "Encrypt"),
    ("解密", "Decrypt"),
    ("压缩", "Compress"),
    ("解压", "Extract"),
    ("备份", "Backup"),
    ("恢复", "Restore"),
    ("迁移", "Migrate"),
    ("部署", "Deploy"),
    ("升级", "Upgrade"),
    ("降级", "Downgrade"),
    ("回滚", "Rollback"),
    ("监控", "Monitor"),
    ("告警", "Alert"),
    ("审计", "Audit"),
    ("统计", "Statistics"),
    ("分析", "Analysis"),
    ("报表", "Report"),
    ("图表", "Chart"),
    ("曲线", "Curve"),
    ("趋势", "Trend"),
    ("对比", "Compare"),
    ("基准", "Baseline"),
    ("目标", "Target"),
    ("阈值", "Threshold"),
    ("限制", "Limit"),
    ("容量", "Capacity"),
    ("比例", "Ratio"),
    ("倍率", "Multiplier"),
    ("系数", "Coefficient"),
    ("误差", "Error"),
    ("精度", "Precision"),
    ("准确度", "Accuracy"),
    ("偏差", "Deviation"),
    ("方差", "Variance"),
    ("平均", "Average"),
    ("最大", "Max"),
    ("最小", "Min"),
    ("总计", "Total"),
    ("累计", "Cumulative"),
]


# ============================================================
# 3. 词典驱动的翻译函数
# ============================================================

def _build_dict(pairs: List[Tuple[str, str]]) -> Dict[str, str]:
    """构建 zh -> en 字典, 后定义覆盖前定义 (短词优先 base)"""
    d: Dict[str, str] = {}
    for zh, en in pairs:
        d[zh] = en
    return d


_PHRASE_DICT = _build_dict(PHRASES)
# 按长度降序, 优先匹配长词
_TERM_PAIRS_SORTED = sorted(TERMS, key=lambda x: -len(x[0]))


def translate_zh_to_en(text: str) -> Optional[str]:
    """
    将中文字符串翻译为英文.
    返回 None 表示词典未命中, 调用方应回退到 "[EN] {text}" 占位.

    翻译策略:
      1. 完整短语精确匹配 → 直接返回
      2. 短语级 substring 替换 (按长度降序, 短语之间自动加空格)
      3. 剩余中文按 TERM_PAIRS 兜底翻译
      4. 全部未命中 → 返回 None
    """
    if not text or not text.strip():
        return None

    text = text.strip()

    # 策略 1: 完整短语精确匹配
    if text in _PHRASE_DICT:
        return _PHRASE_DICT[text]

    # 策略 2: 多短语组合 - 按长度降序 substring 替换
    # 关键: 短语边界处插入空格, 避免 "OilFilter" 这种粘连
    result = ""
    i = 0
    replaced_any = False
    sorted_phrases = sorted(_PHRASE_DICT.items(), key=lambda x: -len(x[0]))
    while i < len(text):
        matched = False
        for zh, en in sorted_phrases:
            if text.startswith(zh, i):
                # 边界处理: 前面是中文/后面是中文, 加空格
                need_left = result and not result[-1].isspace() and (
                    "\u4e00" <= result[-1] <= "\u9fff"
                    or result[-1] in ")]}>"
                )
                # 检查右侧下一个字符 (如果是中文/英文, 后续翻译可能拼接)
                next_char = text[i + len(zh)] if i + len(zh) < len(text) else ""
                need_right = next_char and (
                    "\u4e00" <= next_char <= "\u9fff"
                    or (next_char.isalpha() and next_char.isascii())
                )
                if need_left:
                    result += " "
                result += en
                if need_right:
                    result += " "
                i += len(zh)
                matched = True
                replaced_any = True
                break
        if not matched:
            result += text[i]
            i += 1

    # 策略 3: 剩余中文段按 TERM_PAIRS 兜底
    # 关键: 替换 TERM 时, 检查左右相邻字符, 必要时插入空格
    def _fill_chinese_segments(s: str) -> Tuple[str, bool]:
        # 在单个连续中文段中, 反复找最长 TERM 匹配, 替换并维护边界空格
        def _translate_segment(seg: str) -> Tuple[str, bool]:
            if not seg:
                return seg, False
            out: List[str] = []
            i = 0
            replaced = False
            while i < len(seg):
                matched = False
                for zh, en in _TERM_PAIRS_SORTED:
                    if seg.startswith(zh, i):
                        # 左边界: 前一个字符是英文字母 → 加空格
                        if out and out[-1] and out[-1][-1].isalpha() and out[-1][-1].isascii():
                            out.append(" ")
                        out.append(en)
                        # 右边界: 后一个字符是中文 → 加空格
                        if i + len(zh) < len(seg) and "\u4e00" <= seg[i + len(zh)] <= "\u9fff":
                            out.append(" ")
                        i += len(zh)
                        matched = True
                        replaced = True
                        break
                if not matched:
                    out.append(seg[i])
                    i += 1
            return "".join(out), replaced

        out: List[str] = []
        i = 0
        any_replaced = False
        while i < len(s):
            if "\u4e00" <= s[i] <= "\u9fff":
                j = i
                while j < len(s) and "\u4e00" <= s[j] <= "\u9fff":
                    j += 1
                seg = s[i:j]
                translated, changed = _translate_segment(seg)
                if changed:
                    any_replaced = True
                out.append(translated)
                i = j
            else:
                out.append(s[i])
                i += 1
        return "".join(out), any_replaced

    final_result, term_replaced = _fill_chinese_segments(result)
    result = final_result
    if term_replaced:
        replaced_any = True

    if not replaced_any:
        return None

    # 清理: 合并多余空格
    result = re.sub(r"\s+", " ", result).strip()
    # 中文标点 → 英文标点
    result = result.replace("，", ", ").replace("。", ". ")
    result = result.replace("：", ": ").replace("；", "; ")
    result = re.sub(r"\s+([,.;:?!])", r"\1", result)
    result = result.replace("（", "(").replace("）", ")")
    # 收尾: 删除英文标点前的多余空格
    result = re.sub(r"\s+([,.;:?!\)])", r"\1", result)
    return result


# ============================================================
# 4. 单元自测
# ============================================================
if __name__ == "__main__":
    test_cases = [
        # 完整短语精确匹配
        ("机油滤清器", "Oil Filter"),
        ("机油滤清器交叉引用", "Oil Filter Cross-Reference"),
        ("已保存", "Saved"),
        ("产品已存在", "Product already exists"),
        # 中文 + 英文/标点
        ("OEM 2 (必填)", "OEM 2 (Required)"),
        # 短术语
        ("外径", "OD"),
        ("内径", "ID"),
        ("总高", "OAL"),
        ("高压", "High Pressure"),
        # 多短语拼接 (验证空格)
        ("高压滤芯", "High Pressure Filter Element"),
        # 短语
        ("防回流阀", "Anti-Drainback Valve"),
        # 全英文输入, 不应翻译
        ("Hydraulic Filter Cross-Reference", None),
        # 空格 + 中文
        ("产品名称 1", "Product Name 1"),
        # 纯数字+英文
        ("Slot 1-6", None),
        # 空
        ("", None),
        # ElMessage 风格
        ("Slot 非法", "Invalid Slot"),
        ("必须在 1-6 之间", "must be between 1-6"),
        # 多短语 + 标点 + 术语
        ("已触发 ETL, 后台执行中", "Triggered ETL, background Execute"),
        # 自然语句
        ("请先保存产品再上传图片", "Please first Save Product then Upload Image"),
        # 复合术语 (部分命中)
        ("测试用例不存在", "Test case Not found"),
    ]
    print("=== 词典自测 ===")
    pass_n = 0
    fail_n = 0
    for zh, expected in test_cases:
        got = translate_zh_to_en(zh)
        ok = (got == expected) if expected is not None else (got is None or got == zh)
        if expected is None and got is not None and got != zh:
            ok = False
        marker = "✓" if ok else "✗"
        print(f"  {marker} {zh!r:42s} → {got!r:48s} (expected={expected!r})")
        if ok:
            pass_n += 1
        else:
            fail_n += 1
    print(f"\n  通过 {pass_n} / 失败 {fail_n} / 总 {len(test_cases)}")
    if fail_n > 0:
        sys.exit(1)
