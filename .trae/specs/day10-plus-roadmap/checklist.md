# Day 10+ Checklist (验证检查点)

> 配合 `tasks.md` 使用,每个 Task 完成后勾选对应检查点。Phase 末全勾才进入下一阶段。

---

## Phase 1: P0 地基 + P1 业务

### Task 1: P0.1 ILIKE ESCAPE 全局修复
- [x] Grep 无 2 参 `EF.Functions.ILike` 调用 (除 `LikeEscapeExtensions` 内部)
- [x] `LikeEscapeExtensions` 帮助方法存在且单测覆盖
- [x] E2E `_test_escape_underscore.py` 1/1 全绿
- [x] dotnet build 0 warning 0 error
- [x] 边界测试: `q=foo%bar`, `q=foo\\bar`, `q=__test__` 全部正确转义

### Task 2: P0.2 EF Core Migrations baseline 自动化
- [x] `spike-test/_ef_migrations_baseline.py` 接受 `migration_id: list[str]` 参数
- [x] `.github/workflows/e2e.yml` 含 `init-postgres` 步骤
- [x] `scripts/db-baseline.sh` 一键可执行
- [x] `docs/ef-migrations-baseline.md` 写完使用流程
- [x] CI workflow 删 DB → 重跑 → 全绿 (回归测试)
- [x] 本地 baseline seed < 30 秒

### Task 3: P1.1 ETL 暂停恢复
- [x] `etl_progress_log` 含 `checkpoint_id` 列 (EF Core Migration `20260702085522_AddEtlCheckpointId`)
- [x] `EtlImportService.PauseActiveTask` / `GetLastPausedCheckpointAsync` 实现 + `_pausedFlag` 线程安全
- [x] Resume 续读: `TriggerAsync(entity, path, mode, startLineNo)` 跳过已 COMMIT 批次 (BatchSize=1000)
- [x] API `POST /api/admin/etl/pause` + `POST /api/admin/etl/resume` 注册 (RateLimit 'etl' 30/min)
- [x] 前端 `AdminEtlView.vue` "暂停"按钮 + paused 状态 Alert + "恢复"按钮
- [x] E2E `_test_pause_resume.py` 4/4 通过 (暂停/恢复/无 paused 404/无活跃 paused=false)
- [x] count reconciliation 一致: stage=affected (无 DISTINCT ON 去重/无 ON CONFLICT 跳过, delta=0)
- [x] Cancel 不影响 Pause: Pause 走 `_pausedFlag`, Cancel 走 `_activeCts.Cancel()` 信号隔离
- [x] Resume 不重复 COMMIT 已存在批次: `startLineNo=checkpointId` 跳过已读行, INSERT 走 `ON CONFLICT DO NOTHING` 幂等

### Task 4: P1.2 CDN 切换 MinIO → Aliyun OSS
- [x] `AliyunOssStorage : IObjectStorage` 实现
- [x] `Program.cs` DI 按 `Storage:Provider` 注入
- [x] 预签名 URL 用于前台读图
- [x] `docs/cdn-switch.md` 写完 (切换 + 回滚)
- [x] E2E 验证两种 provider 上传/下载/删除
- [x] 切换 provider 后 URL 可访问 (curl 200)
- [x] 切回 MinIO 不影响已有图片

### Task 5: P1.4 Search 性能基准
- [x] `spike-test/_bench_search.py` 50 查询 / 10-100 并发
- [x] 输出 P50/P95/P99 延迟 JSON 报告
- [x] CI 加 `bench` 步骤, P95 > 200ms fail
- [x] `docs/bench-baseline.md` 记录 baseline 数字
- [x] typeahead P95 < 100ms
- [x] bench 与已有 E2E 隔离 (不污染测试环境, 只读 + 写 _bench_results.json)

---

## Phase 2: P2 字典扩展

