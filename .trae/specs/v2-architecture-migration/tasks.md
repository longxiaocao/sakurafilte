# Tasks — V2 架构迁移与 5 项客户需求落地

> 任务依赖关系见末尾"Task Dependencies"。
> 每个任务标注:所属 Phase、依赖任务、可并行标记。
> **执行顺序严格按 Phase 0 → Phase 1 → ... → Phase 4**,Phase 内可并行。

---

## Phase 0: 架构前置改造(MR.1 主键化)

- [ ] **Task 0.1**: 数据库迁移脚本 — MR.1 主键化与字段补充
  - [ ] 0.1.1: 创建 EF Core 迁移 `AddMr1PrimaryKeyAndV2Fields`,内容:
    - products 表: 加 `chk_mr_1_format` CHECK 约束(1-10 位字母+数字) + 加 `idx_products_mr_1_unique` 部分唯一索引
    - products 表: 加 `d1_mm_raw` / `d2_mm_raw` / `h1_mm_raw` / `d3_mm_raw` / `d4_mm_raw` / `h2_mm_raw` / `h3_mm_raw` / `h4_mm_raw` 8 个 text 列(尺寸原始字符串)
    - cross_references 表: 加 `sort_order int DEFAULT 0` + `machine_type varchar(50) DEFAULT 'others'` + `is_published boolean DEFAULT true`
    - cross_references 表: 加 `idx_xrefs_brand_oem3_sort` 索引 + `uq_xrefs_brand_oem3` 唯一约束
    - product_images 表: 加 `oem_no_3 varchar(200)` + `image_role varchar(20) DEFAULT 'detail'` + 两个唯一索引
    - machine_applications 表: 加 `machine_category varchar(50) DEFAULT 'others'` + 索引
    - 创建 `partition6_placeholder` 空表
    - system_settings 表: 插入 7 项新配置
  - [ ] 0.1.2: 编写一次性 SQL 脚本 `backend/migrations/018_v2_legacy_data_cleanup.sql`(TRUNCATE 所有业务表,保留字典/用户/系统配置),脚本头注释标注"一次性脚本,不可重跑"
  - [ ] 0.1.3: 本地执行迁移 + 清空脚本,验证表结构与配置项
  - **验证**: `dotnet ef migrations script --idempotent` 无报错;`psql \d products` 显示新索引与约束

- [ ] **Task 0.2**: 实体与 EF Core 配置更新
  - [ ] 0.2.1: `SakuraFilter.Core/Entities/Product.cs` — Product 类加 `D1MmRaw` / `D2MmRaw` / `H1MmRaw` 等 8 个 string 属性(列名 `d1_mm_raw` 等)
  - [ ] 0.2.2: `CrossReference` 类加 `SortOrder` / `MachineType` / `IsPublished` 属性
  - [ ] 0.2.3: `ProductImage` 类加 `OemNo3` / `ImageRole` 属性
  - [ ] 0.2.4: `MachineApplication` 类加 `MachineCategory` 属性
  - [ ] 0.2.5: `SakuraFilter.Infrastructure/Data/Configurations/` 下对应配置类加字段约束(`HasMaxLength` / `HasDefaultValue` / `IsConcurrencyToken` 等)
  - [ ] 0.2.6: `ProductDbContext.OnModelCreating` 加 `idx_products_mr_1_unique` 索引声明 + `chk_mr_1_format` CHECK 约束
  - **验证**: `dotnet build` 通过;ModelSnapshot 与迁移一致

- [ ] **Task 0.3**: AdminProductService 校验逻辑改造
  - [ ] 0.3.1: `ValidateForm` 方法加 MR.1 格式校验(正则 `^[A-Za-z0-9]{1,10}$`),不通过抛 `MR1_FORMAT_INVALID`
  - [ ] 0.3.2: `CreateAsync` 方法加 MR.1 唯一性校验,冲突抛 `MR1_ALREADY_EXISTS`
  - [ ] 0.3.3: `UpdateAsync` 方法适配 xmin 乐观锁,409 错误码新增 `MR1_ALREADY_EXISTS`
  - **验证**: 单元测试 `Mr1_ValidateFormat_*` / `Mr1_Create_Duplicate` 通过

