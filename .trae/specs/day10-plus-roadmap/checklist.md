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

## Phase 2: P2 字典扩展 — ✅ 全部完成

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

### Task 8: P2.3 Type 排序 + 机器分类 ✅
- [x] `spike-test/_seed_dict_defaults.py` 跑通
  - 验证: `psql -c "SELECT * FROM dict_type ORDER BY sort_order"` 实际 oil(1) fuel(2) air(3) cabin(4) others(99) ✓
- [x] 前台公开端点按 `dict_type.sort_order` 排序
  - 验证: `curl http://localhost:5148/api/public/products/by-type` 返 5 个 group, 顺序 [oil, fuel, air, cabin, others] ✓
- [x] machine brand 按 4 大类聚合
  - 验证: `curl http://localhost:5148/api/public/machine-brands/aggregated` 返 `{ Agriculture, Commercial, Construction, others }` 4 大类齐全 ✓
  - 验证: `totalCount=5` 与 4 大类累加一致 ✓
- [x] dict_machine 表加 machine_category 列 (EF Migration 20260702133148_AddMachineCategory)
  - 验证: `idx_dict_machine_category` 索引存在, 默认值 'others' ✓
- [x] AdminMachinesView 加 category 编辑 (4 大类 el-select + 列表 tag)
- [x] MachineDictService 加 `ListMachinesByCategoryAsync` + `UpdateMachineCategoryAsync` (白名单校验)
- [x] 拖动 type 排序后, 前台立即生效
  - 验证: E2E Case 4 改 sort_order → GET /by-type 顺序立即变化, 恢复原值成功 ✓
- [x] E2E 验证排序持久化
  - 验证: `cd spike-test && python _test_type_ordering.py` 实际 5/5 PASS ✓
  - 验证: P2.2 回归 9/9, Day 10 回归 10/10 全部 PASS, 无破坏

---

## Phase 3: P3 搜索+展示 ⏳ 待开始

> **规格依据**: 新思路.xlsx → "后台搜索统筹" / "对比界面" / "前端展示内容" / "各分区管理界面" 5 个 sheet

### Task 9: P3.1 搜索容差 UI (±5mm 固定)
- [ ] 后端 `tolerance` 参数默认 5
  - 验证: `grep "tolerance" backend/.../AdminSearchController.cs` 应见 `int? tolerance = 5`
  - 失败: 改为 5
- [ ] 前端 `AdminSearchView.vue` **无容差下拉**,尺寸字段请求固定 `tolerance=5`
  - 验证: 浏览器访问 `/admin/search` 看到尺寸字段, 但**没有**容差下拉
  - DevTools Network 看到 `?tolerance=5&h1=...&d1=...`
- [ ] `PublicSearchView.vue` (P3.4) 同步实现
- [ ] (可选) 尺寸字段 popover 提示"搜索范围 ±5mm"
- [ ] E2E 验证搜索结果符合 ±5mm 范围
  - 验证: `cd spike-test && python _test_tolerance_ui.py` 预期 2/2 PASS
  - Case 1: H1=100 → 约 20 条 (H1 ∈ 95-105)
  - Case 2: H1=100 + H2=200 → 5-10 条 (双字段 AND)

### Task 10: P3.2 Excel 多行粘贴 (OEM 2 / OEM 3)
- [ ] 搜索输入框"批量粘贴"模式 (OEM 2/3 专用)
  - 验证: 浏览器看到 el-tabs 切换 单条/批量
- [ ] 解析 tab/换行/逗号/分号分隔
  - 验证: 粘贴 100 个 OEM, 自动拆成 100 元素数组
- [ ] API `POST /api/search/batch-oem` 接 `oems: string[]`
  - 验证: `curl -X POST http://localhost:5148/api/search/batch-oem -H "Content-Type: application/json" -d '{"oems":["11427622448","11427622449"]}'`
- [ ] 前端结果表 1 行 1 OEM
  - 验证: 浏览器看到表格, 100 行
