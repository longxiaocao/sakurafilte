# X-Admin-Token 零停机轮转 SOP

> 适用场景：生产环境 token 泄露/定期轮换/团队成员变更时，**不停止服务**完成 token 切换。

## 设计原理

- **双 Key 并存**：`CurrentKey`（新 token，主用）+ `PreviousKey`（旧 token，过渡期保留）
- **DB 持久化**：token 状态存 `auth_token_state` 表（id=1 单行），进程重启不丢
- **PG NOTIFY 实时广播**：A 实例 rotate → 写 DB + NOTIFY → B/C 实例 LISTEN 后秒级重载
- **过渡期监控**：`DevTokenAuthMiddleware` 对使用 `PreviousKey` 的请求记 WARN 日志，便于追踪切换进度

## 4 步零停机流程

### 步骤 1：配置新 token（appsettings.json）

在所有实例的 `appsettings.json` 中**同时配置**新旧 token：

```json
{
  "Auth": {
    "DevStaticToken": "<新 token>",
    "DevStaticTokenPrevious": "<旧 token>"
  }
}
```

> **WHY 同时配置**：滚动部署时旧实例仍在用旧 token，新实例已支持双 key，过渡期两端都能通过鉴权。

### 步骤 2：滚动部署

执行常规部署流程（`docker compose up -d --build` 或 K8s `kubectl rollout restart`）。

实例启动顺序：
1. 新实例启动 → 读 `appsettings.json` 双 key → DB 覆盖（若 DB 有更新的值）
2. `DevTokenAuthMiddleware` 同时接受新旧 token，`PreviousKey` 使用记 WARN 日志
3. 旧实例逐步停止

### 步骤 3：CLI 触发 DB 轮转（关键步骤）

所有新实例就绪后，在**任一实例**所在主机执行 CLI 命令，触发 DB 持久化 + NOTIFY 跨实例广播：

```bash
cd backend/src/SakuraFilter.Cli
dotnet run -- rotate-token \
  --new "<新 token>" \
  --old "<旧 token>" \
  --by "ops-$(whoami)@$(hostname)" \
  --pg-conn "Host=...;Database=...;Username=...;Password=..."
```

> **WHY 必须执行 CLI**：
> - `appsettings.json` 只影响本实例内存，跨实例需 DB + NOTIFY
> - CLI 写 `auth_token_state` 表 → NOTIFY `auth_token_rotated` → 所有实例 `AuthTokenStore.ReloadFromDbAsync`

CLI 输出示例：
```
[rotate-token] ✅ 成功
  DB 已更新 (auth_token_state.id=1)
  NOTIFY auth_token_rotated 已广播, 其他实例将自动重载
下一步:
  1. 验证新 token: curl -H 'X-Admin-Token: <新 token 前 8 位>...' <api>/api/admin/auth/status
  2. 观察 24h, 确认没有 'PreviousKey 使用' 告警
  3. 过渡期结束后, 清空 appsettings.json:Auth:DevStaticTokenPrevious
```

### 步骤 4：监控过渡期 + 清理

**监控**（持续 24 小时）：

```bash
# 查看是否有仍在使用旧 token 的请求
grep "PreviousKey" /var/log/sakurafilter/api.log | tail -20

# 查询当前 token 状态
curl -H "X-Admin-Token: <新 token>" http://localhost:5148/api/admin/auth/status
```

**清理**（确认无旧 token 流量后）：

修改所有实例的 `appsettings.json`，移除 `DevStaticTokenPrevious`：

```json
{
  "Auth": {
    "DevStaticToken": "<新 token>"
  }
}
```

再次滚动部署，完成轮转。

## 故障恢复

### 场景 1：CLI 执行失败（DB 不可达）

**现象**：CLI 报错 `NpgsqlException: Connection refused`

**处理**：
1. 检查 PG 连接串（`--pg-conn` 参数或 `ConnectionStrings__Postgres` 环境变量）
2. 修复 PG 后重试 CLI
3. 实例仍可用 `appsettings.json` 双 key 工作，不影响业务

### 场景 2：轮转后所有请求 401

**现象**：所有 API 请求返回 401，新旧 token 都无效

**根因**：`auth_token_state` 表中的 token 与 `appsettings.json` 不一致，且 `appsettings.json` 的 `DevStaticToken` 被错误修改

**处理**：
1. 用 DB 客户端查询：`SELECT current_key, previous_key FROM auth_token_state WHERE id=1;`
2. 用 `current_key` 作为 `X-Admin-Token` 调用 `/api/admin/auth/status` 验证
3. 若 DB 数据正确，修正 `appsettings.json` 的 `DevStaticToken` 为 `current_key`
4. 重启实例

### 场景 3：NOTIFY 广播失败（部分实例未更新）

**现象**：部分实例仍用旧 token，部分已切到新 token

**根因**：PG NOTIFY 未送达（实例 LISTEN 连接断开）

**处理**：
1. 查看实例日志：`[AuthTokenBroadcaster] LISTEN auth_token_rotated 已启动` 是否存在
2. 若 LISTEN 断开，`AuthTokenBroadcaster` 会 5s 后自动重连
3. 手动触发重载：重启实例，或调用 `/api/admin/auth/status`（启动时自动 ReloadFromDbAsync）

## 验证清单

轮转完成后，逐项验证：

- [ ] `curl -H "X-Admin-Token: <新 token>" http://<api>/api/admin/auth/status` 返回 200
- [ ] `curl -H "X-Admin-Token: <旧 token>" http://<api>/api/admin/auth/status` 返回 200（过渡期）
- [ ] `curl -H "X-Admin-Token: <错误 token>" http://<api>/api/admin/auth/status` 返回 401
- [ ] 业务前端/客户端已切换到新 token，请求正常
- [ ] 24h 后日志中无 `PreviousKey 使用` WARN
- [ ] `appsettings.json` 中 `DevStaticTokenPrevious` 已清理

## 相关代码

- `backend/src/SakuraFilter.Api/Services/AuthTokenStore.cs` - token DB 存储 + NOTIFY 广播
- `backend/src/SakuraFilter.Api/Services/AuthTokenBroadcaster.cs` - PG LISTEN 跨实例广播
- `backend/src/SakuraFilter.Api/Services/DevTokenAuthMiddleware.cs` - 双 key 鉴权 + PreviousKey WARN 日志
- `backend/src/SakuraFilter.Cli/Program.cs` - CLI rotate-token 命令实现
