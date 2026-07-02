# Day 10+ Checklist (验证检查点)

> 配合 `tasks.md` 使用,每个 Task 完成后勾选对应检查点。Phase 末全勾才进入下一阶段。
>
> **验证原则**: 每条检查点都附"验证命令",可复制粘贴直接执行。预期输出 / 期望值已列明。

---

## Phase 1: P0 地基 + P1 业务 ✅ 全部完成

### Task 1: P0.1 ILIKE ESCAPE 全局修复 ✅
- [x] Grep 无 2 参 `EF.Functions.ILike` 调用 (除 `LikeEscapeExtensions` 内部)
  - 验证: `rg -n "EF\.Functions\.ILike" backend/src/ --type cs` 预期 0 处
- [x] `LikeEscapeExtensions` 帮助方法存在且单测覆盖
  - 验证: `ls backend/src/SakuraFilter.Api/Common/LikeEscapeExtensions.cs`
- [x] E2E `_test_escape_underscore.py` 1/1 全绿
  - 验证: `cd spike-test && python _test_escape_underscore.py` 预期 PASS
- [x] dotnet build 0 warning 0 error
  - 验证: `cd backend/src/SakuraFilter.Api && dotnet build --nologo` 预期 0 Error 0 Warning
- [x] 边界测试: `q=foo%bar`, `q=foo\\bar`, `q=__test__` 全部正确转义
  - 验证: 跑 E2E 边界 case 全部命中预期 (1 条)

### Task 2: P0.2 EF Core Migrations baseline 自动化 ✅
- [x] `spike-test/_ef_migrations_baseline.py` 接受 `migration_id: list[str]` 参数
  - 验证: `python _ef_migrations_baseline.py --help` 预期显示参数
- [x] `.github/workflows/e2e.yml` 含 `init-postgres` 步骤
  - 验证: `grep "init-postgres" .github/workflows/e2e.yml`
- [x] `scripts/db-baseline.sh` 一键可执行
  - 验证: `bash scripts/db-baseline.sh` 预期 < 30s 完成
- [x] `docs/ef-migrations-baseline.md` 写完使用流程
  - 验证: `ls docs/ef-migrations-baseline.md`
- [x] CI workflow 删 DB → 重跑 → 全绿 (回归测试)
  - 验证: 手动删 PG → push → workflow 绿
- [x] 本地 baseline seed < 30 秒
  - 验证: `time python _ef_migrations_baseline.py` 预期 real < 30s

### Task 3: P1.1 ETL 暂停恢复 ✅
- [x] `etl_progress_log` 含 `checkpoint_id` 列 (EF Core Migration `20260702085522_AddEtlCheckpointId`)
  - 验证: `psql -c "\d etl_progress_log" | grep checkpoint_id`
- [x] `EtlImportService.PauseActiveTask` / `GetLastPausedCheckpointAsync` 实现 + `_pausedFlag` 线程安全
- [x] Resume 续读: `TriggerAsync(entity, path, mode, startLineNo)` 跳过已 COMMIT 批次 (BatchSize=1000)
- [x] API `POST /api/admin/etl/pause` + `POST /api/admin/etl/resume` 注册 (RateLimit 'etl' 30/min)
- [x] 前端 `AdminEtlView.vue` "暂停"按钮 + paused 状态 Alert + "恢复"按钮
- [x] E2E `_test_pause_resume.py` 4/4 通过
  - 验证: `cd spike-test && python _test_pause_resume.py` 预期 4/4 PASS
- [x] count reconciliation 一致: stage=affected (delta=0)
  - 验证: 暂停/恢复 3 万行, `affected_rows == stage_count` 且 skipped=0
- [x] Cancel 不影响 Pause: Pause 走 `_pausedFlag`, Cancel 走 `_activeCts.Cancel()` 信号隔离
- [x] Resume 不重复 COMMIT 已存在批次: `startLineNo=checkpointId` 跳过已读行, INSERT 走 `ON CONFLICT DO NOTHING`

