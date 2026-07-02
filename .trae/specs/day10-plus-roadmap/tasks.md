# Day 10+ Tasks (有序任务清单)

> 共 15 项任务,按 Phase 顺序编号。可对照 `spec.md` 看完整需求,看 `checklist.md` 看验证点。

---

## Phase 1: P0 地基 + P1 业务 (5 任务)

### Task 1: P0.1 全局 ILIKE ESCAPE 修复 (0.5 session)
- [x] SubTask 1.1: Grep `EF.Functions.ILike` 找所有 2 参调用位置
- [x] SubTask 1.2: 提取 `LikeEscapeExtensions.EscapeKeyword(string)` 帮助方法 (DRY)
- [x] SubTask 1.3: 替换所有 2 参为 3 参 `EF.Functions.ILike(col, $"%{escaped}%", "\\")`
- [x] SubTask 1.4: 加 E2E `_test_escape_underscore.py` 验证 `q=foo_bar` 不误命中
- [x] 验证: dotnet build 0 warning, E2E 1/1 全绿

### Task 2: P0.2 EF Core Migrations baseline 自动化 (0.5 session)
- [x] SubTask 2.1: 创建 `spike-test/_ef_migrations_baseline.py` 参数化脚本 (接收 `migration_id: list[str]`)
- [x] SubTask 2.2: `.github/workflows/e2e.yml` 加 `init-postgres` 步骤 (CI 全新 DB 跑 baseline)
- [x] SubTask 2.3: `scripts/db-baseline.sh` 一键执行 (本地开发用)
- [x] SubTask 2.4: 文档 `docs/ef-migrations-baseline.md` (使用流程 + 回滚)
- [x] 验证: 删 CI DB → 重跑 workflow → 全绿; 本地 baseline seed < 30s

### Task 3: P1.1 ETL 暂停恢复 (1.5 session)
- [x] SubTask 3.1: `etl_progress_log` 加 `checkpoint_id` 列 (EF Core Migration)
- [x] SubTask 3.2: `EtlImportService` 加 `PauseActiveTask` / `GetLastPausedCheckpointAsync` 方法 + `_pausedFlag` 标志
- [x] SubTask 3.3: Resume 时从 `lastCommittedBatchId` 续读, 跳过已 COMMIT 批次 (批次粒度 1000 行)
- [x] SubTask 3.4: API `POST /api/admin/etl/pause` + `POST /api/admin/etl/resume` (区别 Cancel, RateLimit 30/min)
- [x] SubTask 3.5: 前端 `AdminEtlView.vue` 加"暂停"/"恢复"按钮 + 状态显示
- [x] SubTask 3.6: E2E `_test_pause_resume.py` 验证 3 万行 xref 中途暂停/恢复, count reconciliation 一致 (delta=0)
- [x] 验证: 暂停/恢复 3 万行, 总行数 = 30,000, count reconciliation delta=0 (sub-100 阈值内)

### Task 4: P1.2 图片 CDN 切换 MinIO → Aliyun OSS (1.5 session)
- [x] SubTask 4.1: 安装 Aliyun OSS SDK NuGet
- [x] SubTask 4.2: `Storage/AliyunOssStorage : IObjectStorage` 实现 (Upload/GetUrl/Remove)
- [x] SubTask 4.3: `Program.cs` DI 按 `Storage:Provider` 配置注入 (`minio` / `aliyun-oss`)
- [x] SubTask 4.4: 预签名 URL (GetObject) 用于前台产品页直接 OSS 读图
- [x] SubTask 4.5: 文档 `docs/cdn-switch.md` (切换流程 + 回滚)
- [x] SubTask 4.6: E2E 模拟两种 provider 启动 → 上传/下载/删除
- [x] 验证: 切换 provider 后图片 URL 可访问, 前台产品页不 404

### Task 5: P1.4 Search 性能基准 (1 session)
- [x] SubTask 5.1: `spike-test/_bench_search.py` 50 个典型查询
- [x] SubTask 5.2: 并发 10 / 50 / 100 测试, 输出 P50/P95/P99 延迟表
- [x] SubTask 5.3: CI 加 `bench` 步骤, 阈值: P95 < 200ms 搜索, P95 < 100ms typeahead
- [x] SubTask 5.4: 文档 `docs/bench-baseline.md` 记录基线 + 退化告警
- [x] 验证: CI 报告 P95 < 200ms, 文档记录 baseline 数字

---

## Phase 2: P2 字典扩展 (4 任务)