- [ ] E2E 100 OEM < 1s 返回
  - 验证: `cd spike-test && python _test_batch_oem.py` 预期 PASS + 耗时 < 1s
- [ ] 特殊字符 (中文/斜杠/引号) 不破坏解析
  - 验证: E2E 边界 case: "滤清器 1142" / "AB/CD" / `"OEN-123"` 全部正确
- [ ] 空行/重复行 健壮处理
  - 验证: E2E 边界 case: 含空行/重复/前后空格 全部去重

### Task 11: P3.3 前台产品详情页
- [x] `PublicProductView.vue` 路由 `/product/:slug` 存在
  - 验证: `ls frontend/src/views/public/PublicProductView.vue`
  - URL 格式: `/product/{name1}-{name2}-{oemBrand}-{oemNo}` (按规格 R1)
- [x] 7 分区折叠展示 (按"后台新增产品格式"规格)
  - 验证: 浏览器访问 `/product/...` 看到 7 个折叠面板
  - 字段: 见 `spec.md` P3.3 7 分区字段表 (分区 1-7 共 30+ 字段)
- [x] SEO `<title>` 格式正确
  - 验证: View Page Source, title = "ProductName1 ProductName2 OEM BRAND OEM NO - SakuraFilter"
- [x] OG meta tags (og:title / og:image / og:description / og:type=product)
  - 验证: View Page Source, 4 个 og meta 都有
- [x] 公开 `/product/:slug` 无 admin 鉴权
  - 验证: `curl -I http://localhost:5148/product/oil-filter-of100-mann-w950` 预期 200 (无 token)
- [ ] Playwright 截图测试通过
  - 验证: `cd frontend && npx playwright test tests/visual/public-product.spec.ts` 预期 PASS
- [ ] 首屏 < 1.5s (lighthouse)
  - 验证: `npx lighthouse http://localhost:5148/product/oil-filter-of100-mann-w950 --only-categories=performance` 预期 score > 90
- [ ] imageKey 命名严格按 R5 规格 (按 OEM 编号)
  - 验证: 主图 `oem2/{OEM}.jpg`, 副图 `oem2/{OEM}_{slot}.jpg`
  - 验证: `curl -I $(image_url) | head -1` 预期 200, URL 含 `oem2/`
  - 缺图回退: `static/logo.png`

### Task 11.5: P3.4 公开搜索页 (8 字段多框) — 新增
- [ ] `PublicSearchView.vue` 8 字段多框布局
  - 验证: 浏览器看到 8 个 `<el-input>` (2 行 4 列)
  - 字段: oem brand / oem 2 no / oem 3 no / machine brand / machine model / model name / engine brand / engine type
- [ ] 后端 `GET /api/public/search` 接 8 可选 string 参数
  - 验证: `curl http://localhost:5148/api/public/search?oemBrand=CAT` 返 JSON 数组
  - 验证: 多字段 AND: `?oemBrand=MANN&machineBrand=Caterpillar`
- [ ] URL 同步 query 参数 (可分享)
  - 验证: 字段值变化时 URL query 同步更新
- [ ] E2E 验证规格 R8 例子
  - 验证: `?oemBrand=CAT` → 含 "CAT" 的产品
  - 验证: `?oemNo3=207-60` → 以 "207-60" 开头的产品