### Task 4: P1.2 CDN 切换 MinIO → Aliyun OSS ✅
- [x] `AliyunOssStorage : IObjectStorage` 实现
- [x] `Program.cs` DI 按 `Storage:Provider` 注入
  - 验证: `grep "Storage:Provider" backend/src/SakuraFilter.Api/Program.cs`
- [x] 预签名 URL 用于前台读图
- [x] `docs/cdn-switch.md` 写完 (切换 + 回滚)
- [x] E2E 验证两种 provider 上传/下载/删除
  - 验证: `cd spike-test && python _test_cdn_switch.py` 预期 2/2 PASS
- [x] 切换 provider 后 URL 可访问 (curl 200)
  - 验证: `curl -I $(oss_url) | head -1` 预期 200
- [x] 切回 MinIO 不影响已有图片

### Task 5: P1.4 Search 性能基准 ✅
- [x] `spike-test/_bench_search.py` 50 查询 / 10-100 并发
- [x] 输出 P50/P95/P99 延迟 JSON 报告
  - 验证: `cat spike-test/_bench_results.json | jq .p95` 预期 < 阈值
- [x] CI 加 `bench` 步骤, P95 > 200ms fail
- [x] `docs/bench-baseline.md` 记录 baseline 数字
- [x] typeahead P95 < 100ms
  - 验证: `jq .typeahead_p95 spike-test/_bench_results.json` 预期 < 100
- [x] bench 与已有 E2E 隔离 (只读 + 写 _bench_results.json)

---

## Phase 2: P2 字典扩展 — 🟡 5/6 完成 (Task 8 进行中)

### Task 6: P2.1 字典抽象层 ✅
- [x] `IDictService<TItem>` 接口定义 (7 方法)
  - 验证: `ls backend/src/SakuraFilter.Api/Services/IDictService.cs` + 7 个方法签名
- [x] `BaseDictService<TItem>` 抽象基类
- [x] `OemBrandDictService` 继承基类, 业务逻辑 < 50 行
  - 验证: `wc -l backend/src/SakuraFilter.Api/Services/OemBrandDictService.cs` 预期 < 50
- [x] `IEntityTypeConfiguration<TItem>` 分文件配置
  - 验证: `ls backend/src/SakuraFilter.Api/Data/Configurations/`
- [x] Day 10 E2E `_test_day10_oem_brands.py` 10/10 仍通过
  - 验证: `cd spike-test && python _test_day10_oem_brands.py` 预期 10/10 PASS
- [x] 抽象无 leaky: 子类不需 override 核心方法
  - 验证: `grep "override" backend/src/SakuraFilter.Api/Services/OemBrandDictService.cs` 仅 BuildSearchPredicate

### Task 7: P2.2 7 个新字典 ✅
- [x] `dict_product_name1` Entity + Migration + Service + View + E2E
  - 验证: `ls backend/src/SakuraFilter.Api/Entities/DictProductName1.cs`
- [x] `dict_product_name2` (同上结构)
- [x] `dict_type` + 默认值 seed (oil/fuel/air/cabin/others, 5 行)
  - 验证: `psql -c "SELECT count(*) FROM dict_type WHERE is_deleted=false"` 预期 5
- [x] `dict_oem_no3` Entity + Migration + Service + View + E2E (5.27M 行 seed)
  - 验证: `psql -c "SELECT count(*) FROM dict_oem_no3"` 预期 ~5.27M
- [x] `dict_media` (Media Name + Model 二合一, 5 行 seed)
- [x] `dict_machine` (machine brand + model + name 三合一, 1000 行 seed)
- [x] `dict_engine` (Engine brand + type, 5 行 seed)
- [x] 数据迁移脚本从 products/cross_references/machine_applications 提取 (6 个 `_seed_dict_*.py` 全部跑通)
  - 验证: `ls spike-test/_seed_dict_*.py` 预期 6 个
- [x] 7 个字典管理页拖拽排序 + typeahead (el-dropdown 集成进 AppHeader)
  - 验证: 手动访问 `/admin/dict/oem-brand` 等 7 页拖拽生效
- [x] AdminProductFormView 7 分区 typeahead 全部联动
  - 验证: 手动访问 `/admin/product/new` 7 个 typeahead 都从字典取