- [ ] **Task 0.4**: Meilisearch 索引结构重构
  - [ ] 0.4.1: `MeiliSearchProvider.cs` — 索引主键从 `oem_no_normalized` 改为 `mr_1`
  - [ ] 0.4.2: 新增 `BuildMr1DocumentAsync` 方法,将 products + cross_references + machine_applications + product_images 聚合为嵌套 JSON 文档(结构见 spec.md)
  - [ ] 0.4.3: 配置索引 `searchableAttributes` / `filterableAttributes` / `sortableAttributes` / `highlightPreTag` / `highlightPostTag` / `typoTolerance`
  - [ ] 0.4.4: 配置 `separatorTokens`(弱中文分词支持)
  - [ ] 0.4.5: `PostgresSearchProvider.cs` 适配 MR.1 主键查询
  - [ ] 0.4.6: `ResilientSearchProvider.cs` IndexAsync 改为 MR.1 文档级别(任一 OEM 3 变更触发整 MR.1 文档重建)
  - **验证**: 单元测试 `Meili_BuildMr1Document` / `PG_Fallback_Mr1` 通过

---

## Phase 1: 需求 1(MR.1 长度) + 需求 5(聚合搜索)

- [ ] **Task 1.1**: 前端 MR.1 输入校验(需求 1)
  - [ ] 1.1.1: `AdminProductFormView.vue` 分区 1 的 MR.1 输入框加 `maxlength="10"` + `pattern="[A-Za-z0-9]{1,10}"` + el-form-item rules
  - [ ] 1.1.2: 提示文案"1-10 位字母+数字"
  - **验证**: 前端单元测试 `ProductForm_Mr1_Validation` 通过
  - **依赖**: Task 0.3

- [ ] **Task 1.2**: 聚合搜索后端端点(需求 5)
  - [ ] 1.2.1: `SakuraFilter.Api/Controllers/PublicSearchController.cs` 加 `POST /api/public/search/aggregate` 端点
  - [ ] 1.2.2: 请求 DTO `AggregateSearchRequest`(q / page / pageSize / tolerance / includeDiscontinued)
  - [ ] 1.2.3: 响应 DTO `AggregateSearchResponse`(total / hits / processingTimeMs),hit 含 `_formatted` 高亮字段
  - [ ] 1.2.4: `MeiliSearchProvider.SearchAsync` 启用 `AttributesToHighlight` + `ShowRankingScore` + `HighlightPreTag="<mark>"` + `HighlightPostTag="</mark>"`
  - [ ] 1.2.5: `PostgresSearchProvider` 加聚合搜索兜底实现(ILIKE 多字段 OR 查询 + ORDER BY brand, sort_order)
  - **验证**: 单元测试 `Search_Aggregate_Highlight` / `Search_Aggregate_TypoTolerance` / `Search_Fallback_Pg` 通过
  - **依赖**: Task 0.4

- [ ] **Task 1.3**: 聚合搜索前端 UI(需求 5)
  - [ ] 1.3.1: `AppHeader.vue` 加全局单框搜索框(顶部导航栏)
  - [ ] 1.3.2: 新增 `AggregateSearchView.vue` 展示搜索结果列表,`v-html` 渲染 `_formatted` 高亮字段(XSS 白名单只允许 `<mark>`)
  - [ ] 1.3.3: 500ms 防抖 + AbortController 取消前序请求(复用 `withAbort.ts`)
  - [ ] 1.3.4: `frontend/src/api/index.ts` 加 `publicSearchApi.aggregate(req, { signal })`
  - [ ] 1.3.5: `router/index.ts` 加 `/search/aggregate?q=` 路由
  - [ ] 1.3.6: 8 字段高级筛选改为折叠展开(`AdvancedFilterPanel.vue`),与聚合搜索共用结果列表
  - **验证**: 前端单元测试 `Search_Aggregate_HighlightRender` / `Search_Aggregate_Debounce` 通过
  - **依赖**: Task 1.2

---

## Phase 2: 需求 2(OEM 3 优先展示)

- [ ] **Task 2.1**: OEM 3 排序管理后端端点
  - [ ] 2.1.1: `SakuraFilter.Api/Endpoints/AdminXrefReorderEndpoints.cs` 新增,路由组 `/api/admin/xrefs/reorder`
  - [ ] 2.1.2: `GET /api/admin/xrefs/reorder/brands` — 返回 Brand 列表(brand / sortOrder / oem3Count)
  - [ ] 2.1.3: `GET /api/admin/xrefs/reorder?oemBrand=BOSCH` — 返回某 Brand 下 OEM 3 列表(oemNo3 / sortOrder / mr1 / isPublished)
  - [ ] 2.1.4: `POST /api/admin/xrefs/reorder` — 批量更新 sort_order,body `{oemBrand, items: [{oemNo3, sortOrder}]}`
  - [ ] 2.1.5: 单个 OEM 3 sort_order 更新用乐观锁(xmin),批量用事务
  - **验证**: 单元测试 `Oem3_Reorder_BrandGrouping` 通过
  - **依赖**: Task 0.1, Task 0.2

