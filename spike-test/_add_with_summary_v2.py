"""P2: 一次性添加所有缺 .WithSummary 的端点"""
import re
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\backend\src\SakuraFilter.Api\Endpoints")

# 找每个 MapXxx 后面到 .WithOpenApi() 或 .Produces() 之间的位置
# 直接在 .MapXxx(...).WithName(...) 后面插入 .WithSummary
def add_summary(txt: str, summary_map: dict) -> tuple[str, int]:
    """返回 (新文本, 添加数)"""
    # 单次扫描模式: MapXxx("path"...) .WithName("...") 或直接到 .WithOpenApi
    # 匹配: .MapXxx("path", delegate).WithName("...") 或 .MapXxx("path", delegate)
    # 不匹配: 已经有 .WithSummary 的
    pattern = re.compile(
        r'(\.Map(?:Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"[^)]*\)'
        r'(?:\.With(?:Name|OpenApi|Produces)[^;]*?)?)'
        r')(?=\s*\.(?:With(?:Name|OpenApi|Summary|Produces)|Map(?:Get|Post))|\s*;|\s*\})',
        re.DOTALL
    )
    out = []
    last = 0
    added = 0
    for m in pattern.finditer(txt):
        full_call, path = m.group(1), m.group(2)
        if ".WithSummary" in full_call:
            continue
        # 推断 method
        method = m.group(0).split('.')[1].split('(')[0].upper()
        key = f"{method} {path}"
        # 路径参数化
        key_norm = re.sub(r'/\d+(?=[/"])', '/{id}', key)
        summary = summary_map.get(key) or summary_map.get(key_norm)
        if summary is None:
            tail = path.rstrip("/").split("/")[-1] or "resource"
            summary = f"{method} {tail}"
        out.append(txt[last:m.end()])
        out.append(f'\n                .WithSummary("{summary}")')
        last = m.end()
        added += 1
    out.append(txt[last:])
    return "".join(out), added

# 调用
SUMMARY_MAP = {
    "GET /api/admin/dead-letter": "查询死信队列列表 (分页 + 筛选)",
    "POST /api/admin/dead-letter/recover-batch": "批量恢复符合条件的死信",
    "POST /api/admin/dead-letter/{id}/recover": "恢复单条死信到 pending",
    "GET /api/public/product/{slug}": "公开产品详情",
    "GET /api/public/compare": "公开产品对比",
    "GET /api/public/machine-brands": "公开机械品牌列表",
    "GET /api/public/search": "公开 8 字段多框搜索",
    "POST /api/public/search/batch-oem": "公开批量 OEM 查询 (最多 500 个)",
    "GET /api/etl/dead-letter": "ETL 死信队列列表",
    "POST /api/etl/dead-letter/recover": "ETL 死信恢复入口 (需 X-Admin-Token)",
    "POST /api/etl/dead-letter/recover-batch": "ETL 死信批量恢复",
    "GET /api/etl/status": "ETL 当前状态",
    "POST /api/etl/import": "ETL 触发导入",
    "POST /api/etl/pause": "ETL 暂停",
    "POST /api/etl/resume": "ETL 恢复",
    "GET /api/products/{id}": "产品详情 (内部)",
    "GET /health/live": "Liveness 探针",
    "GET /health/ready": "Readiness 探针",
    "GET /api/perf/snapshot": "性能指标快照",
    "GET /api/admin/perf/snapshot": "管理后台性能指标",
    "POST /api/auth/login": "登录 (获取 JWT)",
    "POST /api/auth/refresh": "刷新 access token",
    "POST /api/auth/logout": "登出",
    "GET /api/auth/me": "获取当前登录用户",
    "POST /api/auth/change-password": "修改密码",
    "GET /api/admin/users": "用户列表",
    "POST /api/admin/users": "创建用户",
    "PUT /api/admin/users/{id}": "更新用户",
    "DELETE /api/admin/users/{id}": "删除用户",
    "POST /api/admin/users/{id}/restore": "恢复已删除用户",
    "POST /api/admin/users/{id}/reset-password": "重置用户密码",
}

total_added = 0
for fname in ["AdminAuthEndpoints.cs", "AdminDeadLetterEndpoints.cs", "AdminProductEndpoints.cs",
              "CommonEndpoints.cs", "DeadLetterEndpoints.cs", "DictionaryEndpoints.cs",
              "EtlEndpoints.cs", "ProductEndpoints.cs"]:
    fp = ROOT / fname
    if not fp.exists():
        print(f"  [SKIP] {fname}")
        continue
    txt = fp.read_text(encoding="utf-8")
    new_txt, n = add_summary(txt, SUMMARY_MAP)
    if n > 0:
        fp.write_text(new_txt, encoding="utf-8")
        print(f"  {fname}: +{n} summary")
        total_added += n
print(f"\n总计添加: {total_added} 个 .WithSummary()")
