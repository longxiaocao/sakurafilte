# P7.1 X-Admin-Token 自动轮转工具 — 验收规格

> 目标: 把"手改 appsettings.json + 滚动重启"的人工流程, 原子化为一条命令
> 日期: 2026-07-03
> 优先级: 高 (生产部署阻塞运维效率)

---

## 1. 背景

### 1.1 现状 (Day 9.9 已实现)
- `DevTokenAuthMiddleware` 支持双 key 模式 (`Auth:DevStaticToken` + `Auth:DevStaticTokenPrevious`)
- `CursorHmac` 已实现同款双 key 轮换模式 (可参考实现)
- 轮转人工流程: ops 改 appsettings.json → 滚动重启多实例 → 等前端刷新 → 再次改 config 清空 PreviousKey → 再次滚动重启
- **痛点**: 4 步人工操作, 期间任何一个实例重启顺序错都会导致 401 风暴

### 1.2 目标
- 一条命令完成轮转: `SakuraFilter.Cli rotate-token --new <token> [--old <token>] [--dry-run]`
- 多实例配置原子化 (同一时刻所有实例用同一份 config, 不会 A 旧 B 新的混乱窗口)
- dry-run 预览不实际写入
- 多实例感知 (一台实例触发轮转 → 其他实例通过 PG NOTIFY/LISTEN 收到通知 → 自动重载 config)

---

## 2. 架构决策

### 2.1 项目布局
- 独立 console project: `backend/src/SakuraFilter.Cli/`
  - `SakuraFilter.Cli.csproj` (.NET 8 console, OutputType=Exe)
  - 引用 `SakuraFilter.Infrastructure` (PG 连接) 和 `SakuraFilter.Api` (appsettings.json 路径)
  - 入口: `Program.cs` (System.CommandLine 解析参数) + 子命令类

### 2.2 协议: 原子双 key 切换
- **轮转前**: `CurrentKey = A`, `PreviousKey = ""` (单 key 模式)
- **轮转中 (瞬时)**: `CurrentKey = B`, `PreviousKey = A` (双 key 模式, 旧 token 仍可用)
- **过渡期结束**: `CurrentKey = B`, `PreviousKey = ""` (单 key 模式, 旧 token 失效)

**原子保证**: 一次 commit 同时改 2 个 key, 不存在"Current=B 但 Previous 还是旧值"的中间态

### 2.3 配置文件载体
- 主源: `appsettings.json` (被 .NET IConfiguration 读取)
- 备份: `appsettings.json.bak.<timestamp>` (轮转前自动备份, 出错可回滚)
- **不引入新配置中心** (YAGNI, 保持与现有架构一致)

### 2.4 多实例同步策略
- **配置同步** (推): CLI 改完 appsettings.json → 通过 PG NOTIFY `auth_token_rotated` 广播事件
- **配置加载** (拉): 每个 API 实例启动时 LISTEN `auth_token_rotated` 通道
  - 收到 NOTIFY → 重新加载 IConfiguration (重读 appsettings.json)
  - **或**: SIGHUP 信号触发 IConfigurationRoot.Reload()
- **降级**: 单实例部署或 PG 不可用时, NOTIFY 失败不影响 CLI 主流程 (仅记录 warning, ops 手动滚动重启)

### 2.5 决策记录
| 备选方案 | 选择 | 理由 |
|---|---|---|
| 独立 CLI vs API 端点 | **CLI** | 可在 ops 机器上跑, 不需要 API 在线; 单点登录/审计独立 |
| 配置中心 (etcd/Consul) | **不动 appsettings.json** | YAGNI; 现有架构依赖文件配置 |
| 滚动重启 vs 通知重载 | **两者都支持, 通知优先** | 通知更快 (毫秒级); 重启作 fallback |
| 立即清 PreviousKey vs 保留 | **保留, 由 --purge 显式触发** | 防误操作导致旧 token 客户端 401 |

---

## 3. CLI 接口设计