- [ ] **Task 2.2**: OEM 3 排序管理前端页面
  - [ ] 2.2.1: 新增 `AdminXrefReorderView.vue`,布局: 左侧 Brand 列表 + 右侧 OEM 3 拖拽排序
  - [ ] 2.2.2: 使用 `vuedraggable` 库(若未引入,加入 package.json)
  - [ ] 2.2.3: 拖拽完成自动调 API 保存
  - [ ] 2.2.4: `router/index.ts` 加 `/admin/xrefs/reorder` 路由,`requireAuth: true`
  - [ ] 2.2.5: `AppHeader.vue` 后台菜单加"OEM 排序管理"入口
  - [ ] 2.2.6: `api/index.ts` 加 `adminXrefApi.listBrands` / `listByBrand` / `reorder`
  - **验证**: 前端单元测试 `XrefReorder_DragDrop` 通过
  - **依赖**: Task 2.1

- [ ] **Task 2.3**: 前台搜索/详情页 OEM 3 排序逻辑
  - [ ] 2.3.1: `MeiliSearchProvider.BuildMr1DocumentAsync` 中 `oem_list` 数组按 `oem_brand.sort_order → oem_no_3.sort_order` 排序后入索引
  - [ ] 2.3.2: `PublicSearchController` 搜索结果返回 `oemList` 已排序
  - [ ] 2.3.3: `PublicProductController` 详情页"同 MR.1 其他 OEM 3"区块 API `/api/public/products/{mr1}/sibling-oem3` 返回排序后列表
  - [ ] 2.3.4: 前端展示层使用后端返回的已排序 `oemList`,不再前端排序
  - **验证**: 单元测试 `Oem3_Search_OrderByBrandThenOem3` 通过
  - **依赖**: Task 0.4, Task 2.1

---

## Phase 3: 需求 4(图片命名可配置 + 主图/详情图分层)

- [ ] **Task 3.1**: AdminProductImageService.BuildKey 改造
  - [ ] 3.1.1: `BuildKey` 方法改为 `BuildKeyAsync`,读 system_settings 配置决定命名字段
  - [ ] 3.1.2: 主图 key 格式 `products/primary/{namingValue}/{namingValue}-1.{ext}`
  - [ ] 3.1.3: 详情图 key 格式 `products/detail/{namingValue}/{namingValue}-{slot}.{ext}`
  - [ ] 3.1.4: 加缓存(`IMemoryCache` 5 分钟),避免每次上传查 DB
  - **验证**: 单元测试 `Image_BuildKey_Primary_OemNo3` / `Image_BuildKey_Detail_Mr1` / `Image_BuildKey_ConfigSwitch` 通过
  - **依赖**: Task 0.1, Task 0.2

- [ ] **Task 3.2**: 图片上传端点分层改造
  - [ ] 3.2.1: `AdminProductEndpoints.cs` 图片上传端点改为两个:
    - `POST /api/admin/products/{mr1}/images/primary?oemNo3=...`
    - `POST /api/admin/products/{mr1}/images/detail?slot=2`
  - [ ] 3.2.2: 主图校验: 同 OEM 3 仅 1 张主图(`uq_product_images_primary` 约束),冲突返回 `IMAGE_PRIMARY_DUPLICATE`
  - [ ] 3.2.3: 详情图校验: 同 MR.1 slot 唯一(`uq_product_images_detail_slot` 约束),冲突返回 `IMAGE_DETAIL_SLOT_DUPLICATE`
  - [ ] 3.2.4: 删除旧的单端点 `POST /api/admin/products/{id}/images/{slot}`
  - **验证**: 单元测试 `Image_Upload_Primary_Duplicate` / `Image_Upload_Detail_Slot_Duplicate` 通过
  - **依赖**: Task 3.1

- [ ] **Task 3.3**: 后台产品表单图片上传 UI 改造
  - [ ] 3.3.1: `AdminProductFormView.vue` 分区 4 拆为两组:
    - 主图区: 下拉选 OEM 3(从已保存的 oemList) + 上传 1 张主图 + 预览/删除
    - 详情图区: 上传 slot 2-6 详情图 + 预览/删除(MR.1 共享)
  - [ ] 3.3.2: `api/index.ts` 加 `imageApi.uploadPrimary(mr1, oemNo3, file)` / `imageApi.uploadDetail(mr1, slot, file)`
  - [ ] 3.3.3: `api/types.ts` 加 `ProductImage` 类型含 `oemNo3` / `imageRole` 字段
  - **验证**: 前端单元测试 `ProductForm_ImageUpload_Primary` / `ProductForm_ImageUpload_Detail` 通过
  - **依赖**: Task 3.2