- [x] E2E `_test_p22_seven_dicts.py` 9/9 全绿
  - 验证: `cd spike-test && python _test_p22_seven_dicts.py` 预期 9/9 PASS
- [x] Day 10 回归 `_test_day10_oem_brands.py` 10/10 仍通过 (ILIKE 反射重写后兼容性验证)
  - 验证: `cd spike-test && python _test_day10_oem_brands.py` 预期 10/10 PASS
- [x] BaseDictService 多字段 OR 搜索 (ExtraSearchProperties 机制, BuildSearchPredicate 走方法组引用)

### Task 8: P2.3 Type 排序 + 机器分类 🟡
- [ ] `spike-test/_seed_dict_defaults.py` 跑通
  - 验证: `cd spike-test && python _seed_dict_defaults.py`
  - 验证: `psql -c "SELECT * FROM dict_type ORDER BY sort_order"` 预期 oil(1) fuel(2) air(3) cabin(4) others(99)
- [ ] 前台产品页按 `dict_type.sort_order` 排序
  - 验证: `curl http://localhost:5148/api/products/by-type | jq '.[].type'` 预期按 sort_order 排
- [ ] machine brand 按 4 大类聚合
  - 验证: `curl http://localhost:5148/api/machine-brands/aggregated` 返 `{ Agriculture, Commercial, Construction, others }`
- [ ] 拖动 type 排序后, 前台立即生效
  - 验证: E2E 改 sort_order + GET /by-type 验证顺序变化
- [ ] E2E 验证排序持久化
  - 验证: `cd spike-test && python _test_type_ordering.py` 预期 5/5 PASS

---

## Phase 3: P3 搜索+展示 ⏳ 待开始

### Task 9: P3.1 搜索容差 UI
- [ ] `AdminSearchView.vue` "尺寸容差" 下拉 (1/5/10mm)
  - 验证: 浏览器访问 `/admin/search` 看到下拉
- [ ] `PublicSearchView.vue` 同步实现
  - 验证: 浏览器访问 `/search` 看到下拉
- [ ] 选 5mm → 请求带 `tolerance=5` (后端 Day 8.4 已实现)
  - 验证: DevTools Network 看到 `?tolerance=5&...`
- [ ] popover 提示"切换容差会显著影响搜索速度"
  - 验证: 鼠标悬停下拉, 看到提示文字
- [ ] E2E 验证切换前后结果数变化
  - 验证: `cd spike-test && python _test_tolerance_ui.py` 预期 3/3 PASS
  - 1mm → 3 条, 10mm → 50+ 条 (H1=100 基准)

### Task 10: P3.2 Excel 多行粘贴
- [ ] 搜索输入框"批量粘贴"模式
  - 验证: 浏览器看到 el-tabs 切换 单条/批量
- [ ] 解析 tab/换行/逗号分隔
  - 验证: 粘贴 100 个 OEM, 自动拆成 100 元素数组
- [ ] API `POST /api/search/batch-oem` 接 `oems: string[]`
  - 验证: `curl -X POST http://localhost:5148/api/search/batch-oem -H "Content-Type: application/json" -d '{"oems":["1142","1234"]}'`
- [ ] 前端结果表 1 行 1 OEM
  - 验证: 浏览器看到表格, 100 行
- [ ] E2E 100 OEM < 1s 返回
  - 验证: `cd spike-test && python _test_batch_oem.py` 预期 PASS + 耗时 < 1s
- [ ] 特殊字符 (中文/斜杠/引号) 不破坏解析
  - 验证: E2E 边界 case: "滤清器 1142" / "AB/CD" / `"OEN-123"` 全部正确
- [ ] 空行/重复行 健壮处理
  - 验证: E2E 边界 case: 含空行/重复/前后空格 全部去重

### Task 11: P3.3 前台产品详情页
- [ ] `PublicProductView.vue` 路由 `/product/:oem` 存在
  - 验证: `ls frontend/src/views/public/PublicProductView.vue`