### 3.1 子命令
```
SakuraFilter.Cli rotate-token [options]

选项:
  --new <token>      (必填) 新的 CurrentKey, ≥ 32 字符
  --old <token>      (可选) 显式指定旧 token, 默认从配置文件读 CurrentKey
  --config <path>    (可选) 配置文件路径, 默认 backend/src/SakuraFilter.Api/appsettings.json
  --purge            (可选) 轮转后立即清空 PreviousKey (单 key 模式), 默认 false
  --dry-run          (可选) 预览变更, 不实际写入
  --notify           (可选) 轮转完成后通过 PG NOTIFY 广播, 默认 true
  --no-reload        (可选) 跳过 PG NOTIFY, 仅改 config (供 CI/批量部署用)

示例:
  # 干跑: 预览不写入
  SakuraFilter.Cli rotate-token --new "new-token-32chars-..." --dry-run

  # 正常轮转: 新 token 入库 + 旧 token 留 7 天
  SakuraFilter.Cli rotate-token --new "new-token-32chars-..."

  # 紧急轮转: 旧 token 立即失效 (慎用, 客户端会立即 401)
  SakuraFilter.Cli rotate-token --new "new-token-32chars-..." --purge
```

### 3.2 退出码
- `0`: 成功 (包括 dry-run)
- `1`: 参数错误 (token 长度 < 32, --new 与 --old 相同, 文件不存在)
- `2`: 配置写入失败 (IO error, 权限)
- `3`: NOTIFY 广播失败 (PG 不可达) — 不影响 config 写入, 仅 warning
- `4`: 实例 reload 失败 (PG NOTIFY 发了但 0 实例响应) — 仅 warning

### 3.3 输出 (stdout, JSON 格式, 便于脚本解析)
```json
{
  "dryRun": false,
  "configPath": "backend/src/SakuraFilter.Api/appsettings.json",
  "previous": {
    "currentKey": "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C",
    "previousKey": ""
  },
  "next": {
    "currentKey": "new-token-32chars-...",
    "previousKey": "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
  },
  "backupPath": "appsettings.json.bak.20260703-225308",
  "notify": {
    "sent": true,
    "channel": "auth_token_rotated",
    "respondedInstances": 2
  },
  "warnings": []
}
```

---

## 4. 实施清单 (P7.1.1 → P7.1.5)

### P7.1.1 项目骨架 (1 文件)
- `backend/src/SakuraFilter.Cli/SakuraFilter.Cli.csproj`
  - `<OutputType>Exe</OutputType>`
  - `<TargetFramework>net8.0</TargetFramework>`
  - 引用 `SakuraFilter.Infrastructure` (PG 连接)
  - 引用 `System.CommandLine` (参数解析)
  - NuGet: `Npgsql` (8.x)

### P7.1.2 配置读写 (2 文件)
- `backend/src/SakuraFilter.Cli/Services/ConfigFileService.cs`
  - `LoadConfig(path) → JObject`
  - `SaveConfig(path, JObject, backup=true) → string backupPath`
  - `ValidateToken(token) → bool` (≥ 32 字符)
- `backend/src/SakuraFilter.Cli/Models/TokenRotationPlan.cs`
  - record: `(string OldKey, string NewKey, string? BackupPath, bool Purge)`

### P7.1.3 PG NOTIFY 广播 (1 文件)
- `backend/src/SakuraFilter.Cli/Services/TokenRotationNotifier.cs`
  - 注入 `NpgsqlDataSource` (复用 API 的 NpgsqlDataSource, 共享连接池)
  - `NotifyRotatedAsync(plan) → int respondedInstances`
  - 协议: NOTIFY auth_token_rotated, '{"oldKey":"...","newKey":"...","purge":false}'
  - **响应实例数**: 暂定为"在线 PG LISTEN 数" (查 pg_stat_activity), 实际响应需后续 API 端 confirm

### P7.1.4 CLI 入口 (1 文件)
- `backend/src/SakuraFilter.Cli/Program.cs`
  - System.CommandLine 解析 --new/--old/--config/--purge/--dry-run/--notify/--no-reload
  - 调用 ConfigFileService + TokenRotationNotifier
  - 输出 JSON 结果
  - 异常处理: 抛 InvalidOperationException (参数错) / IOException (IO 错)

### P7.1.5 API 端感知 (1 文件, 增量)
- `backend/src/SakuraFilter.Api/Services/TokenRotationListener.cs` (新文件, 不修改现有中间件)
  - `IHostedService`, 启动时 `LISTEN auth_token_rotated`
  - 收到 NOTIFY → 重新构造 `IConfigurationRoot` (通过 reloadOnChange=true 自动生效)
  - **不修改 DevTokenAuthMiddleware** (中间件从 IConfiguration 读, 配置变更自动生效)