---

## Phase 4: 需求 3(SEO URL + Razor SSR + sitemap)

- [ ] **Task 4.1**: Razor Pages 详情页 SSR
  - [ ] 4.1.1: `SakuraFilter.Api/Pages/Products/Detail.cshtml.cs` — `PageModel` 加 `OnGetAsync(string pn1, string pn2, string brand, string oem3)`,查 DB 拼模型
  - [ ] 4.1.2: `Detail.cshtml` — 服务端渲染 `<h1>` / 参数表格 / 适配机型列表 / 同 MR.1 OEM 3 推荐 / canonical / OG meta
  - [ ] 4.1.3: Vue 局部 hydration 挂载点 `<div id="vue-gallery" data-mr1="..." data-oem3="...">`
  - [ ] 4.1.4: 新增 `SakuraFilter.Api/wwwroot/js/product-detail-hydration.js` — Vue createApp 挂载图片画廊/对比按钮/询盘表单
  - [ ] 4.1.5: `Program.cs` 加 `services.AddRazorPages()` + `app.MapRazorPages()`
  - **验证**: 单元测试 `Razor_DetailPage_Renders` 校验 HTML 含 `<h1>` + canonical;E2E `Public_ProductDetail_SeoMeta` 通过
  - **依赖**: Task 0.4, Task 2.3

- [ ] **Task 4.2**: 旧 URL 301 重定向
  - [ ] 4.2.1: `SakuraFilter.Api/Controllers/PublicProductController.cs` 旧路由 `[Route("/product/{oem}")]` 改为返回 301
  - [ ] 4.2.2: 重定向逻辑: 查 DB 拿到该 OEM 对应的 pn1/pn2/brand/oem3 → 拼 SEO URL → `RedirectPermanent`
  - [ ] 4.2.3: 若 OEM 找不到,返回 404(不再创建新页)
  - [ ] 4.2.4: `system_settings` 配置 `seo.url_legacy_redirect_enabled` 控制开关
  - **验证**: E2E `Public_ProductDetail_LegacyRedirect` 通过
  - **依赖**: Task 4.1

- [ ] **Task 4.3**: sitemap.xml 生成
  - [ ] 4.3.1: `SakuraFilter.Api/Endpoints/SitemapEndpoints.cs` 新增,`GET /sitemap.xml` 返回索引
  - [ ] 4.3.2: `GET /sitemaps/products-{shard}.xml` 返回分片(每分片 ≤ 50000 URL,按 mr_1 hash 分片)
  - [ ] 4.3.3: 查询 `cross_references WHERE is_published = true AND is_discontinued = false`,关联 products 拼接 URL
  - [ ] 4.3.4: 加内存缓存(IMemoryCache 1 小时),避免每次查询 DB
  - [ ] 4.3.5: `system_settings` 配置 `seo.sitemap_shard_size` 控制分片大小
  - **验证**: 单元测试 `Sitemap_Index_Renders` / `Sitemap_Shard_UrlCount` 通过
  - **依赖**: Task 4.1

- [ ] **Task 4.4**: 前端路由 SEO 适配
  - [ ] 4.4.1: `router/index.ts` 移除 `/product/:oem` 路由(交由 Razor 处理),改为外部链接模式
  - [ ] 4.4.2: 公开搜索结果列表中产品详情链接改用 SEO URL `/products/{pn1}/{pn2}/{brand}/{oem3}`
  - [ ] 4.4.3: `PublicCompareView.vue` 中产品链接同步改造
  - [ ] 4.4.4: 删除 `PublicProductView.vue` 中纯 SPA 渲染逻辑(保留 Vue 局部组件供 hydration 使用)
  - **验证**: E2E `Public_ProductDetail_SeoUrl` 通过
  - **依赖**: Task 4.1

---

## Phase 5: ETL 适配 + 新模拟数据 + 测试