- [ ] ILIKE 转义 (P0.1) 防止注入
  - 验证: 字段值含 `%` / `_` / `\` 正确转义

### Task 12: P3.5 对比 UI (23 字段)
- [ ] `AdminCompareView.vue` 6 列布局 (23 字段,与"对比界面"规格严格一致)
  - 验证: 浏览器访问 `/admin/compare?ids=...` 看到 6 列 grid
  - 字段顺序: 图片1 / MR.1 / OEM 2 / OEM 3 / H1-H4 / D1-D4 / D7 / D8 / Media Name / Media Model / remark / QTY / Weight / Length / Wide / Height / Volume
- [ ] 差异高亮 (相同灰底/不同黄底)
  - 验证: 6 产品同一字段全等 → 灰底 #f5f5f5; 不全等 → 黄底 #fffbe6
- [ ] 拖拽**列产品**调顺序 (字段顺序固定,不能拖)
  - 验证: 鼠标拖动列头, 产品列顺序变化
  - 验证: 字段行顺序**不**变 (与"对比界面"规格严格一致)
- [ ] `@media print` 打印优化 CSS
  - 验证: 浏览器 Ctrl+P, 看到无按钮 + A4 横向
- [ ] 6 产品 × 23 字段 = 138 单元格一次性展示
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

### Phase 2 末 ✅
- [x] P2.1 + P2.2 + P2.3 全部完成
- [x] 7 字典 + 抽象层
- [x] Day 10 E2E 仍 10/10 (回归)
- [x] 6 新 E2E 全部 10/10 (P2.2 9/9 + P2.3 5/5)
- [x] typeahead 联动产品表单 7 分区全覆盖
- [x] P2.3 Type 排序 + Machine 4 大类
- [x] Task 8 E2E `_test_type_ordering.py` 5/5

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
| Phase 2 | P2 (3 任务) | ✅ | 3/3 (100%) |
| Phase 3 | P3 (4 任务) | ⏳ | 0/4 (0%) |
| Phase 4 | P4+P5 (3 任务) | ⏳ | 0/3 (0%) |
| **总计** | **15 任务** | **🟡** | **8/15 (53%)** |

> 实时更新: 每次 Task 完成立即勾选 + 更新本表。

---

# Next 任务验证手册 (Day 11 启动用)

> **使用场景**: 启动新 Task 时,翻到本节查"验证命令 + 期望输出 + 失败诊断"。

## 🟢 Task 11 P3.3 前台产品页验证

### 11.1 后端编译 + 启动

```bash
cd d:\projects\sakurafilter\backend\src\SakuraFilter.Api
dotnet build --nologo
# 预期: 0 Error, 19 Warning (与 Phase 2 基线一致)
# 失败: 见"诊断 D1"
```

### 11.2 公开 API 无 token

```bash
curl -i http://localhost:5148/api/public/product/11427622448
# 预期: HTTP/1.1 200 OK + JSON
# 失败: 见"诊断 D2"
```

**期望响应**:
```json
{
  "oem": "11427622448",
  "name1": "Oil Filter",
  "name2": "OF-100",
  "type": "oil",
  "dimensions": { "h1": 100, "d1": 80, "d2": 70 },
  "images": [{ "slot": 1, "url": "https://oss.../oem2/11427622448.jpg" }],
  "machines": [{ "brand": "Caterpillar", "model": "320D" }],
  "xrefs": [{ "oemBrand": "MANN", "oemNo3": "W950" }]
}
```

### 11.3 前台路由

```bash
curl -i http://localhost:5148/product/11427622448
# 预期: HTTP/1.1 200 + HTML (SPA 入口)
# 失败: 见"诊断 D3"
```

### 11.4 SEO meta tags

```bash
# 浏览器 F12 → Elements → <head>
# 预期看到:
# <title>Oil Filter OF-100 MANN 11427622448 - SakuraFilter</title>
# <meta property="og:title" content="...">
# <meta property="og:image" content="https://oss.../oem2/11427622448.jpg">
# <meta property="og:description" content="...">
# <meta property="og:type" content="product">
```

### 11.5 Lighthouse 性能

```bash
npx lighthouse http://localhost:5148/product/11427622448 \
  --only-categories=performance \
  --output=json --output-path=/tmp/lh.json