### Task 6: P2.1 字典抽象层 IDictService + BaseDictService (1 session)
- [x] SubTask 6.1: 设计 `IDictService<TItem>` 通用接口 (List/Typeahead/Create/Update/Delete/Restore/Reorder)
- [x] SubTask 6.2: `BaseDictService<TItem>` 抽象基类 (软删/排序/UNIQUE/xrefCount 统一)
- [x] SubTask 6.3: 重构 `OemBrandDictService` 继承 `BaseDictService<XrefOemBrand>` (Day 10 E2E 10/10 仍通过)
- [x] SubTask 6.4: 用 `IEntityTypeConfiguration<TItem>` 分文件配置, DbContext 集中注册
- [x] 验证: Day 10 E2E 10/10 通过, 后续字典 P2.2 实现量 < 100 行/字典

### Task 7: P2.2 7 个新字典 (复用 P2.1 抽象) (2 session)
- [x] SubTask 7.1: `dict_product_name1` Entity + Migration + Service + View + E2E (0.5 session)
- [x] SubTask 7.2: `dict_product_name2` Entity + Migration + Service + View + E2E (0.5 session)
- [x] SubTask 7.3: `dict_type` Entity + Migration + Service + View + E2E + 默认值 seed (0.5 session)
- [x] SubTask 7.4: `dict_oem_no3` Entity + Migration + Service + View + E2E (0.5 session)
- [x] SubTask 7.5: `dict_media` (Media Name + Model 二合一) + `dict_machine` (3 字段) + `dict_engine` (2 字段) (并行, 共 1 session)
- [x] SubTask 7.6: 数据迁移脚本 (从 products/cross_references/machine_applications 提取 distinct)
- [x] SubTask 7.7: typeahead 接入产品表单 (AdminProductFormView 7 分区全覆盖)
- [x] 验证: 7 个字典管理页 + 7 个 typeahead 全部联动, 拖拽排序全部 OK

### Task 8: P2.3 Type 字典排序 + 机器分类 (0.5 session)
- [ ] SubTask 8.1: `spike-test/_seed_dict_defaults.py` (Type: oil/fuel/air/cabin/others; Machine: Agriculture/Commercial/Construction/others)
- [ ] SubTask 8.2: 前台产品页 (P3.3) 按 `dict_type.sort_order` 排序展示
- [ ] SubTask 8.3: machine brand 按 4 大类聚合 (依赖 P2.2 dict_machine)
- [ ] 验证: 前台产品页按 type 排序, machine brand 按 4 大类分组

---

## Phase 3: P3 搜索+展示 (3 任务)

### Task 9: P3.1 搜索容差 UI (±1/±5/±10mm) (0.5 session)
- [ ] SubTask 9.1: `AdminSearchView.vue` 加"尺寸容差"下拉
- [ ] SubTask 9.2: `PublicSearchView.vue` (P3.4) 同步加
- [ ] SubTask 9.3: 选 5mm → 搜索请求带 `tolerance=5` (后端 Day 8.4 已实现)
- [ ] SubTask 9.4: popover 提示"切换容差会显著影响搜索速度"
- [ ] 验证: 容差切换后, 搜索结果数量变化符合预期 (E2E)

### Task 10: P3.2 Excel 多行复制粘贴查询 (1 session)
- [ ] SubTask 10.1: 搜索输入框加"批量粘贴"模式
- [ ] SubTask 10.2: 解析 tab/换行分隔, 拆成多个 OEM 2 或 OEM 3
- [ ] SubTask 10.3: 后端 `/api/search/batch-oem` 接 `oems: string[]`, 返每个 OEM 最佳匹配
- [ ] SubTask 10.4: 前端结果表 1 行 = 1 个查询 OEM
- [ ] SubTask 10.5: E2E 验证粘贴 100 个 OEM → 100 行结果 < 1s
- [ ] 验证: 100 OEM 1 秒内返回, 特殊字符 (中文/斜杠) 不破坏解析

### Task 11: P3.3 前台产品详情页 (公开) (1 session)
- [ ] SubTask 11.1: `PublicProductView.vue` 路由 `/product/:oem`
- [ ] SubTask 11.2: 7 分区折叠展示 (图片 / 基础 / 尺寸 / 性能 / 包装 / 车型 / Cross Ref)
- [ ] SubTask 11.3: SEO `<title>` + OG meta tags
- [ ] SubTask 11.4: 图片按 OEM 编号命名验证 (imageKey 前缀 = `oem2` 编码)
- [ ] SubTask 11.5: 公开搜索 `/search?q=`, 无 admin 鉴权
- [ ] SubTask 11.6: Playwright 截图测试
- [ ] 验证: `/product/11427622448` 公开访问, 首屏 < 1.5s, title/OG 正确

### Task 12: P3.5 对比 UI 完整版 (0.5 session)
- [ ] SubTask 12.1: `AdminCompareView.vue` 6 列布局
- [ ] SubTask 12.2: 高亮差异 (相同灰底/不同黄底)
- [ ] SubTask 12.3: 拖拽列调整顺序
- [ ] SubTask 12.4: 打印优化 CSS `@media print`
- [ ] 验证: 6 产品对比页一次性展示, 差异高亮, 截图 E2E