### P7.1.6 E2E 验证脚本 (1 文件)
- `spike-test/_test_p71_token_rotation.py`
  - 场景 1: dry-run 预览不写入 (对比 appsettings.json mtime)
  - 场景 2: 正常轮转 (--new, 不 --purge) → 旧 token 仍可用, 新 token 可用
  - 场景 3: 立即清空 (--purge) → 旧 token 失效, 新 token 可用
  - 场景 4: 错误处理 (--new 长度 < 32 退出 1, --new == --old 退出 1)
  - 场景 5: 多实例感知 (起 2 个 API 实例, 跑 CLI, 验证 2 实例 log 都收到 reload 事件)
  - 场景 6: 备份恢复 (CLI 改坏后用 .bak 还原)

---

## 5. 验收标准

### 5.1 功能验收 (E2E 6 场景全过)
- [ ] dry-run 不写文件 (mtime 不变)
- [ ] 正常轮转 旧 token 仍可用 + 新 token 可用
- [ ] --purge 旧 token 立即 401 + 新 token 可用
- [ ] --new 长度 < 32 退出码 1
- [ ] --new == --old 退出码 1
- [ ] 2 实例都能 reload (log 出现 "config reloaded" 关键字)

### 5.2 安全验收
- [ ] 新 token 长度强制 ≥ 32 字符 (CLI 端 + API 端双校验)
- [ ] 旧 token 不会出现在 stdout 日志 (仅显示前 8 字符 + "...")
- [ ] 备份文件权限 0600 (仅当前用户可读, Linux/Mac)
- [ ] CLI 不打印完整 token 到 stderr

### 5.3 性能验收
- [ ] CLI 启动 + 读 config + 写 config + NOTIFY 完整流程 < 2s
- [ ] 单次 NOTIFY 广播 PG RTT < 100ms (局域网)

### 5.4 兼容性验收
- [ ] 现有 _test_day911_previous_key.py 测试 仍能通过 (不破坏双 key 模式)
- [ ] 现有 CI 拆分 3 job 仍能通过 (e2e.yml 不需要改)
- [ ] 不引入新的 NuGet 依赖 (除 System.CommandLine)

---

## 6. 不在范围内 (Out of Scope)
- 自动生成强 token (`generate-token` 子命令) — 留给 P7.2
- 多环境 (dev/staging/prod) 配置管理 — 留给 P9
- 集成到 Docker / k8s secrets — 留给 P9.1
- 历史轮转记录审计 — 留给 P7.3 (写 PG audit 表)
- Web UI 操作 — 留给后续

---

## 7. 风险评估

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| CLI 写文件过程中崩溃, config 半写 | 低 | 高 | 写前 .bak 备份, 写后校验 JSON 解析, 失败回滚 |
| NOTIFY 广播失败, 0 实例 reload | 中 | 中 | 不阻塞主流程, warning 输出, ops 手动 SIGHUP |
| 旧 token 客户端未刷新, --purge 误操作 | 中 | 高 | --purge 默认 false, 必须显式传; 提示 "客户端会立即 401" |
| 多实例部分 reload 成功部分失败 | 低 | 中 | API 端 reload 失败时记录 error, 仍用旧 config (降级, 不 500) |
| appsettings.json 在多副本部署中文件不同步 | 中 | 高 | Phase 2 改进: 写 PG audit 表 + 每实例启动对比; Phase 1 依赖 K8s/Volume 同步 |

---

## 8. 依赖关系

- **前置**: CI 拆分 3 job 全绿 (当前进行中, CI run #47 验证)
- **后置**: P7.2 (generate-token) / P7.3 (audit) / P9.1 (Docker secrets 集成)
- **并行**: P5.5 性能埋点 (无依赖)

---

## 9. 时间估算

- P7.1.1 项目骨架: 0.5h
- P7.1.2 配置读写: 1h
- P7.1.3 PG NOTIFY 广播: 1h
- P7.1.4 CLI 入口: 1h
- P7.1.5 API 端感知: 0.5h
- P7.1.6 E2E 验证: 1.5h
- **总计**: ~5.5h (1 个工作日)

---

## 10. 参考实现

- `backend/src/SakuraFilter.Api/Services/CursorHmac.cs` (Day 9.6 双 key 模式, 同款)
- `backend/src/SakuraFilter.Api/Services/DevTokenAuthMiddleware.cs` (双 key 验证)
- `spike-test/_test_day911_previous_key.py` (现有轮转测试, CLI 版扩展)
- `backend/src/SakuraFilter.Api/Services/EtlProgressBroadcaster.cs` (PG NOTIFY/LISTEN 实现参考)