cat /tmp/lh.json | jq '.categories.performance.score'
# 预期: > 0.9 (> 90 分)
# 失败: 见"诊断 D4"
```

### 11.6 E2E

```bash
cd d:\projects\sakurafilter\spike-test
python _test_public_product.py
# 预期: 2/2 PASS
# 失败: 见"诊断 D5"
```

### 11.7 Playwright 截图

```bash
cd d:\projects\sakurafilter\frontend
npx playwright test tests/visual/public-product.spec.ts
# 预期: 1 passed
# 首次跑会创建 baseline
```

---

## 🟡 Task 12 P3.5 对比 UI 验证

### 12.1 6 列布局

```bash
# 浏览器访问 /admin/compare?ids=1,2,3,4,5,6
# 预期: 6 列 grid 布局
# 失败: 见"诊断 D6"
```

### 12.2 差异高亮

```bash
# 6 产品加入, 同一字段 H1=100/100/100/100/100/100 → 灰底 #f5f5f5
# 6 产品 H1=100/100/100/100/100/105 → 黄底 #fffbe6
# F12 → computed style → background-color 验证
```

### 12.3 拖拽列

```bash
# 鼠标按住列头 drag-handle 拖动 → 列顺序变化
# DevTools → Vue DevTools → products 数组顺序变化
```

### 12.4 打印优化

```bash
# 浏览器 Ctrl+P → 打印预览
# 预期: 无按钮, A4 横向
# 失败: 见"诊断 D7"
```

### 12.5 E2E

```bash
cd d:\projects\sakurafilter\spike-test
python _test_compare.py
# 预期: PASS

cd d:\projects\sakurafilter\frontend
npx playwright test tests/visual/compare.spec.ts
# 预期: 1 passed
```

---

## 🔵 Task 9 P3.1 容差 UI 验证

### 9.1 后端 tolerance 参数

```bash
grep -n "tolerance" d:\projects\sakurafilter\backend\src\SakuraFilter.Api\Controllers\AdminSearchController.cs
# 预期: 看到 Search(int? tolerance, ...)
# 缺失: Day 8.4 未实现,需先补
```

### 9.2 前端下拉

```bash
# 浏览器访问 /admin/search → 顶部看到 el-select
# 选项: ±1mm / ±5mm / ±10mm
# 默认: 5mm
```

### 9.3 DevTools Network

```bash
# 切换到 ±1mm → 触发搜索 → DevTools Network 看到:
# /api/products/search?tolerance=1&h1=100&h2=200&...
```

### 9.4 E2E

```bash
cd d:\projects\sakurafilter\spike-test
python _test_tolerance_ui.py
# 预期: 3/3 PASS
# Case 1: 1mm → 3 条
# Case 2: 5mm → 20 条
# Case 3: 10mm → 50+ 条
```

---

## 🟣 Task 10 P3.2 Excel 粘贴验证

### 10.1 批量 API

```bash
curl -X POST http://localhost:5148/api/search/batch-oem \
  -H "Content-Type: application/json" \
  -d '{"oems":["11427622448","11427622449"]}'
# 预期: 200 + 2 行结果
# 失败: 见"诊断 D8"
```

**期望响应**:
```json
[
  { "oem": "11427622448", "found": true, "productId": 123, "oemBrand": "MANN" },
  { "oem": "11427622449", "found": false, "productId": null, "oemBrand": null }
]
```

### 10.2 边界 - 中文

```bash
curl -X POST http://localhost:5148/api/search/batch-oem \
  -H "Content-Type: application/json" \
  -d '{"oems":["滤清器 1142"]}'
# 预期: 200 + 1 行结果 (单元素正确解析)
```

### 10.3 边界 - 斜杠

```bash
curl -X POST http://localhost:5148/api/search/batch-oem \
  -H "Content-Type: application/json" \
  -d '{"oems":["AB/CD/123"]}'
# 预期: 200 + 1 行 (斜杠不分割)
```

### 10.4 边界 - 引号

```bash
curl -X POST http://localhost:5148/api/search/batch-oem \
  -H "Content-Type: application/json" \
  -d '{"oems":["\"OEN-123\""]}'
