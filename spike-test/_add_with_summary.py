"""P2: 批量为 minimal API 端点补 .WithSummary()"""
import re
import json
from pathlib import Path

ROOT = Path(r"d:\projects\sakurafilter\backend\src\SakuraFilter.Api\Endpoints")
ENDPOINTS = [
    "AdminAuthEndpoints.cs", "AdminDeadLetterEndpoints.cs", "AdminProductEndpoints.cs",
    "CommonEndpoints.cs", "DeadLetterEndpoints.cs", "DictionaryEndpoints.cs",
    "EtlEndpoints.cs", "ProductEndpoints.cs"
]

# 路由 → summary 映射 (基于代码注释 + 上下文)
SUMMARY_MAP = {
    # AdminDeadLetterEndpoints
    "GET /api/admin/dead-letter": "查询死信队列列表 (分页 + 筛选)",
    "POST /api/admin/dead-letter/recover-batch": "批量恢复符合条件的死信 (按 operation/lastError/recoveryCount 过滤)",
    "POST /api/admin/dead-letter/{id}/recover": "恢复单条死信到 pending 状态 (人工/自动重试入口)",
    # Common
    "GET /api/public/product/{slug}": "公开产品详情 (按 OEM 编号或 slug 查)",
    "GET /api/public/compare": "公开产品对比 (URL 传 ids 查询多个产品并排展示)",
    "GET /api/public/machine-brands": "公开机械品牌列表 (typeahead)",
    "GET /api/public/search": "公开 8 字段多框搜索 (支持 OEM/品牌/型号/机型多维组合)",
    "POST /api/public/search/batch-oem": "公开批量 OEM 查询 (Excel 多行粘贴, 单次最多 500 个)",
    # DeadLetter
    "GET /api/etl/dead-letter": "ETL 死信队列列表 (按操作类型/时间筛选)",
    "POST /api/etl/dead-letter/recover": "ETL 死信恢复入口 (需 X-Admin-Token)",
    "POST /api/etl/dead-letter/recover-batch": "ETL 死信批量恢复 (按条件批量重试)",
    # Etl
    "GET /api/etl/status": "ETL 当前状态 (running/idle/stopped + 进度)",
    "POST /api/etl/import": "ETL 触发导入 (XLSX 拖拽上传或 JSONL)",
    "POST /api/etl/pause": "ETL 暂停当前任务",
    "POST /api/etl/resume": "ETL 恢复已暂停任务",
    # Product
    "GET /api/products/{id}": "产品详情 (内部 API, 需认证)",
    # Health
    "GET /health/live": "Liveness 探针 (K8s livenessProbe)",
    "GET /health/ready": "Readiness 探针 (K8s readinessProbe, 含所有后台服务状态)",
    # Perf
    "GET /api/perf/snapshot": "性能指标快照 (P50/P95/P99/ErrorRate, 5s 自动刷新)",
    "GET /api/admin/perf/snapshot": "管理后台性能指标 (需 admin 角色)",
    # Users (auth)
    "POST /api/auth/login": "登录 (获取 JWT access + refresh token)",
    "POST /api/auth/refresh": "刷新 access token (一次性, 防重放)",
    "POST /api/auth/logout": "登出 (撤销当前 refresh token)",
    "GET /api/auth/me": "获取当前登录用户信息",
    "POST /api/auth/change-password": "修改当前用户密码 (需登录)",
    # Users admin
    "GET /api/admin/users": "用户列表 (分页 + 筛选)",
    "POST /api/admin/users": "创建用户 (admin 角色)",
    "PUT /api/admin/users/{id}": "更新用户信息 (角色/邮箱/激活状态)",
    "DELETE /api/admin/users/{id}": "删除用户 (软删除)",
    "POST /api/admin/users/{id}/restore": "恢复已删除用户",
    "POST /api/admin/users/{id}/reset-password": "重置用户密码 (admin 操作)",
}

# 扫描所有 endpoints 文件, 找出没有 WithSummary 的 MapXxx, 自动添加
added = 0
for fname in ENDPOINTS:
    fp = ROOT / fname
    if not fp.exists():
        print(f"  [SKIP] {fname} 不存在")
        continue
    txt = fp.read_text(encoding="utf-8")
    # 找 pattern: .MapXxx("...") - 检查下一行是否有 .WithSummary
    # 简化: 找 .MapGet/MapPost/MapPut/MapDelete("path", ...).WithName(...) 但没 .WithSummary
    pat = re.compile(r'(\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"[^)]*\)(?:\.WithName\("[^"]+"\))?)(?!\s*\.WithSummary)', re.MULTILINE)
    for m in pat.finditer(txt):
        method, path = m.group(2).upper(), m.group(3)
        key = f"{method} {path}"
        # 路径参数化: /api/admin/dead-letter/123 → /api/admin/dead-letter/{id}
        key_norm = re.sub(r'/\d+(?=[/"])', '/{id}', key)
        # 在 SUMMARY_MAP 找
        summary = SUMMARY_MAP.get(key) or SUMMARY_MAP.get(key_norm)
        if summary is None:
            # 启发式: 用 path 末段 + method
            tail = path.rstrip("/").split("/")[-1] or "resource"
            summary = f"{method} {tail}"
        new_line = f'{m.group(1).rstrip()}\n                .WithSummary("{summary}")'
        txt = txt[:m.end()] + "\n" + new_line[len(m.group(1).rstrip()):] + txt[m.end():]
        added += 1
    fp.write_text(txt, encoding="utf-8")

print(f"自动添加 {added} 个 .WithSummary (启发式 + 映射表)")

# 重新生成 openapi.json
import subprocess
result = subprocess.run(
    ["python", str(Path(r"d:\projects\sakurafilter\spike-test\_export_openapi.py"))],
    capture_output=True, text=True, encoding="utf-8"
)
print(f"openapi export: {result.returncode}")
print(result.stdout[-300:] if result.stdout else "")