- [ ] 7 分区折叠展示 (图片 / 基础 / 尺寸 / 性能 / 包装 / 车型 / Cross Ref)
  - 验证: 浏览器访问 `/product/11427622448` 看到 7 个折叠面板
- [ ] SEO `<title>` 格式正确
  - 验证: View Page Source, title = "ProductName1 ProductName2 OEM BRAND OEM NO - SakuraFilter"
- [ ] OG meta tags (og:title / og:image / og:description)
  - 验证: View Page Source, 4 个 og meta 都有
- [ ] 公开 `/search?q=` 无 admin 鉴权
  - 验证: `curl http://localhost:5148/search?q=...` 不需要 token
- [ ] Playwright 截图测试通过
  - 验证: `cd frontend && npx playwright test tests/visual/public-product.spec.ts` 预期 PASS
- [ ] 首屏 < 1.5s (lighthouse)
  - 验证: `npx lighthouse http://localhost:5148/product/11427622448 --only-categories=performance` 预期 score > 90
- [ ] imageKey 前缀 = `oem2` 编码 (规格 R5)
  - 验证: `curl -I $(image_url) | head -1` 预期 200, URL 含 `oem2/`
- [ ] URL `/product/11427622448` 公开可访问 (无 token)
  - 验证: `curl -I http://localhost:5148/product/11427622448` 预期 200

### Task 12: P3.5 对比 UI
- [ ] `AdminCompareView.vue` 6 列布局
  - 验证: 浏览器访问 `/admin/compare` 看到 6 列 grid
- [ ] 差异高亮 (相同灰底/不同黄底)
  - 验证: 6 产品同一字段全等 → 灰底; 不全等 → 黄底
- [ ] 拖拽列调序
  - 验证: 鼠标拖动列头, 列顺序变化
- [ ] `@media print` 打印优化 CSS
  - 验证: 浏览器 Ctrl+P, 看到无按钮 + A4 横向
- [ ] 6 产品对比页一次性展示所有字段
  - 验证: 6 产品加入, 30+ 字段全部展示
- [ ] Playwright 截图测试
  - 验证: `cd frontend && npx playwright test tests/visual/compare.spec.ts` 预期 PASS
- [ ] E2E 验证高亮规则
  - 验证: `cd spike-test && python _test_compare.py` 预期 PASS

---

## Phase 4: P4 CI 闭环 + P5 打磨 ⏳ 待开始

### Task 13: P4.1 E2E 全量
- [ ] 7 个字典 E2E (OEM Brand 已有, + 6 新)
  - 验证: `ls spike-test/_test_dict_*.py` 预期 6 个新文件
- [ ] P3.1 尺寸容差 E2E
  - 验证: `cd spike-test && python _test_tolerance_ui.py` 预期 PASS
- [ ] P3.2 Excel 粘贴 E2E
  - 验证: `cd spike-test && python _test_batch_oem.py` 预期 PASS
- [ ] P3.3 前台产品页 Playwright 截图
  - 验证: `cd frontend && npx playwright test tests/visual/public-product.spec.ts` 预期 PASS
- [ ] P3.4 对比 UI Playwright 截图
  - 验证: `cd frontend && npx playwright test tests/visual/compare.spec.ts` 预期 PASS
- [ ] CI 15+ E2E 并行跑 < 10 分钟
  - 验证: 触发 workflow, 查看总耗时 < 10min
- [ ] 任一 fail → workflow 标红 → block merge
  - 验证: 故意改坏一个 E2E, push, workflow 红叉 + 不能 merge
- [ ] 测试隔离: 每个 E2E 独立数据, 不污染
  - 验证: 连续跑 2 次 E2E, 第二次仍 100% PASS

### Task 14: P4.2 + P4.3 字典契约 + 视觉回归
- [ ] API `GET /api/admin/dict/_schema` 返回所有 dict 字段定义
  - 验证: `curl -H "X-Admin-Token: $TOKEN" http://localhost:5148/api/admin/dict/_schema` 返 JSON 含 7 字典
- [ ] `npm run test:contract` 验证 TS interface 一致
  - 验证: `cd frontend && npm run test:contract` 预期 PASS
