# 架构决策记录 (ADR)

本文件记录项目中关键技术选型、已排除方案及原因。每条决策格式固定:
```
#<编号> <决策标题> (<日期>)
决策: <选型结论>
理由: <为什么选择该方案>
排除方案:
  - <方案A>: <排除原因>
关联文件: <影响的核心文件列表>
```

---

#1 SSE 401 修复方案选择 (2026-07-18)
决策: 前端改用 fetch + ReadableStream 替代 EventSource, 不改后端
理由: EventSource API 不支持自定义 Header, 无法携带 JWT。fetch + ReadableStream 可携带 Authorization Bearer, 与现有 axios 拦截器逻辑一致 (复用 buildAuthHeaders), 无需后端改动
排除方案:
  - 后端 SSE 支持 query token (?token=xxx): token 会泄漏到访问日志/Referer/nginx 日志, 安全风险高
  - 后端 SSE 支持 cookie auth: 需后端改动 + 与 JWT 无状态架构冲突, 改动面大
关联文件:
  - frontend/src/composables/useEtlProgress.ts
  - frontend/src/utils/http.ts (新增 buildAuthHeaders 导出)
  - backend/src/SakuraFilter.Api/Endpoints/AdminEtlEndpoints.cs (未改动)
