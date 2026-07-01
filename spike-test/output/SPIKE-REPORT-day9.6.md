# SPIKE-REPORT-day9.6 — 5 项后续建议自主执行

**日期**: 2026-07-01
**范围**: Day 9.5 后续建议 5 项 (1 项已含, 4 项新增)
**模式**: 自主决策 + 自主执行 (用户反馈"不要列建议,直接做")
**作者**: 协作完成 (Claude + Trae)

---

## 一、本次完成项 (5 项,1 已含 + 4 新增)

| # | 改进项 | 状态 | 关键文件 |
|---|--------|------|----------|
| 1 | EtlAlert payload 增加 reason_code | ✅ 已在 Day 9.5 包含 | `EtlAlertService.cs` |
| 2 | 前端取消任务枚举选择 UI | ✅ 新增 | `AdminEtlView.vue` |
| 3 | CI E2E 门禁 (E2E_REQUIRED master gate) | ✅ 新增 | `.github/workflows/ci.yml` |
| 4 | Cursor HMAC 双 key 轮转 (无侵入) | ✅ 新增 | `CursorHmac.cs` + `appsettings.json` |
| 5 | SSE 跨实例广播 (PG NOTIFY/LISTEN, 零新依赖) | ✅ 新增 | `EtlProgressBroadcaster.cs` + `IEtlProgressBroadcaster.cs` + `EtlImportService.cs` + `Program.cs` |

---

## 二、第 2 项: 前端取消任务枚举选择 UI

### 2.1 设计动机
之前用 `ElMessageBox.prompt` 文本输入,用户可写任意字符串(如"用户取消" / "管理员取消" / "其他"),无法做运营审计聚合。

### 2.2 实现
`reasonCodeOptions` 5 个枚举(与后端 `AllowedReasonCodes` 对齐):
- USER_REQUEST (默认选中,蓝边框)
- ADMIN_OVERRIDE
- TIMEOUT
- SYSTEM_SHUTDOWN
- OTHER

UI 步骤:
1. 弹框显示 5 个 radio 选项,用户必须选一个 (蓝框 + default)
2. 弹框确认后,弹第二个 prompt 让用户补充更详细描述 (留空用默认)
3. 调用 `etlApi.cancel(reason, reasonCode)`,后端归一化后写库

### 2.3 修复
- vue-tsc 报 "Type 字面量不可赋值" (reason 推断为最严格类型) → 显式 `let reason: string = ...`
- ElMessageBox 不支持传 message + dangerouslyUseHTMLString 两次 (后写覆盖前写) → 只传一次 message

---

## 三、第 3 项: CI E2E 门禁 (E2E_REQUIRED 环境变量)

### 3.1 设计动机
之前 `python _test_day94.py || true` 永远 pass,失败也只是日志,无门禁意义。
但完全强制 gate 又会让 feature 分支数据集未 ready 时 PR 永久红。

### 3.2 分层 gate 策略
```yaml
env:
  E2E_REQUIRED: ${{ github.event_name == 'push' && github.ref == 'refs/heads/master' && 'true' || (github.event_name == 'workflow_dispatch' && 'true' || 'false') }}
```
- **master 分支 push**: E2E_REQUIRED=true,失败 exit 1,CI 红
- **workflow_dispatch** (手动触发): 同上
- **PR / feature 分支**: E2E_REQUIRED=false,失败仅记日志

### 3.3 串行跑多套件
```bash
for suite in _test_day95.py _test_day94.py; do
  if python "$suite"; then [PASS]; 
  else ALL_PASS=1; 
       if [ "$E2E_REQUIRED" = "true" ]; then exit $RC; fi; 
  fi; 
done
```

---

## 四、第 4 项: Cursor HMAC 双 key 轮转

### 4.1 设计动机
生产环境中单纯换 `Search:CursorHmacKey` 后:
- 所有旧的 cursor (分页中间状态) 验签失败 → 用户翻页报错
- 体验断崖,业务不可接受