- [ ] **Task 5.1**: ETL 导入适配 V2 主键
  - [ ] 5.1.1: `EtlImportService.ImportProductsAsync` — 解析 `mr_1` 字段,校验格式(1-10 位字母+数字),冲突按 mode 处理
  - [ ] 5.1.2: `oem_no_normalized` 从 `mr_1` 派生(临时方案,`mr_1` 转大写 + 去特殊字符)
  - [ ] 5.1.3: `ImportXrefsAsync` — 解析 `sort_order` / `machine_type` / `is_published` 字段
  - [ ] 5.1.4: xrefs 关联 `mr_1` 而非 `oem_no_2`(找不着时 `IncrSkippedMissingMr1`)
  - [ ] 5.1.5: `ImportAppsAsync` — 解析 `machine_category` 字段
  - [ ] 5.1.6: COPY 列定义新增所有 V2 字段
  - **验证**: E2E ETL 导入 100 MR.1 / 300 OEM 3 / 500 机型,状态 completed
  - **依赖**: Task 0.1, Task 0.2

- [ ] **Task 5.2**: 新模拟数据生成脚本
  - [ ] 5.2.1: `spike-test/_gen_v2_mock_data.py` 生成 jsonl 文件:
    - `mock_products_v2.jsonl`(100 行,MR000001~MR000100)
    - `mock_xrefs_v2.jsonl`(300 行,每 MR.1 对应 2-5 个 OEM 3)
    - `mock_apps_v2.jsonl`(500 行,每 MR.1 对应 3-8 个机型)
  - [ ] 5.2.2: 图片占位生成: 300 张主图(key `products/primary/{oem3}/{oem3}-1.png`)+ 200 张详情图(key `products/detail/{mr1}/{mr1}-{slot}.png`),使用 MinIO SDK 上传
  - [ ] 5.2.3: 数据关系验证: MR.1 一对多 OEM 3、OEM 3 一对一主图、MR.1 一对多详情图、machine_category 覆盖 5 类
  - **验证**: 脚本执行后 Meilisearch 索引 100 文档,搜索 "BOSCH" 返回预期结果
  - **依赖**: Task 5.1

- [ ] **Task 5.3**: 全量测试套件更新
  - [ ] 5.3.1: 后端单元测试: 上述所有 `*_Validate*` / `*_Duplicate` / `*_Search_*` / `*_BuildKey_*` 用例
  - [ ] 5.3.2: 前端单元测试: `ProductForm_Mr1_Validation` / `XrefReorder_DragDrop` / `Search_Aggregate_*`
  - [ ] 5.3.3: 契约测试: `dict-schema.test.ts` 更新(若字段有变化)
  - [ ] 5.3.4: E2E 测试: 上述所有 `Public_*` / `Admin_*` 用例
  - [ ] 5.3.5: 视觉回归基线重置: `public-product-seo.spec.ts` / `public-aggregate-search.spec.ts` / `admin-xref-reorder.spec.ts`
  - [ ] 5.3.6: 防回归脚本 `_test_regression.py --scan` 新增 V2 修复点扫描
  - **验证**: `dotnet test` + `npm run test:contract` + `npm run test:visual` 全绿
  - **依赖**: Task 5.2, 所有前序任务

---

## Task Dependencies

- **Phase 0 内部**: Task 0.1 → Task 0.2 → (Task 0.3 ∥ Task 0.4) 并行
- **Phase 1**: Task 0.3 → Task 1.1;Task 0.4 → Task 1.2 → Task 1.3
- **Phase 2**: (Task 0.1 + Task 0.2) → Task 2.1 → Task 2.2;(Task 0.4 + Task 2.1) → Task 2.3
- **Phase 3**: (Task 0.1 + Task 0.2) → Task 3.1 → Task 3.2 → Task 3.3
- **Phase 4**: (Task 0.4 + Task 2.3) → Task 4.1 → (Task 4.2 ∥ Task 4.3 ∥ Task 4.4) 并行
- **Phase 5**: Task 5.1 依赖 Phase 0;Task 5.2 依赖 Task 5.1;Task 5.3 依赖所有前序

**可并行任务**:
- Phase 0: Task 0.3 ∥ Task 0.4
- Phase 1: Task 1.1 ∥ Task 1.2(分别依赖 0.3/0.4)
- Phase 2: Task 2.2 ∥ Task 2.3(分别依赖 2.1)
- Phase 3: 整 Phase 可与 Phase 2 并行(均依赖 Phase 0)
- Phase 4: Task 4.2 ∥ Task 4.3 ∥ Task 4.4(均依赖 4.1)

**关键路径**: Task 0.1 → 0.2 → 0.4 → 4.1 → 5.3(MR.1 主键化 → Meilisearch 重构 → Razor SSR → 全量测试)