# 预期: 200 + 1 行 (引号保留)
```

### 10.5 性能

```bash
# 100 个 OEM 必须 < 1s
time curl -X POST http://localhost:5148/api/search/batch-oem \
  -H "Content-Type: application/json" \
  -d @spike-test/_test_batch_oem_data.json
# 预期: real < 1s
```

### 10.6 E2E

```bash
cd d:\projects\sakurafilter\spike-test
python _test_batch_oem.py
# 预期: PASS + 耗时 < 1s
```

---

## 🔴 Task 13 P4.1 E2E 全量验证

### 13.1 字典 E2E 全集

```bash
ls spike-test/_test_dict_*.py
# 预期: 7 个 (oem-brand, product-name1, product-name2, type, oem-no3, media, machine, engine)
# 注: oem-brand 是 _test_day10_oem_brands.py, 但已包含在矩阵中
```

### 13.2 跑全部

```bash
cd d:\projects\sakurafilter\spike-test
for f in _test_*.py; do
  echo "=== $f ==="
  python "$f" 2>&1 | tail -1
done
# 预期: 全部 PASS, 总耗时 < 5min (本地)
```

### 13.3 CI 总耗时

```bash
# 触发 push → 看 GitHub Actions 总耗时
gh run list --workflow=e2e --limit=1
gh run view <run-id> --json jobs --jq '.jobs[] | "\(.name): \(.conclusion) (\(.startedAt) -> \(.completedAt))"'
# 预期: 总耗时 < 10min
```

### 13.4 失败 → 红叉

```bash
# 故意改坏一个 E2E → push → workflow 红叉 + 不能 merge
echo "assert False, '故意失败'" >> spike-test/_test_dict_type.py
git add -A && git commit -m "test: 故意失败" && git push
# 预期: CI 红叉
# 还原: git revert HEAD && git push
```

---

## 🟠 Task 14 P4.2+4.3 契约 + 视觉验证

### 14.1 字典 schema API

```bash
curl -H "X-Admin-Token: $TOKEN" \
  http://localhost:5148/api/admin/dict/_schema | jq .