### 4.2 双 key 轮转方案
- 启动加载 `CurrentKey` + `PreviousKey` (后者可选)
- **编码 cursor 始终用 CurrentKey** (避免新 cursor 走 PreviousKey,导致下次 CurrentKey 切换时再失效)
- **验签按 CurrentKey → PreviousKey 顺序尝试**,任一通过即可
- 轮转步骤:
  1. ops 配 `PreviousKey = 旧 key` + `CurrentKey = 新 key` → 部署
  2. 等 24h 旧 cursor 全部过期失效
  3. 清空 `PreviousKey` (减小验签开销)
- 副作用: 验签 O(n) (n=2,几乎无开销,16 字符 Base64URL HMAC 比较 < 1μs)

### 4.3 配置示例
```json
"Search": {
  "CursorHmacKey": "<新 key (≥32 字符)>",
  "CursorHmacKeyPrevious": "<旧 key (轮转期填,稳定后清空)>"
}
```

### 4.4 安全细节
- 用 `CryptographicOperations.FixedTimeEquals` 抗时序攻击
- `CurrentKey == PreviousKey` 时记录 warning 并忽略 previous
- 长度 < 32 字符抛 `InvalidOperationException`(启动失败,绝不静默)

---

## 五、第 5 项: SSE 跨实例广播 (PG NOTIFY/LISTEN)

### 5.1 设计动机
之前 SSE endpoint 1s 轮询本地 `GetActiveTaskInfo()`,**只对单实例有效**。
多实例部署时:
- A 实例触发 ETL → A 实例 SSE 客户端能收到
- B 实例 SSE 客户端看不到(它的 GetActiveTaskInfo 是 null)
- 运维监控混乱

### 5.2 架构选择
**用 PostgreSQL NOTIFY/LISTEN(零新依赖)**,不用 Redis:
- 项目软件分发要求完全离线 (user_profile 约束)
- PG 已在用,NOTIFY 是 PG 原生能力,0 新增基础设施
- 多实例 < 10 个时 PG NOTIFY 性能足够 (PG 内部用内存队列)

### 5.3 实现细节

#### 5.3.1 接口设计 (`IEtlProgressBroadcaster` in SakuraFilter.Etl)
```csharp
public interface IEtlProgressBroadcaster {
    Task InitAsync(CancellationToken ct = default);  // 启动 LISTEN 后台 task
    void Publish(string payload);                     // NOTIFY etl_progress
    IDisposable Subscribe(Func<string, Task> cb);    // 本地订阅
    bool IsListening { get; }                        // 是否连上 PG LISTEN
}
```

#### 5.3.2 PG NOTIFY 实现 (`EtlProgressBroadcaster` in SakuraFilter.Api)
- 后台 task 用持久 NpgsqlConnection 执行 `LISTEN etl_progress`
- 订阅 `Connection.Notification` 事件,转发给所有本地 `Subscribe` 回调
- `Publish` 走独立短连接 `NOTIFY etl_progress, '<payload>'` (失败静默)
- 重连策略: 异常 3s 后重连
- payload 限 8KB (PG NOTIFY 硬限制), 截断到 7900 字符 + 转义单引号

#### 5.3.3 EtlImportService 集成
- 构造函数加可选 `IEtlProgressBroadcaster? broadcaster = null`
- `TriggerAsync` 入口: 启动 500ms snapshot Timer
  - 每 500ms 拍一次 `Progress.ToJson()`,与上次对比,变化时 `broadcaster.Publish(json)`
  - TriggerAsync finally 块: 停止 Timer + 推终态帧
- broadcaster 为 null 时 (单元测试 / spike-test 脚本) 自动跳过

#### 5.3.4 SSE endpoint 改造
- 立即推本地首帧 (避免客户端等 broadcaster 第一帧)
- 订阅 broadcaster,收到消息时立即推给客户端
- 15s 心跳注释行 (避免代理/Nginx 60s 超时断开)
- `IsListening=false` 时降级为 15s 轮询本地 (单实例降级路径)
- 客户端断开时自动取消订阅 (`subscription.Dispose()`)