### Task 6: P2.1 字典抽象层
- [ ] `IDictService<TItem>` 接口定义 (7 方法)
- [ ] `BaseDictService<TItem>` 抽象基类
- [ ] `OemBrandDictService` 继承基类, 业务逻辑 < 50 行
- [ ] `IEntityTypeConfiguration<TItem>` 分文件配置
- [ ] Day 10 E2E `_test_day10_oem_brands.py` 10/10 仍通过
- [ ] 抽象无 leaky: 子类不需 override 核心方法

### Task 7: P2.2 7 个新字典
- [x] `dict_product_name1` Entity + Migration + Service + View + E2E
- [x] `dict_product_name2` Entity + Migration + Service + View + E2E
- [x] `dict_type` + 默认值 seed (oil/fuel/air/cabin/others, 5 行)
- [x] `dict_oem_no3` Entity + Migration + Service + View + E2E (5.27M 行 seed)
- [x] `dict_media` (Media Name + Model 二合一, 5 行 seed)
- [x] `dict_machine` (machine brand + model + name 三合一, 1000 行 seed)
- [x] `dict_engine` (Engine brand + type, 5 行 seed)
- [x] 数据迁移脚本从 products/cross_references/machine_applications 提取 (6 个 _seed_dict_*.py 全部跑通)
- [x] 7 个字典管理页拖拽排序 + typeahead (el-dropdown 集成进 AppHeader)
- [x] AdminProductFormView 7 分区 typeahead 全部联动 (productName1/2/type/oemNo3/media/machine/engine + oemBrand)
- [x] E2E `_test_p22_seven_dicts.py` 9/9 全绿 (1 表结构 + 1 鉴权 + 7 字典 CRUD 生命周期)
- [x] Day 10 回归 `_test_day10_oem_brands.py` 10/10 仍通过 (ILIKE 反射重写后兼容性验证)
- [x] BaseDictService 多字段 OR 搜索 (ExtraSearchProperties 机制, BuildSearchPredicate 走方法组引用)

### Task 8: P2.3 Type 排序 + 机器分类
- [ ] `spike-test/_seed_dict_defaults.py` 跑通
- [ ] 前台产品页按 `dict_type.sort_order` 排序
- [ ] machine brand 按 4 大类聚合 (Agriculture/Commercial/Construction/others)
- [ ] 拖动 type 排序后, 前台立即生效
- [ ] E2E 验证排序持久化

---

## Phase 3: P3 搜索+展示

### Task 9: P3.1 搜索容差 UI
- [ ] `AdminSearchView.vue` "尺寸容差" 下拉 (1/5/10mm)
- [ ] `PublicSearchView.vue` 同步实现
- [ ] 选 5mm → 请求带 `tolerance=5` (后端 Day 8.4 已实现)
- [ ] popover 提示"切换容差会显著影响搜索速度"
- [ ] E2E 验证切换前后结果数变化

### Task 10: P3.2 Excel 多行粘贴
- [ ] 搜索输入框"批量粘贴"模式
- [ ] 解析 tab/换行/逗号分隔
- [ ] API `POST /api/search/batch-oem` 接 `oems: string[]`
- [ ] 前端结果表 1 行 1 OEM
- [ ] E2E 100 OEM < 1s 返回
- [ ] 特殊字符 (中文/斜杠/引号) 不破坏解析
- [ ] 空行/重复行 健壮处理

### Task 11: P3.3 前台产品详情页
- [ ] `PublicProductView.vue` 路由 `/product/:oem` 存在
- [ ] 7 分区折叠展示 (图片 / 基础 / 尺寸 / 性能 / 包装 / 车型 / Cross Ref)
- [ ] SEO `<title>` 格式正确
- [ ] OG meta tags (og:title / og:image / og:description)
- [ ] 公开 `/search?q=` 无 admin 鉴权
- [ ] Playwright 截图测试通过
- [ ] 首屏 < 1.5s (lighthouse)
- [ ] imageKey 前缀 = `oem2` 编码 (规格 R5)
- [ ] URL `/product/11427622448` 公开可访问 (无 token)

