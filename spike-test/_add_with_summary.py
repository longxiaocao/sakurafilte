"""
批次 6d: 为 Program.cs 中所有 minimal API 端点补 .WithSummary()
WHY: 当前 swagger summary 覆盖率 0%, 通过显式 WithSummary 提升到 30%+
"""
import re
from pathlib import Path

PROGRAM = Path(r"d:\projects\sakurafilter\backend\src\SakuraFilter.Api\Program.cs")

# 推断规则: 提取 path + WithName → 生成简洁中文 summary
SUMMARY_MAP = {
    # Health
    "HealthLive": "Liveness 探活 (进程是否存活, K8s/Docker 用)",
    "HealthReady": "Readiness 探活 (PG/Meili/BackgroundService 整体健康度)",
    # Perf
    "PerfSnapshot": "性能埋点快照 (P50/P95/P99, 最近 1000 条样本)",
    "Metrics": "Prometheus 兼容 /metrics 端点 (含 HTTP + 业务 + 进程指标)",
    "PerfAlerts": "性能告警列表 (按时间倒序, 运维面板用)",
    "PerfIngest": "接收前端性能埋点批量上报 (上限 100 条/批)",
    # Auth
    "AdminAuthStatus": "Auth Token 轮转状态查询 (current/previous 长度 + 轮转时间, 不暴露完整 token)",
    # Search
    "SearchProducts": "产品搜索 (走 ISearchProvider 抽象, Resilient 主备自动切换)",
    "SearchHealth": "搜索健康检查 (主备状态)",
    "GetProductByOem": "产品详情 (按 OEM 精确/规范化查询, 含 cross-references + machine-applications)",
    # ETL
    "EtlImport": "ETL 导入触发 (products/xrefs/apps, 统一入口, 路径白名单校验)",
    "EtlStatus": "ETL 导入进度查询 (实时 JSON, 含 current/total/elapsed/eta)",
    "EtlImportXrefs": "ETL 导入 xrefs (兼容旧入口, 新调用走 /api/etl/import + entityType)",
    "EtlImportApps": "ETL 导入 apps (兼容旧入口, 新调用走 /api/etl/import + entityType)",
    "EtlDryRun": "ETL dry-run 试解析 (不写库, 仅返回前 N 行解析结果 + 错误)",
    "EtlPause": "ETL 任务暂停 (Checkpoint, 可恢复)",
    "EtlResume": "ETL 任务恢复 (从 Checkpoint 继续)",
    "EtlCancel": "ETL 任务取消 (软取消, 留痕)",
    "EtlProgressSse": "ETL 进度 SSE 推送 (实时刷新, 替代轮询)",
    "EtlRecentErrors": "ETL 最近错误 (近 5min 缓冲区, 含上下文)",
    "EtlHealth": "ETL 服务健康度 (worker 心跳 + 队列长度)",
    # Dead-letter
    "GetDeadLetter": "死信队列分页查询 (keyset cursor, 支持 operation/since/recovery_count 过滤)",
    "RecoverDeadLetter": "死信单条恢复 (移回 pending + advisory lock 串行化)",
    "RecoverBatchDeadLetter": "死信批量恢复 (按条件, advisory lock 串行化)",
    # Progress (admin)
    "GetEtlProgress": "ETL 进度详情 (含 worker 心跳 + 错误码分布)",
    "GetProductHistory": "产品变更历史 (按 ID 倒序, 含 old/new JSON diff)",
    "GetProductHistoryCursor": "产品历史 cursor 翻页 (HMAC 签名防篡改)",
    # History
    "GetHistory": "产品历史明细 (按 reason_code + 变更类型过滤)",
    "GetHistoryCursor": "历史 cursor 翻页",
    # Auth (controllers)
    "AuthLogin": "管理员登录 (返回 access/refresh token + 用户信息)",
    "AuthRefresh": "刷新 token (旧 token 撤销 + 签发新 token, 一次性)",
    "AuthLogout": "登出 (撤销当前 refresh token)",
    "AuthMe": "当前登录用户信息 (含 role/email/lastLoginAt)",
    "ChangePassword": "修改密码 (旧密码校验, 强密码策略)",
    # PublicSearch
    "PublicSearchEightField": "公开 8 字段多框模糊搜索 (oemBrand/oemNo2/oemNo3 + machine 5 字段, AND 关系, 排除下架)",
    "PublicBatchOem": "公开批量 OEM 查询 (Excel 多行粘贴, 1-500 个, 自动 trim+去重)",
    # PublicProduct
    "GetProductBySlug": "公开产品详情 (按 slug: {name1}-{name2}-{oemBrand}-{oemNo}, 排除下架)",
    "ListProductsByType": "按 type 分组聚合产品 (前台首页/产品列表用)",
    # PublicCompare
    "PublicCompare": "公开产品对比 (上限 6 个, 排除下架, 保持传入顺序)",
    # PublicMachineBrands
    "PublicMachineBrands": "公开机型品牌聚合 (4 大类: Agriculture/Commercial/Construction/others, 去重 + sort_order)",
    # Admin Products
    "AdminListProducts": "后台产品列表 (支持搜索/筛选/排序/分页, 含 published/discontinued 状态)",
    "AdminGetProduct": "后台产品详情 (含全部字段, 不排除下架)",
    "AdminCreateProduct": "后台创建产品 (含 cross-references + machine-applications 嵌套创建)",
    "AdminUpdateProduct": "后台更新产品 (xmin 乐观锁, 409 冲突)",
    "AdminDeleteProduct": "后台软删除产品 (is_discontinued=true, 保留历史)",
    "AdminRestoreProduct": "后台恢复已下架产品 (is_discontinued=false)",
    "AdminCompareProducts": "后台产品对比 (admin 用, 不排除下架, 上限 6 个)",
    "AdminBatchDelete": "后台批量删除 (admin 用, 软删除)",
    "AdminBatchUpdateStatus": "后台批量更新发布/下架状态 (admin 用)",
    "AdminSearchProducts": "后台产品搜索 (admin 用, 含下架, 8 字段)",
    "AdminListTrash": "后台回收站 (列出已下架产品)",
    "AdminListPending": "后台待发布列表 (is_published=false)",
    # Admin Dict
    "ListEngines": "字典列表 - 发动机品牌 (支持 q/includeDeleted/limit)",
    "CreateEngine": "字典创建 - 发动机品牌",
    "UpdateEngine": "字典更新 - 发动机品牌",
    "DeleteEngine": "字典软删除 - 发动机品牌",
    "RestoreEngine": "字典恢复 - 发动机品牌 (从回收站)",
    "ReorderEngines": "字典重排序 - 发动机品牌 (拖拽排序)",
    "TypeaheadEngines": "字典 typeahead - 发动机品牌 (前端联想输入用)",
    "ListMachines": "字典列表 - 机器品牌",
    "CreateMachine": "字典创建 - 机器品牌",
    "UpdateMachine": "字典更新 - 机器品牌",
    "DeleteMachine": "字典软删除 - 机器品牌",
    "RestoreMachine": "字典恢复 - 机器品牌",
    "ReorderMachines": "字典重排序 - 机器品牌",
    "TypeaheadMachines": "字典 typeahead - 机器品牌",
    "ListMedias": "字典列表 - 过滤介质",
    "CreateMedia": "字典创建 - 过滤介质",
    "UpdateMedia": "字典更新 - 过滤介质",
    "DeleteMedia": "字典软删除 - 过滤介质",
    "RestoreMedia": "字典恢复 - 过滤介质",
    "ReorderMedias": "字典重排序 - 过滤介质",
    "TypeaheadMedias": "字典 typeahead - 过滤介质",
    "ListOemBrands": "字典列表 - OEM 品牌",
    "CreateOemBrand": "字典创建 - OEM 品牌",
    "UpdateOemBrand": "字典更新 - OEM 品牌",
    "DeleteOemBrand": "字典软删除 - OEM 品牌",
    "RestoreOemBrand": "字典恢复 - OEM 品牌",
    "ReorderOemBrands": "字典重排序 - OEM 品牌",
    "TypeaheadOemBrands": "字典 typeahead - OEM 品牌",
    "ListOemNo3s": "字典列表 - OEM 编号3",
    "CreateOemNo3": "字典创建 - OEM 编号3",
    "UpdateOemNo3": "字典更新 - OEM 编号3",
    "DeleteOemNo3": "字典软删除 - OEM 编号3",
    "RestoreOemNo3": "字典恢复 - OEM 编号3",
    "ReorderOemNo3s": "字典重排序 - OEM 编号3",
    "TypeaheadOemNo3s": "字典 typeahead - OEM 编号3",
    "ListProductName1s": "字典列表 - 产品名1",
    "CreateProductName1": "字典创建 - 产品名1",
    "UpdateProductName1": "字典更新 - 产品名1",
    "DeleteProductName1": "字典软删除 - 产品名1",
    "RestoreProductName1": "字典恢复 - 产品名1",
    "ReorderProductName1s": "字典重排序 - 产品名1",
    "TypeaheadProductName1s": "字典 typeahead - 产品名1",
    "ListProductName2s": "字典列表 - 产品名2",
    "CreateProductName2": "字典创建 - 产品名2",
    "UpdateProductName2": "字典更新 - 产品名2",
    "DeleteProductName2": "字典软删除 - 产品名2",
    "RestoreProductName2": "字典恢复 - 产品名2",
    "ReorderProductName2s": "字典重排序 - 产品名2",
    "TypeaheadProductName2s": "字典 typeahead - 产品名2",
    "ListTypes": "字典列表 - 滤清器类型",
    "CreateType": "字典创建 - 滤清器类型",
    "UpdateType": "字典更新 - 滤清器类型",
    "DeleteType": "字典软删除 - 滤清器类型",
    "RestoreType": "字典恢复 - 滤清器类型",
    "ReorderTypes": "字典重排序 - 滤清器类型",
    "TypeaheadTypes": "字典 typeahead - 滤清器类型",
    "GetDictSchema": "字典元数据 (含 8 类字典的 schema 定义, 前端表单生成用)",
    # Users
    "ListUsers": "用户列表 (admin 角色, 支持搜索/筛选/分页)",
    "GetUser": "用户详情 (admin 角色)",
    "CreateUser": "创建用户 (admin 角色, 强密码策略)",
    "UpdateUser": "更新用户 (admin 角色, role/email/fullName/isActive)",
    "DeleteUser": "软删除用户 (admin 角色, 不能删除最后一个 admin)",
    "ResetPassword": "重置用户密码 (admin 角色, 不需旧密码)",
    "GetUserLoginLog": "用户登录审计日志 (分页, admin 角色)",
    # Images
    "UploadImage": "上传产品图片 (MinIO/OSS, 自动缩略图 + WebP)",
    "DeleteImage": "删除产品图片",
    # Index replay
    "IndexReplay": "触发搜索索引重放 (手动, 通常 worker 自动跑)",
    # General
    "ApiInfo": "API 根信息 (name/version/status)",
    "ApiScalar": "Scalar API 文档 (开发环境)",
    "ApiSwagger": "Swagger UI 文档 (开发环境)",
}


def main():
    src = PROGRAM.read_text(encoding="utf-8")

    # 找所有 .WithName("X") 调用, 在前面插入 .WithSummary(...)
    # 模式: .WithName("Name")  →  .WithSummary("...").WithName("Name")
    pattern = re.compile(r'\.WithName\("([A-Za-z0-9_]+)"\)')
    hits = 0
    misses = []

    def repl(m):
        nonlocal hits
        name = m.group(1)
        summary = SUMMARY_MAP.get(name)
        if summary is None:
            misses.append(name)
            return m.group(0)
        hits += 1
        # 转义引号
        esc = summary.replace('"', '\\"')
        return f'.WithSummary("{esc}").WithName("{name}")'

    new_src = pattern.sub(repl, src)

    if new_src != src:
        PROGRAM.write_text(new_src, encoding="utf-8")
        print(f"[OK] 已为 {hits} 个端点添加 WithSummary")
    else:
        print("[!] 文件无变化")

    if misses:
        print(f"[INFO] 未配置的 WithName ({len(misses)}): {sorted(set(misses))[:20]}")


if __name__ == "__main__":
    main()