---

## Phase 4: P4 CI 闭环 + P5 打磨 (3 任务)

### Task 13: P4.1 E2E 全量覆盖 (1 session)
- [ ] SubTask 13.1: 每个字典配 1 个 E2E (7 个: OEM Brand 已有, + 6 个新字典)
- [ ] SubTask 13.2: P3.1 尺寸容差 E2E
- [ ] SubTask 13.3: P3.2 Excel 粘贴 E2E
- [ ] SubTask 13.4: P3.3 前台产品页 Playwright 截图测试
- [ ] SubTask 13.5: P3.4 对比 UI 截图测试
- [ ] SubTask 13.6: CI gate 全部并行跑, < 10 分钟
- [ ] 验证: CI 跑完整 15+ E2E < 10 分钟

### Task 14: P4.2 + P4.3 字典契约 + 视觉回归 (1 session)
- [ ] SubTask 14.1: 后端 `GET /api/admin/dict/_schema` 暴露所有字典字段定义
- [ ] SubTask 14.2: 前端 `npm run test:contract` 验证 TS interface 一致
- [ ] SubTask 14.3: Playwright 跑每个字典管理页截图
- [ ] SubTask 14.4: 视觉回归 (像素 diff > 5% → fail)
- [ ] SubTask 14.5: CI 加 contract + 视觉步骤
- [ ] 验证: 改后端字段不改前端, CI 必报; 拖拽前后截图差异 < 5%

### Task 15: P5 打磨 (Volume / Popover / 主题 / 帮助) (1 session)
- [ ] SubTask 15.1: P5.1 Volume 自动计算 (L×W×H/1e9 m³, 前端实时)
- [ ] SubTask 15.2: P5.2 字段说明 popover (`dict_field_help` 表)
- [ ] SubTask 15.3: P5.3 主题切换 (Pinia/Vue reactive store + localStorage)
- [ ] SubTask 15.4: P5.4 `/admin/help` 路由 (操作指南 + 字典规范)
- [ ] 验证: 输入长宽高 → Volume 自动显示; 主题切换刷新后保持

---

# Task Dependencies

```
Phase 1:
  Task 1 (P0.1 ILIKE)        → 无依赖, 优先
  Task 2 (P0.2 baseline)     → 无依赖
  Task 3 (P1.1 ETL pause)    → 依赖 Task 1 (ILIKE 安全)
  Task 4 (P1.2 CDN)          → 无依赖
  Task 5 (P1.4 bench)        → 无依赖

Phase 2:
  Task 6 (P2.1 抽象层)        → 依赖 Task 1 (ILIKE 修复保证搜索)
  Task 7 (P2.2 5字典)        → 依赖 Task 6 (抽象层)
  Task 8 (P2.3 排序+机器)    → 依赖 Task 7 (Type/Machine 字典)

Phase 3:
  Task 9  (P3.1 容差 UI)     → 依赖 Task 1 (后端已有)
  Task 10 (P3.2 Excel 粘贴)  → 无依赖
  Task 11 (P3.3 前台页)      → 依赖 Task 7 (字典 typeahead)
  Task 12 (P3.5 对比 UI)     → 无依赖 (后端 Day 9.4 已实现)

Phase 4:
  Task 13 (P4.1 E2E)         → 依赖 Task 1-12 全部
  Task 14 (P4.2+P4.3)        → 依赖 Task 6+7 (字典)
  Task 15 (P5 打磨)          → 依赖 Task 11 (前台页样式基础)

并行机会:
  - Task 1, 2, 4, 5 可并行
  - Task 9, 10, 12 可并行
  - Phase 4 三个 Task 可并行
```

---

# 验收里程碑 (Milestone)

- **M1** (Phase 1 末): P0/P1 全绿, ILIKE 安全, ETL 暂停可恢复, CDN 可切换, 性能基线建立
- **M2** (Phase 2 末): 6 字典全部上线, Type 排序生效, 机器分类生效
- **M3** (Phase 3 末): 前台产品页公开可访问, 搜索容差可调, 对比 UI 完整
- **M4** (Phase 4 末): 15+ E2E 全绿 < 10min, 字典契约测试, 视觉回归, P5 全部完成

---

# 工作量汇总

| 阶段 | 任务数 | session 数 |
|------|--------|----------|
| Phase 1 (P0+P1) | 5 | 5 |
| Phase 2 (P2) | 3 | 4 |
| Phase 3 (P3) | 4 | 3 |
| Phase 4 (P4+P5) | 3 | 3 |
| **合计** | **15** | **15** |

> 按 Day 10 节奏 1 session = 1 次连续 push + 全绿。最高优先级: Task 1 (P0.1 ILIKE) — 已发现 1 处 bug, 全局 audit 防漏。