### Task 12: P3.5 对比 UI
- [ ] `AdminCompareView.vue` 6 列布局
- [ ] 差异高亮 (相同灰底/不同黄底)
- [ ] 拖拽列调序
- [ ] `@media print` 打印优化 CSS
- [ ] 6 产品对比页一次性展示所有字段
- [ ] Playwright 截图测试
- [ ] E2E 验证高亮规则

---

## Phase 4: P4 CI 闭环 + P5 打磨

### Task 13: P4.1 E2E 全量
- [ ] 6 个字典 E2E (OEM Brand 已有, + 5 新)
- [ ] P3.1 尺寸容差 E2E
- [ ] P3.2 Excel 粘贴 E2E
- [ ] P3.3 前台产品页 Playwright 截图
- [ ] P3.4 对比 UI Playwright 截图
- [ ] CI 15+ E2E 并行跑 < 10 分钟
- [ ] 任一 fail → workflow 标红 → block merge
- [ ] 测试隔离: 每个 E2E 独立数据, 不污染

### Task 14: P4.2 + P4.3 字典契约 + 视觉回归
- [ ] API `GET /api/admin/dict/_schema` 返回所有 dict 字段定义
- [ ] `npm run test:contract` 验证 TS interface 一致
- [ ] Playwright 跑每个字典管理页截图
- [ ] 像素 diff > 5% fail
- [ ] 拖拽前后截图差异 < 5%
- [ ] CI 加 contract + 视觉步骤
- [ ] 改后端字段不改前端 → CI 必报

### Task 15: P5 打磨
- [ ] P5.1 Volume = L×W×H/1e9 m³ 自动计算
- [ ] 前端实时显示 (输入长宽高即更新)
- [ ] P5.2 `dict_field_help` 表 + popover
- [ ] 字段 `?` 图标 + 鼠标悬停显示
- [ ] P5.3 主题切换 Pinia store
- [ ] localStorage 持久化
- [ ] 深色/浅色模式适配表单/表格/弹窗
- [ ] P5.4 `/admin/help` 路由存在
- [ ] 操作指南 + 字典规范内容齐
- [ ] 主题切换刷新后保持

---

## Phase 末总验收 (跨 Task)

### Phase 1 末
- [x] P0.1 + P0.2 + P1.1 + P1.2 + P1.4 全部勾选
- [x] CI 全绿
- [x] 无 dotnet warning
- [x] 无 Pylance error
- [x] git 3+ commits pushed

### Phase 2 末
- [ ] P2.1 + P2.2 + P2.3 全部勾选
- [ ] 6 字典 + 抽象层
- [ ] Day 10 E2E 仍 10/10 (回归)
- [ ] 6 新 E2E 全部 10/10
- [ ] typeahead 联动产品表单 7 分区全覆盖

### Phase 3 末
- [ ] P3.1 + P3.2 + P3.3 + P3.5 全部勾选
- [ ] 前台产品页公开可访问 (无 token)
- [ ] 搜索容差 UI 可切换
- [ ] Excel 粘贴 100 OEM < 1s
- [ ] 对比 UI 差异高亮 + 截图 E2E

### Phase 4 末 (项目成功标准)
- [ ] 6 字典全部上线, 拖拽 + typeahead ✓
- [ ] 前台产品页公开, 域名格式 product name 1+2+OEM BRAND+OEM NO ✓
- [ ] 后台搜索 ±1/±5/±10mm 切换 + Excel 多行粘贴 ✓
- [ ] 对比 UI 6 列 + 差异高亮 ✓
- [ ] CI 15+ E2E 全绿 < 10 分钟 ✓
- [ ] 前后端契约测试自动校验 ✓
- [ ] 主题深色/浅色切换 ✓
- [ ] Volume 自动计算 ✓
- [ ] ILIKE 全部安全转义 (无 LIKE 注入) ✓
- [ ] P95 搜索 < 200ms, P95 typeahead < 100ms ✓