# 预期: 8 个字典 (OemBrand + 7 新字典)
# 失败: 见"诊断 D9"
```

**期望响应**:
```json
{
  "DictOemBrand": { "Id": "Int64", "Brand": "String", "SortOrder": "Int32", ... },
  "DictProductName1": { ... },
  ...
}
```

### 14.2 前端契约测试

```bash
cd d:\projects\sakurafilter\frontend
npm run test:contract
# 预期: PASS
# 失败: 见"诊断 D10"
```

### 14.3 视觉回归 baseline

```bash
cd d:\projects\sakurafilter\frontend
npx playwright test tests/visual/dict-pages.spec.ts
# 预期: 8 passed (8 个字典管理页)
# 首次跑会创建 baseline
ls tests/visual/baselines/dict-*.png
# 预期: 8 个 png 文件
```

### 14.4 像素 diff

```bash
# 故意改 AdminTypeView.vue 样式 → 重跑 → 失败
# 还原样式 → 重跑 → 通过
# 失败: 见"诊断 D11"
```

---

## 🟤 Task 15 P5 打磨验证

### 15.1 Volume 自动计算

```bash
# 浏览器访问 /admin/product/new
# 输入 L=300, W=200, H=150 → Volume 字段自动显示 0.009
# 改 H=200 → Volume 自动更新为 0.012
# 失败: 见"诊断 D12"
```

### 15.2 字段 popover

```bash
# 浏览器悬停字段 `?` 图标 → 看到说明
# DevTools Network → /api/admin/dict/field-help/h1 → 200
# 失败: 见"诊断 D13"
```

### 15.3 主题切换

```bash
# 浏览器点击太阳/月亮图标 → 全部组件切换深色
# F12 → Application → Local Storage → theme = "dark"
# F5 刷新 → 仍深色
# 失败: 见"诊断 D14"
```

### 15.4 帮助页

```bash
# 浏览器访问 /admin/help
# 预期看到 5 个模块: 快速开始 / 字典规范 / 批量导入 / 容差建议 / FAQ
# Markdown 正确渲染 (标题/列表/链接)
```

### 15.5 E2E

```bash
cd d:\projects\sakurafilter\spike-test
python _test_p5_volume.py
python _test_p5_theme.py
# 预期: 全 PASS
```

---

# 失败诊断手册 (按错误信息定位)

> **使用场景**: 跑 E2E 或手动验证出现错误时,翻到本节查根因。

| 错误码 / 现象 | 根因 | 排查命令 | 修复 |
|--------------|------|---------|------|
| **D1** `dotnet build` 0 Error → 19+ Warning | 新加代码引用未 using | `dotnet build --nologo /v:n` 看具体 warning | 补 using 或修类型 |
| **D2** `curl /api/public/...` 返回 401 | 缺 `[AllowAnonymous]` 特性 | `grep -n "AllowAnonymous" backend/.../PublicProductController.cs` | 加 `[AllowAnonymous]` |
| **D3** `/product/:oem` 返回 404 | 路由未配置 | `grep -n "PublicProductView" frontend/src/router/index.ts` | 加路由配置 |
| **D4** Lighthouse 分数 < 90 | 图片未懒加载 / JS 过大 | `cat /tmp/lh.json \| jq '.audits["largest-contentful-paint"]'` | 加 `<img loading="lazy">` |
| **D5** `_test_public_product.py` 失败 | 后端 404 或 500 | `dotnet run` 控制台日志 | 查 stacktrace |
| **D6** 6 列布局变 1 列 | CSS grid 写错 | `curl localhost:5173 \| grep "grid-cols-7"` | 改 `grid-cols-7` |
| **D7** 打印仍显示按钮 | `@media print` CSS 未覆盖 | `Ctrl+P → More settings → Background graphics` | 改 CSS `.el-button { display: none }` |
| **D8** `POST /api/search/batch-oem` 400 | 参数校验失败 | `curl -v` 看响应 | 检查 `oems` 字段名 |
| **D9** `/api/admin/dict/_schema` 500 | 反射失败 | `dotnet run` 日志 | 检查 `typeof(DictOemBrand)` 类名 |
| **D10** `npm run test:contract` 失败 | 前后端字段不一致 | `cat contract-test-output` | 同步 TS interface |
| **D11** pixel diff > 5% | 样式改了未更新 baseline | `ls tests/visual/baselines/` | 重新生成 baseline |
| **D12** Volume 字段不更新 | watch 未注册 | `grep -A 3 "watch(" AdminProductFormView.vue` | 补 watch |
| **D13** popover 不显示 | el-popover 触发器错 | F12 → Console | 改 `trigger="hover"` |
| **D14** 主题不持久化 | localStorage 未保存 | F12 → Application | 改 `localStorage.setItem` |

---

# Session 启动检查 (粘贴到 IDE 终端)

> 复制本节到终端,5 分钟确认环境健康。

```bash
# 1. 备份
cd d:\projects\sakurafilter
git status

# 2. 数据库
psql -h localhost -U postgres -d spike_test_v3 -c "SELECT 1"

# 3. 后端编译
cd backend/src/SakuraFilter.Api
dotnet build --nologo

# 4. 启动后端 (新窗口)
dotnet run --urls "http://localhost:5148"

# 5. E2E 回归 (P2 必须仍绿)
cd d:\projects\sakurafilter\spike-test
python _test_day10_oem_brands.py
python _test_p22_seven_dicts.py
python _test_type_ordering.py

# 6. 完整诊断 (出问题时跑)
curl -i http://localhost:5148/api/public/product/11427622448
curl -i -H "X-Admin-Token: $TOKEN" http://localhost:5148/api/admin/dict/_schema
```

**全绿 → 开始新 Task;任一红 → 修完再继续。**