### 5.4 端到端验证
1. PG 端 `pg_stat_activity` 显示 `LISTEN etl_progress` 进程存在
2. SSE 端点响应 `text/event-stream` + 首帧 `data: {...}`
3. (多实例场景) 需 2 个 API 进程同时跑,跨实例 NOTIFY 转发

---

## 六、E2E 测试 (_test_day96.py, 6/6 PASS)

| # | 测试项 | 结果 |
|---|--------|------|
| 1 | EtlAlert reason_code 落库 (DB 列存在) | ✅ |
| 2 | 取消 reasonCode 归一化 (user_request→USER_REQUEST, HACK_VALUE→OTHER) | ✅ |
| 3 | CI 配置 E2E_REQUIRED gate (yaml 含关键串) | ✅ |
| 4 | Cursor HMAC 双 key baseline (current key 验签通过) | ✅ |
| 5 | SSE Broadcaster LISTEN (PG 端进程 + SSE 首帧) | ✅ |
| 6 | Cancel reason 持久化 (14 cancelled 中 10 带 reason_code) | ✅ |

执行命令: `python _test_day96.py`

---

## 七、文件变更清单

### 新增 (3 个)
- `backend/src/SakuraFilter.Etl/IEtlProgressBroadcaster.cs` (34 行)
- `backend/src/SakuraFilter.Api/Services/EtlProgressBroadcaster.cs` (162 行)
- `spike-test/_test_day96.py` (179 行)

### 修改 (6 个)
- `backend/src/SakuraFilter.Api/Services/CursorHmac.cs` (双 key 支持)
- `backend/src/SakuraFilter.Api/appsettings.json` (+CursorHmacKeyPrevious)
- `backend/src/SakuraFilter.Etl/EtlImportService.cs` (broadcaster 注入 + snapshot timer)
- `backend/src/SakuraFilter.Api/Program.cs` (DI 注册 + SSE 改造)
- `.github/workflows/ci.yml` (E2E_REQUIRED gate + 串行多套件)
- `frontend/src/views/admin/AdminEtlView.vue` (枚举下拉 UI)

### 文件级统计
```
新增: 3 文件, ~375 行
修改: 6 文件, ~250 行
```

---

## 八、部署清单 (生产环境)

1. **后端部署**:
   - 应用 Day 9.6 commit
   - 不需新依赖 (NuGet 0 新增)
   - 不需新基础设施 (用现有 PG)
   - PG `etl_progress_log.reason_code` 列已存在 (migration 017 Day 9.5 已应用)

2. **配置变更** (无, 全部后向兼容):
   - `appsettings.json` 的 `Search.CursorHmacKeyPrevious` 默认空字符串 (单 key 模式,与之前一致)
   - 后续轮转时再填

3. **前端部署**:
   - 重新 build `npm run build`
   - 不需后端协调

4. **CI 触发**:
   - 合并到 master → E2E_REQUIRED 自动 true → 跑 Day 9.5/9.6 套件,任一失败 fail
   - 手动触发 workflow_dispatch 也强制 gate
   - PR 默认 fail-soft (避免数据集未 ready 永久红)

---

## 九、改进建议 (后续可执行)

1. **Broadcaster 多实例压力测试**: 当前单实例跑通,生产多实例需验证 NOTIFY 风暴 (1M 行 ETL 500ms 拍一次 = 2000 NOTIFY/min) 时 PG 内存队列不会拥塞
2. **Cursor HMAC 轮转 dashboard**: 显示当前 key 创建时间 + 上次轮转时间,提醒 90 天自动轮转
3. **ETL 历史审计 UI**: 后台页面展示 reason_code 分布饼图 (USER_REQUEST 占比 vs ADMIN_OVERRIDE)
4. **PG NOTIFY payload 压缩**: 大 ETL (1M+ 行) snapshot 可能接近 8KB 上限,考虑 gzip 后 base64
5. **CI 增加 lighthouse 性能测试**: 配合 E2E_REQUIRED 门禁,把前端构建产物性能纳入 PR 检查