- [ ] Playwright 跑每个字典管理页截图
  - 验证: `cd frontend && npx playwright test tests/visual/dict-pages.spec.ts` 预期 PASS
- [ ] 像素 diff > 5% fail
  - 验证: 故意改一个字典管理页样式, 重跑, 失败
- [ ] 拖拽前后截图差异 < 5%
  - 验证: 拖拽 type 排序, 截图前后差异 < 5%
- [ ] CI 加 contract + 视觉步骤
  - 验证: `grep "test:contract\|test:visual" .github/workflows/e2e.yml`
- [ ] 改后端字段不改前端 → CI 必报
  - 验证: 后端 entity 加字段不更新前端 TS, push, CI 红叉

### Task 15: P5 打磨
- [ ] P5.1 Volume = L×W×H/1e9 m³ 自动计算
  - 验证: 浏览器访问产品表单, 输入 L=300 W=200 H=150, Volume 字段显示 0.009
- [ ] 前端实时显示 (输入长宽高即更新)
  - 验证: 改任意一边长, Volume 实时更新
- [ ] P5.2 `dict_field_help` 表 + popover
  - 验证: `psql -c "\d dict_field_help"` + 鼠标悬停字段 `?` 图标看到说明
- [ ] 字段 `?` 图标 + 鼠标悬停显示
  - 验证: 30+ 字段全部有 `?` 图标
- [ ] P5.3 主题切换 Pinia store
  - 验证: `ls frontend/src/stores/theme.ts`
- [ ] localStorage 持久化
  - 验证: DevTools Application → Local Storage → theme = "dark"
- [ ] 深色/浅色模式适配表单/表格/弹窗
  - 验证: 切换主题, 全部组件深色/浅色变化
- [ ] P5.4 `/admin/help` 路由存在
  - 验证: 浏览器访问 `/admin/help` 看到内容
- [ ] 操作指南 + 字典规范内容齐
  - 验证: 5 个内容模块全部存在 (快速开始/字典规范/批量导入/容差建议/FAQ)
- [ ] 主题切换刷新后保持
  - 验证: 切深色 → F5 刷新 → 仍深色

---

## Phase 末总验收 (跨 Task)

### Phase 1 末 ✅
- [x] P0.1 + P0.2 + P1.1 + P1.2 + P1.4 全部勾选
- [x] CI 全绿
- [x] 无 dotnet warning
- [x] 无 Pylance error
- [x] git 3+ commits pushed

### Phase 2 末 🟡
- [x] P2.1 + P2.2 完成, P2.3 进行中
- [x] 7 字典 + 抽象层
- [x] Day 10 E2E 仍 10/10 (回归)
- [x] 6 新 E2E 全部 10/10 (P2.2 9/9 已过)
- [x] typeahead 联动产品表单 7 分区全覆盖
- [ ] P2.3 Type 排序 + Machine 4 大类
- [ ] Task 8 E2E `_test_type_ordering.py` 5/5

### Phase 3 末 ⏳
- [ ] P3.1 + P3.2 + P3.3 + P3.5 全部勾选
- [ ] 前台产品页公开可访问 (无 token)
- [ ] 搜索容差 UI 可切换
- [ ] Excel 粘贴 100 OEM < 1s
- [ ] 对比 UI 差异高亮 + 截图 E2E

### Phase 4 末 (项目成功标准) ⏳
- [ ] 7 字典全部上线, 拖拽 + typeahead ✓
- [ ] 前台产品页公开, 域名格式 product name 1+2+OEM BRAND+OEM NO ✓
- [ ] 后台搜索 ±1/±5/±10mm 切换 + Excel 多行粘贴 ✓
- [ ] 对比 UI 6 列 + 差异高亮 ✓
- [ ] CI 15+ E2E 全绿 < 10 分钟 ✓
- [ ] 前后端契约测试自动校验 ✓
- [ ] 主题深色/浅色切换 ✓
- [ ] Volume 自动计算 ✓
- [ ] ILIKE 全部安全转义 (无 LIKE 注入) ✓
- [ ] P95 搜索 < 200ms, P95 typeahead < 100ms ✓

---

## 验证命令速查 (复制粘贴执行)

### 每日开发必跑
```bash
# 后端编译
cd d:\projects\sakurafilter\backend\src\SakuraFilter.Api
dotnet build --nologo

# 启动后端
dotnet run --urls "http://localhost:5148"

# 跑 E2E (新窗口)
cd d:\projects\sakurafilter\spike-test
python _test_day10_oem_brands.py       # Day 10 回归
python _test_p22_seven_dicts.py         # P2.2 7 字典
python _test_pause_resume.py            # P1.1 暂停恢复
python _bench_search.py                 # P1.4 性能基准
```

### CI 触发 (推 master)
```bash
git add -A
git commit -m "task-XX: [简述]"
git push origin master  # 触发 .github/workflows/e2e.yml
```

### 数据库检查
```bash
# 字典数据量
psql -h localhost -U postgres -d spike_test_v3 -c "
  SELECT 'dict_type' AS t, count(*) FROM dict_type
  UNION ALL SELECT 'dict_oem_no3', count(*) FROM dict_oem_no3
  UNION ALL SELECT 'dict_machine', count(*) FROM dict_machine
  UNION ALL SELECT 'dict_engine', count(*) FROM dict_engine
  UNION ALL SELECT 'dict_media', count(*) FROM dict_media
  UNION ALL SELECT 'dict_product_name1', count(*) FROM dict_product_name1
  UNION ALL SELECT 'dict_product_name2', count(*) FROM dict_product_name2;
"

# EF Core migration 历史
psql -h localhost -U postgres -d spike_test_v3 -c "
  SELECT * FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\";
"
```

### ETL 进度查询
```bash
psql -h localhost -U postgres -d spike_test_v3 -c "
  SELECT id, entity, mode, status, checkpoint_id, stage_count, affected_rows, started_at, completed_at
  FROM etl_progress_log ORDER BY id DESC LIMIT 5;
"
```

### 性能基准 JSON 查看
```bash
cat d:\projects\sakurafilter\spike-test\_bench_results.json | jq .
# 预期: p50/p95/p99/typeahead_p50/typeahead_p95/typeahead_p99
```

---

## 失败排查索引 (按错误信息定位)

| 错误 | 排查文件 | 命令 |
|------|---------|------|
| `Npgsql.PostgresException: 42P10` (ON CONFLICT) | `Data/Configurations/*.cs` | 检查 ON CONFLICT 目标列是否配 `.IsUnique()` |
| `Npgsql.PostgresException: 23502` (NOT NULL) | `Data/Configurations/*.cs` | 检查 NOT NULL 列是否配 `HasDefaultValue` / `HasDefaultValueSql` |
| `EF Core 8: nullable decimal 比较静默丢 WHERE` | `BaseDictService.cs` | 用 `(HasValue && Value >= lo)` 复合表达式 |
| `CI bash set -e cd 失败 0 秒退出` | `.github/workflows/e2e.yml` | 检查 working-directory 与 cd 路径, 加 ::error:: 注解 |
| `ETL _activeCts 未初始化` | `EtlImportService.cs` | 必须用 `TriggerAsync` 而非直接调用 `Import*Async` |
| `Aliyun OSS 同步 SDK 阻塞 async` | `Storage/AliyunOssStorage.cs` | 同步调用包装 `Task.Run(...)` |
| `Excel 粘贴中文乱码` | `PublicSearchView.vue` | 检查 textarea 字符编码 + 正则 Unicode |

---

## 状态总览 (实时同步)

| Phase | 任务 | 状态 | 完成度 |
|-------|------|------|--------|
| Phase 1 | P0+P1 (5 任务) | ✅ | 5/5 (100%) |
| Phase 2 | P2 (3 任务) | 🟡 | 2/3 (67%) |
| Phase 3 | P3 (4 任务) | ⏳ | 0/4 (0%) |
| Phase 4 | P4+P5 (3 任务) | ⏳ | 0/3 (0%) |
| **总计** | **15 任务** | **🟡** | **7/15 (47%)** |

> 实时更新: 每次 Task 完成立即勾选 + 更新本表。
