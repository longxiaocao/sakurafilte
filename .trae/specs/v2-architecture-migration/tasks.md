# Tasks — V2 架构迁移与 5 项客户需求落地 (v2 修订版)

> 任务依赖关系见末尾"Task Dependencies"。
> 每个任务标注:所属 Phase、依赖任务、可并行标记。
> **执行顺序严格按 Phase 0 → Phase 1 → ... → Phase 5**,Phase 内可并行。
> **修订历史**: v2 新增 5 个任务(0.5/0.6/0.7/4.5/4.6),修复 47 个漏洞

---

## Phase 0: 架构前置改造(MR.1 主键化 + 部署基础)

- [ ] **Task 0.1**: 数据库迁移脚本 — MR.1 主键化与字段补充(修复漏洞 1-7,9-13,16-20)
  - [ ] 0.1.1: 创建 EF Core 迁移 `AddMr1PrimaryKeyAndV2Fields`,内容:
    - products 表: `ALTER COLUMN mr_1 TYPE varchar(10)` + `chk_mr_1_format` CHECK + `idx_products_mr_1_unique` 部分唯一索引
    - products 表: DROP `ix_products_oem_no_normalized_unique` + 改普通索引 `idx_products_oem_no_normalized` (WHERE NOT NULL) + `ALTER COLUMN oem_no_normalized DROP NOT NULL`
    - products 表: 加 `d1_mm_raw` / `d2_mm_raw` / `h1_mm_raw` / `d3_mm_raw` / `d4_mm_raw` / `h2_mm_raw` / `h3_mm_raw` / `h4_mm_raw` 8 个 text 列
    - products 表: `ALTER COLUMN d1_mm TYPE numeric(10,2)` 等 8 个 numeric 字段精度统一
    - cross_references 表: 加 `oem_2 varchar(100)` / `sort_order int DEFAULT 0` / `machine_type varchar(50) DEFAULT 'others'` / `is_published boolean DEFAULT true`
    - cross_references 表: 加 `chk_xref_machine_type` CHECK 约束(枚举校验)
    - cross_references 表: `ALTER COLUMN oem_no_3 SET NOT NULL` + `ALTER COLUMN oem_brand SET NOT NULL` + `ALTER COLUMN oem_no_3 TYPE varchar(200)` + `ALTER COLUMN oem_brand TYPE varchar(100)` + `ALTER COLUMN product_id SET NOT NULL`
    - cross_references 表: 加 `idx_xrefs_brand_oem3_sort` 索引(WHERE is_discontinued=false AND is_published=true)
    - cross_references 表: 加 `uq_xrefs_brand_oem3` 唯一约束(WHERE is_discontinued=false,允许下架后重新上架)
    - product_images 表: 加 `oem_no_3 varchar(200)` + `image_role varchar(20) DEFAULT 'detail'`
    - product_images 表: **DROP `ix_product_images_product_id_slot_unique` 旧约束**(修复漏洞 1)
    - product_images 表: 加 `chk_image_role` CHECK + `chk_image_role_slot` CHECK
    - product_images 表: 加 `uq_product_images_primary` + `uq_product_images_detail_slot` 部分唯一索引
    - product_images 表: 加 `fk_product_images_product` 外键 ON DELETE CASCADE
    - machine_applications 表: 加 `machine_category varchar(50) DEFAULT 'others'` + `chk_machine_apps_category` CHECK
    - machine_applications 表: 加 `idx_machine_apps_category` 索引
    - 创建 `partition6_placeholder` 空表
    - system_settings 表: 插入 10 项新配置(含 updated_at=now() + ON CONFLICT DO UPDATE)
  - [ ] 0.1.2: 编写一次性 SQL 脚本 `backend/migrations/018_v2_legacy_data_cleanup.sql`(TRUNCATE 所有业务表,保留字典/用户/系统配置),脚本头注释标注"一次性脚本,不可重跑"
  - [ ] 0.1.3: 本地执行迁移 + 清空脚本,验证表结构与配置项
  - **验证**: `dotnet ef migrations script --idempotent` 无报错;`psql \d products` 显示新索引与约束;`psql \d product_images` 确认旧约束已 DROP

- [ ] **Task 0.2**: 实体与 EF Core 配置更新(修复漏洞 8,13)
  - [ ] 0.2.1: `SakuraFilter.Core/Entities/Product.cs` — Product 类加 `D1MmRaw` / `D2MmRaw` / `H1MmRaw` 等 8 个 string 属性(列名 `d1_mm_raw` 等)
  - [ ] 0.2.2: `CrossReference` 类加 `Oem2` / `SortOrder` / `MachineType` / `IsPublished` 属性 + `RowVersion` (IsRowVersion() IsConcurrencyToken())复用 xmin(修复漏洞 13)
  - [ ] 0.2.3: `ProductImage` 类加 `OemNo3` / `ImageRole` 属性
  - [ ] 0.2.4: `MachineApplication` 类加 `MachineCategory` 属性
  - [ ] 0.2.5: **新增** `SakuraFilter.Core/Entities/Partition6Placeholder.cs`(修复漏洞 8)— 仅 Id + CreatedAt 两属性
  - [ ] 0.2.6: `SakuraFilter.Infrastructure/Data/Configurations/` 下对应配置类加字段约束(`HasMaxLength` / `HasDefaultValue` / `IsRequired` / `IsConcurrencyToken` 等)
  - [ ] 0.2.7: `ProductDbContext.OnModelCreating` 加:
    - `idx_products_mr_1_unique` 索引声明 + `chk_mr_1_format` CHECK 约束
    - `modelBuilder.Entity<Partition6Placeholder>().ToTable("partition6_placeholder").HasKey(e => e.Id)`(修复漏洞 8)
    - cross_references 的 RowVersion 配置 `e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken()`
  - **验证**: `dotnet build` 通过;ModelSnapshot 与迁移一致

- [ ] **Task 0.3**: AdminProductService 校验逻辑改造
  - [ ] 0.3.1: `ValidateForm` 方法加 MR.1 格式校验(正则 `^[A-Za-z0-9]{1,10}$`),不通过抛 `MR1_FORMAT_INVALID`
  - [ ] 0.3.2: `ValidateForm` 方法加 MR.1 必填校验,空值抛 `MR1_REQUIRED`(V2 数据强制)
  - [ ] 0.3.3: `CreateAsync` 方法加 MR.1 唯一性校验,冲突抛 `MR1_ALREADY_EXISTS`
  - [ ] 0.3.4: `CreateAsync` 方法加 OEM 3 唯一性校验(同 Brand 下未下架 OEM 3),冲突抛 `OEM3_ALREADY_EXISTS`
  - [ ] 0.3.5: `CreateAsync` 方法加 machine_type 枚举校验,非法值抛 `MACHINE_TYPE_INVALID`
  - [ ] 0.3.6: `UpdateAsync` 方法适配 xmin 乐观锁,409 错误码新增 `MR1_ALREADY_EXISTS`
  - **验证**: 单元测试 `Mr1_ValidateFormat_*` / `Mr1_Create_Duplicate` / `Oem3_Create_DuplicateBrand` 通过

- [ ] **Task 0.4**: Meilisearch 索引结构重构(修复漏洞 1-5,检索逻辑)
  - [ ] 0.4.1: `MeiliSearchProvider.cs` — 索引主键从 `oem_no_normalized` 改为 `mr_1`
  - [ ] 0.4.2: 新增 `BuildMr1DocumentAsync` 方法,将 products + cross_references + machine_applications + product_images 聚合为嵌套 JSON 文档(结构见 spec.md)
  - [ ] 0.4.3: 配置索引 `searchableAttributes`(含 oem_list.oem_brand/oem_no_3/oem_2 + machine_list.*)
  - [ ] 0.4.4: 配置 `filterableAttributes`(补全 is_published/is_discontinued/oem_brand/oem_no_3/oem_2/machine_brand 等,修复漏洞 3)
  - [ ] 0.4.5: 配置 `sortableAttributes`(product_name_1 + oem_list.sort_order + brand_sort_order_min)
  - [ ] 0.4.6: 配置 `highlightPreTag` / `highlightPostTag` / `typoTolerance` / `minWordSizeForTypos` / `separatorTokens` / `stopWords`
  - [ ] 0.4.7: `BuildMr1DocumentAsync` 中预计算 `brand_sort_order_min` 文档级冗余字段(修复漏洞 20)
  - [ ] 0.4.8: `MeiliSearchProvider.SearchAsync` 返回前对 `_formatted` 做 HTML escape(转义后还原 `<mark>` 标签,修复漏洞 4)
  - [ ] 0.4.9: `PostgresSearchProvider.cs` 适配 MR.1 主键查询 + LATERAL JOIN + JSON 聚合(修复漏洞 2)
  - [ ] 0.4.10: `PostgresSearchProvider.cs` 复用 `LikeEscapeExtensions.EscapeLikePattern` + 3 参 ILike(修复漏洞 11)
  - [ ] 0.4.11: `ResilientSearchProvider.cs` IndexAsync 改为 MR.1 文档级别(任一 OEM 3 变更触发整 MR.1 文档重建)
  - **验证**: 单元测试 `Meili_BuildMr1Document` / `PG_Fallback_Mr1` / `Search_Aggregate_XssDefense` 通过

- [ ] **Task 0.5**: ProblemDetailsFactory 错误码统一(修复漏洞 4,前端联动)
  - [ ] 0.5.1: `SakuraFilter.Api/Services/ProblemDetailsFactory.cs` 改造,新增 V2 错误码全部按大写下划线格式(无 ERR_ 前缀)
  - [ ] 0.5.2: 保留旧 `ERR_*` 错误码映射,确保向后兼容
  - [ ] 0.5.3: `appsettings.json` 加 `ErrorCodes:LegacyPrefix: "ERR_"` 配置
  - [ ] 0.5.4: InvalidOperationException → 409 `XREF_CONFLICT`(原 `ERR_CONFLICT`)
  - **验证**: 单元测试 `ProblemDetails_ErrorCode_LegacyCompat` 通过(旧 ERR_* 仍可识别)

- [ ] **Task 0.6**: nginx.conf 路由配置(修复漏洞 1,前端联动)
  - [ ] 0.6.1: `docker/nginx.conf` 新增 location 规则:
    - `location ~ ^/products/` → proxy_pass http://backend:8080
    - `location ~ ^/product/` → proxy_pass http://backend:8080(旧 URL 301)
    - `location ~ ^/(sitemap\.xml|sitemaps/)` → proxy_pass http://backend:8080
    - `location = /robots.txt` → proxy_pass http://backend:8080
  - [ ] 0.6.2: 部署后用 curl 验证 `/products/...` 返回 HTML(非 SPA index.html)
  - **验证**: `curl -I http://localhost/products/oil-filter/spin-on/bosch/F000000001` 返回 200 + Content-Type: text/html

- [ ] **Task 0.7**: Program.cs 改造(修复漏洞 7,8,前端联动)
  - [ ] 0.7.1: `builder.Services.AddRazorPages()` 注册 Razor Pages
  - [ ] 0.7.2: `app.MapRazorPages()` 中间件管道
  - [ ] 0.7.3: `builder.Services.AddRateLimiter` 加 "public" 策略(120/min,基于 RemoteIpAddress)
  - [ ] 0.7.4: `appsettings.json` 的 `ExemptPaths` 移除 `/api/products`(死配置清理)
  - **验证**: `dotnet build` + 启动后 `/products/...` 路由可访问

---

## Phase 1: 需求 1(MR.1 长度) + 需求 5(聚合搜索)

- [ ] **Task 1.1**: 前端 MR.1 输入校验(需求 1)
  - [ ] 1.1.1: `AdminProductFormView.vue` 分区 1 的 MR.1 输入框加 `maxlength="10"` + `pattern="[A-Za-z0-9]{1,10}"` + el-form-item rules
  - [ ] 1.1.2: 提示文案"1-10 位字母+数字"
  - [ ] 1.1.3: 必填校验(空值前端拦截)
  - **验证**: 前端单元测试 `ProductForm_Mr1_Validation` + `ProductForm_Mr1_TooLong` 通过
  - **依赖**: Task 0.3

- [ ] **Task 1.2**: 聚合搜索后端端点(需求 5,修复漏洞 1,2,4,12)
  - [ ] 1.2.1: `SakuraFilter.Api/Controllers/PublicSearchController.cs` 加 `POST /api/public/search/aggregate` 端点
  - [ ] 1.2.2: 请求 DTO `AggregateSearchRequest`(q / page / pageSize / tolerance / includeDiscontinued / machineCategory)
  - [ ] 1.2.3: 响应 DTO `AggregateSearchResponse`(total / page / pageSize / hits / processingTimeMs),hit 含 mr1 + oemList 数组(文档级返回,修复漏洞 1) + `_formatted` 高亮字段 + `_rankingScore`
  - [ ] 1.2.4: 加分页深度校验(page > max_page_depth 抛 `SEARCH_PAGE_TOO_DEEP`,修复漏洞 12)
  - [ ] 1.2.5: `MeiliSearchProvider.SearchAsync` 启用 `AttributesToHighlight` + `ShowRankingScore` + `HighlightPreTag="<mark>"` + `HighlightPostTag="</mark>"`
  - [ ] 1.2.6: `MeiliSearchProvider.SearchAsync` 返回前对 `_formatted` 做 HTML escape(修复漏洞 4)
  - [ ] 1.2.7: `PostgresSearchProvider` 加聚合搜索兜底实现(LATERAL JOIN + JSON 聚合 + ILIKE 多字段 OR + ORDER BY brand, sort_order,修复漏洞 2)
  - **验证**: 单元测试 `Search_Aggregate_Highlight` / `Search_Aggregate_TypoTolerance` / `Search_Aggregate_XssDefense` / `Search_Aggregate_PageTooDeep` / `Search_Fallback_Pg` 通过
  - **依赖**: Task 0.4

- [ ] **Task 1.3**: 聚合搜索前端 UI(需求 5,修复漏洞 4)
  - [ ] 1.3.1: `AppHeader.vue` 加全局单框搜索框(顶部导航栏)
  - [ ] 1.3.2: 新增 `AggregateSearchView.vue` 展示搜索结果列表,MR.1 文档级显示 + 可展开 oemList
  - [ ] 1.3.3: `v-html` 渲染 `_formatted` 高亮字段 + DOMPurify 白名单只允许 `<mark>`(双保险,修复漏洞 4)
  - [ ] 1.3.4: 新增 `frontend/src/utils/html-sanitizer.ts` 封装 DOMPurify
  - [ ] 1.3.5: 500ms 防抖 + AbortController 取消前序请求(复用 `withAbort.ts`)
  - [ ] 1.3.6: `frontend/src/api/index.ts` 加 `publicSearchApi.aggregate(req, { signal })`
  - [ ] 1.3.7: `router/index.ts` 加 `/search/aggregate?q=` 路由
  - [ ] 1.3.8: 8 字段高级筛选改为折叠展开(`AdvancedFilterPanel.vue`),与聚合搜索共用结果列表
  - **验证**: 前端单元测试 `Search_Aggregate_HighlightRender` / `Search_Aggregate_XssDefense` / `Search_Aggregate_Debounce` 通过
  - **依赖**: Task 1.2

---

## Phase 2: 需求 2(OEM 3 优先展示)

- [ ] **Task 2.1**: OEM 3 排序管理后端端点(修复漏洞 13)
  - [ ] 2.1.1: `SakuraFilter.Api/Endpoints/AdminXrefReorderEndpoints.cs` 新增,路由组 `/api/admin/xrefs/reorder`
  - [ ] 2.1.2: `GET /api/admin/xrefs/reorder/brands` — 返回 Brand 列表(brand / sortOrder / oem3Count)
  - [ ] 2.1.3: `GET /api/admin/xrefs/reorder?oemBrand=BOSCH` — 返回某 Brand 下 OEM 3 列表(oemNo3 / sortOrder / mr1 / isPublished / rowVersion)
  - [ ] 2.1.4: `POST /api/admin/xrefs/reorder` — 批量更新 sort_order,body `{oemBrand, items: [{oemNo3, sortOrder, rowVersion}]}`(含 rowVersion 乐观锁,修复漏洞 13)
  - [ ] 2.1.5: 单个 OEM 3 sort_order 更新用乐观锁(xmin),冲突返回 409 `XREF_CONFLICT`
  - [ ] 2.1.6: 批量更新用事务
  - **验证**: 单元测试 `Oem3_Reorder_BrandGrouping` / `Oem3_Reorder_ConcurrencyConflict` 通过
  - **依赖**: Task 0.1, Task 0.2

- [ ] **Task 2.2**: OEM 3 排序管理前端页面
  - [ ] 2.2.1: 新增 `AdminXrefReorderView.vue`,布局: 左侧 Brand 列表 + 右侧 OEM 3 拖拽排序
  - [ ] 2.2.2: 使用 `vuedraggable` 库(若未引入,加入 package.json)
  - [ ] 2.2.3: 拖拽完成自动调 API 保存(含 rowVersion 透传)
  - [ ] 2.2.4: 409 `XREF_CONFLICT` 时提示刷新重试
  - [ ] 2.2.5: `router/index.ts` 加 `/admin/xrefs/reorder` 路由,`requireAuth: true`
  - [ ] 2.2.6: `AppHeader.vue` 后台菜单加"OEM 排序管理"入口
  - [ ] 2.2.7: `api/index.ts` 加 `adminXrefApi.listBrands` / `listByBrand` / `reorder`
  - **验证**: 前端单元测试 `XrefReorder_DragDrop` / `XrefReorder_ConcurrencyConflict` 通过
  - **依赖**: Task 2.1

- [ ] **Task 2.3**: 前台搜索/详情页 OEM 3 排序逻辑
  - [ ] 2.3.1: `MeiliSearchProvider.BuildMr1DocumentAsync` 中 `oem_list` 数组按 `oem_brand.sort_order → oem_no_3.sort_order` 排序后入索引
  - [ ] 2.3.2: `BuildMr1DocumentAsync` 中预计算 `brand_sort_order_min` 文档级冗余字段
  - [ ] 2.3.3: `PublicSearchController` 搜索结果返回 `oemList` 已排序
  - [ ] 2.3.4: `PublicProductController` 详情页"同 MR.1 其他 OEM 3"区块 API `/api/public/products/{mr1}/sibling-oem3` 返回排序后列表
  - [ ] 2.3.5: 前端展示层使用后端返回的已排序 `oemList`,不再前端排序
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

- [ ] **Task 3.2**: 图片上传端点分层改造(修复漏洞 5,前端联动)
  - [ ] 3.2.1: `AdminProductImageService.UploadAsync` 签名改造为 `(string mr1, string imageRole, string? oemNo3, short slot, Stream stream, string contentType, CancellationToken ct)`(修复漏洞 5)
  - [ ] 3.2.2: 校验 imageRole / slot 一致性(主图 slot=1,详情图 slot=2-6),不一致抛 `IMAGE_ROLE_SLOT_MISMATCH` 或 `IMAGE_DETAIL_SLOT_INVALID`
  - [ ] 3.2.3: 校验 mr_1 存在性(查 products 表)
  - [ ] 3.2.4: 校验 oemNo3 存在性(若 primary,查 cross_references)
  - [ ] 3.2.5: `AdminProductEndpoints.cs` 图片上传端点改为两个:
    - `POST /api/admin/products/{mr1}/images/primary?oemNo3=...`
    - `POST /api/admin/products/{mr1}/images/detail?slot=2`
  - [ ] 3.2.6: 主图校验: 同 OEM 3 仅 1 张主图(`uq_product_images_primary` 约束),冲突返回 `IMAGE_PRIMARY_DUPLICATE`
  - [ ] 3.2.7: 详情图校验: 同 MR.1 slot 唯一(`uq_product_images_detail_slot` 约束),冲突返回 `IMAGE_DETAIL_SLOT_DUPLICATE`
  - [ ] 3.2.8: 删除旧的单端点 `POST /api/admin/products/{id}/images/{slot}`
  - **验证**: 单元测试 `Image_Upload_Primary_Duplicate` / `Image_Upload_Detail_Slot_Duplicate` / `Image_Upload_Detail_Slot_Invalid` / `Image_Upload_Role_Slot_Mismatch` 通过
  - **依赖**: Task 3.1

- [ ] **Task 3.3**: 后台产品表单图片上传 UI 改造
  - [ ] 3.3.1: `AdminProductFormView.vue` 分区 4 拆为两组:
    - 主图区: 下拉选 OEM 3(从已保存的 oemList) + 上传 1 张主图 + 预览/删除
    - 详情图区: 上传 slot 2-6 详情图 + 预览/删除(MR.1 共享)
  - [ ] 3.3.2: 前端校验: 详情图 slot 必须 2-6,主图 slot=1
  - [ ] 3.3.3: `api/index.ts` 加 `imageApi.uploadPrimary(mr1, oemNo3, file)` / `imageApi.uploadDetail(mr1, slot, file)`
  - [ ] 3.3.4: `api/types.ts` 加 `ProductImage` 类型含 `oemNo3` / `imageRole` 字段
  - **验证**: 前端单元测试 `ProductForm_ImageUpload_Primary` / `ProductForm_ImageUpload_Detail` / `ProductForm_ImageUpload_SlotInvalid` 通过
  - **依赖**: Task 3.2

---

## Phase 4: 需求 3(SEO URL + Razor SSR + sitemap)

- [ ] **Task 4.1**: Razor Pages 详情页 SSR(修复漏洞 3,11,14,15)
  - [ ] 4.1.1: `SakuraFilter.Api/Pages/Products/Detail.cshtml.cs` — `PageModel` 加 `OnGetAsync(string pn1, string pn2, string brand, string oem3)`,查 DB 拼模型
  - [ ] 4.1.2: `Detail.cshtml` — 服务端渲染 `<h1>` / 参数表格 / 适配机型列表 / 同 MR.1 OEM 3 推荐 / canonical / OG meta
  - [ ] 4.1.3: Vue client mount 挂载点 `<div id="vue-gallery" data-mr1="..." data-oem3="...">` + `<div id="vue-compare">` + `<div id="vue-inquiry">`
  - [ ] 4.1.4: 新增 `SakuraFilter.Api/wwwroot/js/product-detail-client.js` — Vue createApp 挂载图片画廊/对比按钮/询盘表单(非 hydration 模式,修复漏洞 3)
  - [ ] 4.1.5: 脚本 defer 加载,挂载失败不影响 SEO 内容(渐进增强,修复漏洞 11)
  - [ ] 4.1.6: 加 404 友好页(含站内搜索入口,修复漏洞 15)
  - [ ] 4.1.7: URL slug 含特殊字符做 kebab-case 转换 + URL encode
  - [ ] 4.1.8: `Program.cs` 加 `services.AddRazorPages()` + `app.MapRazorPages()`(在 Task 0.7 完成)
  - **验证**: 单元测试 `Razor_DetailPage_Renders` 校验 HTML 含 `<h1>` + canonical;E2E `Public_ProductDetail_SeoMeta` / `Public_ProductDetail_VueMount` / `Public_ProductDetail_VueMountFailure` / `Public_ProductDetail_404` 通过
  - **依赖**: Task 0.4, Task 0.7, Task 2.3

- [ ] **Task 4.2**: 旧 URL 301 重定向
  - [ ] 4.2.1: `SakuraFilter.Api/Controllers/PublicProductController.cs` 旧路由 `[Route("/product/{oem}")]` 改为返回 301
  - [ ] 4.2.2: 重定向逻辑: 查 DB 拿到该 OEM 对应的 pn1/pn2/brand/oem3 → 拼 SEO URL → `RedirectPermanent`
  - [ ] 4.2.3: 若 OEM 找不到,返回 404(不再创建新页)
  - [ ] 4.2.4: `system_settings` 配置 `seo.url_legacy_redirect_enabled` 控制开关
  - **验证**: E2E `Public_ProductDetail_LegacyRedirect` 通过
  - **依赖**: Task 4.1

- [ ] **Task 4.3**: sitemap.xml 生成(修复漏洞 10)
  - [ ] 4.3.1: `SakuraFilter.Api/Endpoints/SitemapEndpoints.cs` 新增,`GET /sitemap.xml` 返回索引
  - [ ] 4.3.2: `GET /sitemaps/products-{shard}.xml` 返回分片(每分片 ≤ 50000 URL,按 mr_1 hash 分片)
  - [ ] 4.3.3: 查询 `cross_references WHERE is_published = true AND is_discontinued = false`,关联 products 拼接 URL
  - [ ] 4.3.4: 加内存缓存(IMemoryCache 1 小时,键 `sitemap:index` / `sitemap:shard:{shard}`,修复漏洞 10)
  - [ ] 4.3.5: OEM 3 上架/下架/排序变更时主动清除相关 shard 缓存
  - [ ] 4.3.6: `system_settings` 配置 `seo.sitemap_shard_size` / `seo.sitemap_cache_ttl_seconds` 控制
  - **验证**: 单元测试 `Sitemap_Index_Renders` / `Sitemap_Shard_UrlCount` 通过
  - **依赖**: Task 4.1

- [ ] **Task 4.4**: 前端路由 SEO 适配(修复漏洞 2,前端联动)
  - [ ] 4.4.1: `router/index.ts` 移除 `/product/:oem` 路由(交由 Razor 处理)
  - [ ] 4.4.2: 全项目搜索 `router.push('/product/` 与 `to: '/product/`,改为 `window.location.href = '/products/...'` 拼接(修复漏洞 2)
  - [ ] 4.4.3: 公开搜索结果列表中产品详情链接改用 SEO URL `/products/{pn1}/{pn2}/{brand}/{oem3}`
  - [ ] 4.4.4: `PublicCompareView.vue` 中产品链接同步改造
  - [ ] 4.4.5: `AdminProductsView.vue` 中"查看详情"按钮同上
  - [ ] 4.4.6: 删除 `PublicProductView.vue` 中纯 SPA 渲染逻辑(保留 Vue client mount 子组件供 Razor 页面引用)
  - **验证**: E2E `Public_ProductDetail_SeoUrl` 通过;全项目 grep `router.push.*'/product/` 无结果
  - **依赖**: Task 4.1

- [ ] **Task 4.5**: Vue client mount 子组件开发(修复漏洞 3)
  - [ ] 4.5.1: 新增 `frontend/src/components/product/GalleryApp.vue` — 图片画廊(主图 + 详情图轮播)
  - [ ] 4.5.2: 新增 `frontend/src/components/product/CompareApp.vue` — 加入对比按钮 + 跳转对比页
  - [ ] 4.5.3: 新增 `frontend/src/components/product/InquiryApp.vue` — 询盘表单
  - [ ] 4.5.4: 三组件均接受 props(mr1 / oem3),通过 `data-*` 属性传值
  - [ ] 4.5.5: 打包为 `product-detail-client.js`(Vite 多入口配置)
  - **验证**: E2E `Public_ProductDetail_VueMount` 通过
  - **依赖**: Task 4.1

- [ ] **Task 4.6**: CursorHmac 改造(修复漏洞 6,前端联动)
  - [ ] 4.6.1: `SakuraFilter.Api/Services/CursorHmac.cs` — `Sign` 方法签名改为 `Sign(string updatedAtIso, string mr1)`(原为 long id,修复漏洞 6)
  - [ ] 4.6.2: `Verify` 方法同步改造
  - [ ] 4.6.3: 所有调用方更新(PublicSearchController 等)
  - **验证**: 单元测试 `CursorHmac_Sign_Verify_RoundTrip` 通过
  - **依赖**: Task 0.4

---

## Phase 5: ETL 适配 + 新模拟数据 + 测试

- [ ] **Task 5.1**: ETL 导入适配 V2 主键
  - [ ] 5.1.1: `EtlImportService.ImportProductsAsync` — 解析 `mr_1` 字段,校验格式(1-10 位字母+数字),冲突按 mode 处理
  - [ ] 5.1.2: `oem_no_normalized` 从 `mr_1` 派生(临时方案,`mr_1` 转大写 + 去特殊字符)
  - [ ] 5.1.3: `ImportXrefsAsync` — 解析 `sort_order` / `machine_type`(校验枚举) / `is_published` / `oem_2`(必填) 字段
  - [ ] 5.1.4: xrefs 关联 `mr_1` 而非 `oem_no_2`(找不着时 `IncrSkippedMissingMr1`)
  - [ ] 5.1.5: `ImportAppsAsync` — 解析 `machine_category` 字段(校验枚举)
  - [ ] 5.1.6: COPY 列定义新增所有 V2 字段(oem_2/sort_order/machine_type/is_published/machine_category/d1_mm_raw 等)
  - [ ] 5.1.7: machine_type / machine_category 非法值抛 `MACHINE_TYPE_INVALID`
  - **验证**: E2E ETL 导入 100 MR.1 / 300 OEM 3 / 500 机型,状态 completed
  - **依赖**: Task 0.1, Task 0.2

- [ ] **Task 5.2**: 新模拟数据生成脚本
  - [ ] 5.2.1: `spike-test/_gen_v2_mock_data.py` 生成 jsonl 文件:
    - `mock_products_v2.jsonl`(100 行,MR000001~MR000100,含 d1_mm_raw 等原始字符串)
    - `mock_xrefs_v2.jsonl`(300 行,每 MR.1 对应 2-5 个 OEM 3,含 oem_2/sort_order/machine_type/is_published)
    - `mock_apps_v2.jsonl`(500 行,每 MR.1 对应 3-8 个机型,含 machine_category)
  - [ ] 5.2.2: 图片占位生成: 300 张主图(key `products/primary/{oem3}/{oem3}-1.png`)+ 200 张详情图(key `products/detail/{mr1}/{mr1}-{slot}.png`),使用 MinIO SDK 上传
  - [ ] 5.2.3: 数据关系验证: MR.1 一对多 OEM 3、OEM 3 一对一主图、MR.1 一对多详情图、machine_category 覆盖 5 类
  - **验证**: 脚本执行后 Meilisearch 索引 100 文档,搜索 "BOSCH" 返回预期结果
  - **依赖**: Task 5.1

- [ ] **Task 5.3**: 全量测试套件更新
  - [ ] 5.3.1: 后端单元测试: 上述所有 `*_Validate*` / `*_Duplicate` / `*_Search_*` / `*_BuildKey_*` / `*_XssDefense` / `*_ConcurrencyConflict` / `*_PageTooDeep` 用例
  - [ ] 5.3.2: 前端单元测试: `ProductForm_Mr1_Validation` / `XrefReorder_DragDrop` / `XrefReorder_ConcurrencyConflict` / `Search_Aggregate_*` / `ProductForm_ImageUpload_*`
  - [ ] 5.3.3: 契约测试: `dict-schema.test.ts` 更新(若字段有变化)
  - [ ] 5.3.4: E2E 测试: 上述所有 `Public_*` / `Admin_*` 用例(含 404 / VueMountFailure / LegacyRedirect 等边界)
  - [ ] 5.3.5: 视觉回归基线重置: `public-product-seo.spec.ts` / `public-aggregate-search.spec.ts` / `admin-xref-reorder.spec.ts`
  - [ ] 5.3.6: 防回归脚本 `_test_regression.py --scan` 新增 V2 修复点扫描(47 项漏洞逐项校验)
  - **验证**: `dotnet test` + `npm run test:contract` + `npm run test:visual` 全绿
  - **依赖**: Task 5.2, 所有前序任务

---

## v3 修订新增任务(第二轮深度审查衍生漏洞修复)

> 共 30 个新任务,修复第二轮三维度并行深度审查发现的 62 个衍生漏洞
> 任务编号沿用原 Phase 编号,带 .v3 后缀

### Phase 0 v3 补丁任务

- [ ] **Task 0.2.8**: ProductDbContext Fluent API 配置层清理(修复 D1/D2/D12/D13/D20)
  - [ ] 0.2.8.1: 移除 `ProductDbContext.cs:62` 的 `.IsRequired()`(oem_no_normalized 允许 NULL)
  - [ ] 0.2.8.2: 移除 `ProductDbContext.cs:86` 的 `IsUnique()`,改为 `e.HasIndex(p => p.OemNoNormalized).HasFilter("oem_no_normalized IS NOT NULL")`
  - [ ] 0.2.8.3: 移除 `ProductDbContext.cs:153` 的 `IsUnique()`,改为两个部分唯一索引:
    ```csharp
    e.HasIndex(i => i.OemNo3).IsUnique()
        .HasFilter("image_role = 'primary' AND oem_no_3 IS NOT NULL")
        .HasDatabaseName("uq_product_images_primary");
    e.HasIndex(i => new { i.ProductId, i.Slot }).IsUnique()
        .HasFilter("image_role = 'detail'")
        .HasDatabaseName("uq_product_images_detail_slot");
    ```
  - [ ] 0.2.8.4: `ProductDbContext.cs:114` 改为 `e.Property(x => x.OemNo3).HasMaxLength(200)`
  - [ ] 0.2.8.5: `ProductDbContext.cs:164` 后补 `e.Property(s => s.UpdatedAt).HasDefaultValueSql("now()")`
  - [ ] 0.2.8.6: 补 Fluent API 配置 `idx_products_mr_1_unique` 部分唯一索引:
    ```csharp
    e.HasIndex(p => p.Mr1).IsUnique()
        .HasFilter("mr_1 IS NOT NULL")
        .HasDatabaseName("idx_products_mr_1_unique");
    ```
  - **验证**: `dotnet ef migrations script --idempotent` 输出无 `AlterColumn(... nullable: false)` 或 `CreateIndex(... unique: true)` 旧约束重建
  - **依赖**: Task 0.2

- [ ] **Task 0.2.9**: partition6_placeholder 创建方式统一(修复 D14)
  - [ ] 0.2.9.1: spec L592-595 的 `CREATE TABLE IF NOT EXISTS partition6_placeholder` SQL 移除
  - [ ] 0.2.9.2: 仅通过 EF Core 迁移创建,配置:
    ```csharp
    mb.Entity<Partition6Placeholder>(e =>
    {
        e.ToTable("partition6_placeholder");
        e.HasKey(x => x.Id);
        e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
    });
    ```
  - **验证**: `dotnet ef migrations script` 输出含 CreateTable;`psql \d partition6_placeholder` 表存在
  - **依赖**: Task 0.2

- [ ] **Task 0.4.12**: ISearchProvider.DeleteAsync 签名改造(修复 S19)
  - [ ] 0.4.12.1: `ISearchProvider.cs:26` 接口签名改为 `Task DeleteAsync(IEnumerable<string> mr1s, CancellationToken ct = default)`
  - [ ] 0.4.12.2: `MeiliSearchProvider.cs:132-139` 改为接收 `IEnumerable<string> mr1s`
  - [ ] 0.4.12.3: `ResilientSearchProvider.cs:173-178` 双写删除同步改造
  - [ ] 0.4.12.4: `AdminProductService.DeleteAsync` 调用方改为 `_search.DeleteAsync(new[] { product.Mr1 })`
  - **验证**: 单元测试 `Meili_Delete_ByMr1` 通过
  - **依赖**: Task 0.4

- [ ] **Task 0.4.13**: Meilisearch 双索引灰度迁移脚本(修复 S7/F18)
  - [ ] 0.4.13.1: 创建新索引 `products_v2`(主键 `mr_1`),配置 filterableAttributes
  - [ ] 0.4.13.2: 后台批量写入 V2 文档(不影响现有 `products` 索引)
  - [ ] 0.4.13.3: 切换 `MeiliSearchOptions.IndexName = "products_v2"`(热切换)
  - [ ] 0.4.13.4: 验证搜索结果一致性后,删除旧索引 `products`
  - [ ] 0.4.13.5: (可选)重命名 `products_v2` → `products`
  - **验证**: 集成测试 `Meili_IndexMigration_ZeroDowntime` 通过(迁移期间搜索不中断)
  - **依赖**: Task 0.4

- [ ] **Task 0.4.14**: Mr1IndexDoc record 重写为嵌套结构(修复 S7)
  - [ ] 0.4.14.1: `ISearchProvider.cs` 重写 `ProductIndexDoc` 为 `Mr1IndexDoc`:
    ```csharp
    public record Mr1IndexDoc(
        string Mr1,
        string ProductName1,
        string ProductName2,
        string Type,
        string? Oem2,
        bool IsPublished,
        bool IsDiscontinued,
        List<OemListItem> OemList,
        List<MachineListItem> MachineList,
        decimal? D1Mm, /* ... */
        Dictionary<string, string> ImagePrimaryKeys,
        List<string> ImageDetailKeys,
        int BrandSortOrderMin,
        int OemListSortOrderMin  // 新增(修复 S17)
    );
    public record OemListItem(string OemBrand, string OemNo3, int SortOrder, string? MachineType, bool IsPublished, string? Oem2);
    public record MachineListItem(string MachineBrand, string MachineModel, string? MachineCategory);
    ```
  - [ ] 0.4.14.2: `MeiliSearchProvider.IndexAsync` 改为 `primaryKey: "mr_1"`
  - **验证**: 单元测试 `Meili_BuildMr1Document_FlatToNested` 通过
  - **依赖**: Task 0.4.2

- [ ] **Task 0.5.5**: 前端 http.ts 拦截器双格式错误码兼容(修复 F3)
  - [ ] 0.5.5.1: `frontend/src/utils/http.ts` 加 `ERROR_CODE_MAP`:
    ```ts
    const ERROR_CODE_MAP: Record<string, string> = {
      'ERR_AUTH_FAILED': 'AUTH_FAILED',
      'ERR_CONFLICT': 'CONFLICT',
      'MR1_ALREADY_EXISTS': 'MR1_ALREADY_EXISTS',
      'OEM3_ALREADY_EXISTS': 'OEM3_ALREADY_EXISTS',
      'XREF_CONFLICT': 'XREF_CONFLICT',
      'CURSOR_INVALID': 'CURSOR_INVALID',
      'CURSOR_EXPIRED': 'CURSOR_EXPIRED',
      // ... 其他 13 个新错误码
    }
    const normalized = ERROR_CODE_MAP[errorCode] ?? errorCode
    ```
  - [ ] 0.5.5.2: 拦截器根据 normalized 错误码路由到友好提示
  - **验证**: 前端单元测试 `HttpInterceptor_LegacyCompat` 通过
  - **依赖**: Task 0.5

- [ ] **Task 0.5.6**: i18n 文案表补充新错误码翻译(修复 F3)
  - [ ] 0.5.6.1: `frontend/src/i18n/zh-CN.ts` + `en-US.ts` 补充 13 个新错误码翻译:
    - `MR1_ALREADY_EXISTS`: "MR.1 编码已存在" / "MR.1 code already exists"
    - `MR1_FORMAT_INVALID`: "MR.1 编码须为 1-10 位字母+数字"
    - `MR1_REQUIRED`: "V2 数据必须填写 MR.1 编码"
    - `OEM3_ALREADY_EXISTS`: "同 Brand 下 OEM 3 已存在"
    - `OEM3_REQUIRED` / `OEM_BRAND_REQUIRED` / `MACHINE_TYPE_INVALID`
    - `XREF_CONFLICT`: "OEM 排序已被他人修改,请刷新后重试"
    - `IMAGE_PRIMARY_DUPLICATE` / `IMAGE_DETAIL_SLOT_DUPLICATE` / `IMAGE_ROLE_SLOT_MISMATCH` / `IMAGE_DETAIL_SLOT_INVALID`
    - `SEARCH_PAGE_TOO_DEEP` / `CURSOR_INVALID` / `CURSOR_EXPIRED`
  - **验证**: 前端单元测试 `I18n_NewErrorCodes` 通过
  - **依赖**: Task 0.5

- [ ] **Task 0.6.3**: nginx Googlebot 白名单 + sitemap 单独 RateLimit(修复 F6)
  - [ ] 0.6.3.1: `docker/nginx.conf` 加 Googlebot User-Agent 白名单:
    ```nginx
    map $http_user_agent $is_googlebot {
        default 0;
        ~*googlebot 1;
        ~*bingbot 1;
    }
    ```
  - [ ] 0.6.3.2: `/sitemap.xml` location 单独 RateLimit(600/min)
  - [ ] 0.6.3.3: Googlebot 请求绕过 "public" RateLimit
  - **验证**: 压测 Googlebot 600 req/min 不触发 503
  - **依赖**: Task 0.6

- [ ] **Task 0.7.5**: CommonEndpoints 移除根路由(修复 F1)
  - [ ] 0.7.5.1: `CommonEndpoints.cs:18` 的 `MapGet("/")` 改为 `MapGet("/api/info")`
  - [ ] 0.7.5.2: nginx `location = /` 显式 `try_files $uri /index.html =404`,不回源后端
  - **验证**: `curl -I http://localhost/` 返回 `Content-Type: text/html`(非 JSON)
  - **依赖**: Task 0.7

- [ ] **Task 0.7.6**: 路由注册顺序(修复 F12)
  - [ ] 0.7.6.1: `EndpointRouteBuilderExtensions.cs` 按顺序注册:
    ```csharp
    app.MapRazorPages();         // 1. Razor Pages(SEO 路由优先)
    app.MapControllers();        // 2. API 控制器
    app.MapGet("/api/info", ...); // 3. 其他端点
    ```
  - [ ] 0.7.6.2: `Detail.cshtml.cs` 显式 `@page "/products/{pn1}/{pn2}/{brand}/{oem3}"`
  - **验证**: 路由测试 `/products/...` 命中 Razor Pages,非 controller
  - **依赖**: Task 0.7

### Phase 1 v3 补丁任务

- [ ] **Task 1.2.8**: PostgresSearchProvider 手动 _formatted 高亮 + _rankingScore(修复 S9)
  - [ ] 1.2.8.1: 实现 `BuildFormatted(string? source, string query)` 方法:
    ```csharp
    private static string BuildFormatted(string? source, string query)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(query)) return source ?? "";
        var escapedQuery = Regex.Escape(query);
        return Regex.Replace(source, escapedQuery, m => $"<mark>{m.Value}</mark>", RegexOptions.IgnoreCase);
    }
    ```
  - [ ] 1.2.8.2: `_rankingScore` 固定 0.5(PG 无相关性评分)
  - [ ] 1.2.8.3: 前端 v-html 兜底:`_formatted` 为空时回退显示原始字段
  - **验证**: 单元测试 `Search_Fallback_Pg_FormattedHighlight` 通过
  - **依赖**: Task 1.2

- [ ] **Task 1.2.9**: PG WHERE 补全 6 字段 + EXISTS 子查询(修复 S10/S22)
  - [ ] 1.2.9.1: PG WHERE 子句补全:
    ```sql
    AND (
      p.product_name_1 ILIKE @kw ESCAPE '\' OR
      p.product_name_2 ILIKE @kw ESCAPE '\' OR
      p.oem_2 ILIKE @kw ESCAPE '\' OR
      EXISTS (SELECT 1 FROM cross_references x
              WHERE x.product_id = p.id
                AND (x.oem_brand ILIKE @kw OR x.oem_no_3 ILIKE @kw OR x.oem_2 ILIKE @kw)) OR
      EXISTS (SELECT 1 FROM machine_applications m
              WHERE m.product_id = p.id
                AND (m.machine_brand ILIKE @kw OR m.machine_model ILIKE @kw))
    )
    ```
  - [ ] 1.2.9.2: `includeDiscontinued=false` 时加 EXISTS 子查询过滤 OEM 3 级 is_published
  - **验证**: 对比测试 `Search_Meili_vs_Pg_Recall` 召回数差异 < 5%
  - **依赖**: Task 1.2

- [ ] **Task 1.2.10**: PG ORDER BY 三层对齐 Meilisearch + CTE 预计算(修复 S2/S11)
  - [ ] 1.2.10.1: 引入 CTE 预计算 `brand_sort_order_min` + `oem_list_sort_order_min`:
    ```sql
    WITH mr1_sort AS (
      SELECT p.id AS product_id,
             MIN(b.sort_order) AS brand_sort_order_min,
             MIN(x.sort_order) AS oem_list_sort_order_min
      FROM products p
      LEFT JOIN cross_references x ON x.product_id = p.id
      LEFT JOIN xref_oem_brand b ON b.brand = x.oem_brand
      GROUP BY p.id
    )
    ```
  - [ ] 1.2.10.2: ORDER BY 三层:`ms.brand_sort_order_min ASC, ms.oem_list_sort_order_min ASC, p.updated_at DESC`
  - **验证**: 对比测试 `Search_Meili_vs_Pg_SortOrder` 前 20 条结果顺序一致
  - **依赖**: Task 1.2

- [ ] **Task 1.2.11**: PG LATERAL 内 LIMIT 50 + 移除 DISTINCT(修复 S2)
  - [ ] 1.2.11.1: LATERAL 子查询内部加 `LIMIT 50`(单 MR.1 最多 50 OEM 3)
  - [ ] 1.2.11.2: 移除 `json_agg(DISTINCT ...)`,LATERAL 内本身不重复
  - [ ] 1.2.11.3: ORDER BY 改 CTE 预计算(见 Task 1.2.10)
  - **验证**: `EXPLAIN ANALYZE` 单条查询 < 100ms(1M 数据量)
  - **依赖**: Task 1.2.10

- [ ] **Task 1.2.12**: Meilisearch typoTolerance/separatorTokens/stopWords 配置调整(修复 S4/S5/S6)
  - [ ] 1.2.12.1: `typoTolerance.minWordSizeForTypos` 改为 `{oneTypo: 3, twoTypos: 5}`
  - [ ] 1.2.12.2: `separatorTokens` 改为 `[" ", "/", ",", "."]`(移除 `-`)
  - [ ] 1.2.12.3: 新增 `nonSeparatorTokens: ["-"]`
  - [ ] 1.2.12.4: `stopWords` 改为 `["the", "a", "an"]`(移除 of/for/and)
  - **验证**: 单元测试 `Search_Aggregate_TypoTolerance_3LetterBrand`("BNW" 命中 "BMW")通过
  - **验证**: 单元测试 `Search_Aggregate_OemNo3_Hyphen_Precise`(`F-000000001` 精确命中)通过
  - **验证**: 单元测试 `Search_Aggregate_StopWords_OfInModel`(`OF-100` 不误命中 `D100`)通过
  - **依赖**: Task 1.2

- [ ] **Task 1.2.13**: _formatted XSS 占位符替换法(修复 S1)
  - [ ] 1.2.13.1: `MeiliSearchProvider.SearchAsync` 返回前处理:
    ```csharp
    const string MARK_OPEN = "\u0001MARK_OPEN\u0001";
    const string MARK_CLOSE = "\u0001MARK_CLOSE\u0001";
    var safe = raw.Replace("<mark>", MARK_OPEN).Replace("</mark>", MARK_CLOSE);
    safe = WebUtility.HtmlEncode(safe);
    safe = safe.Replace(MARK_OPEN, "<mark>").Replace(MARK_CLOSE, "</mark>");
    ```
  - **验证**: 单元测试 `Search_Aggregate_XssDefense_LiteralMarkTag` 通过(录入产品名 `<mark>test</mark>` 渲染为纯文本)
  - **依赖**: Task 1.2

- [ ] **Task 1.2.14**: 嵌套字段 filter 多字段组合语义明确(修复 S14)
  - [ ] 1.2.14.1: spec 明确:"单字段 OR 语义;多字段 AND 组合同元素 AND 语义"
  - [ ] 1.2.14.2: 单元测试 `Search_Filter_NestedMultiField_SameElement`:构造 MR.1 下有 BOSCH(下架) + MANN(上架),筛选 `oem_brand=BOSCH AND is_published=true` 不命中
  - **依赖**: Task 1.2

- [ ] **Task 1.2.15**: oemList 响应层过滤 isPublished(修复 S3)
  - [ ] 1.2.15.1: `MeiliSearchProvider.SearchAsync` 返回前,对每个 hit 的 `oemList` 过滤:
    - `includeDiscontinued=false`: 仅含 `isPublished=true`
    - `includeDiscontinued=true`: 含全部
  - **验证**: 单元测试 `Search_Aggregate_OemList_FilterUnpublished` 通过
  - **依赖**: Task 1.2

### Phase 3 v3 补丁任务

- [ ] **Task 3.2.9**: product_images 新增 naming_field 字段(修复 D16)
  - [ ] 3.2.9.1: `product_images` 表新增 `naming_field varchar(20)` 字段(记录 'oem_no_3' 或 'mr_1')
  - [ ] 3.2.9.2: EF Core 配置 + 迁移
  - [ ] 3.2.9.3: `BuildKeyAsync` 写入时记录 naming_field
  - [ ] 3.2.9.4: 前端查询 DB 拿 key,不根据配置动态生成
  - **验证**: 集成测试:切换配置后,旧 OEM 3 详情页图片仍可显示
  - **依赖**: Task 3.2

### Phase 4 v3 补丁任务

- [ ] **Task 4.1.8**: Detail.cshtml 挂载点分离(修复 F2)
  - [ ] 4.1.8.1: SSR 内容放在 `<div id="seo-content">`
  - [ ] 4.1.8.2: Vue 挂载点独立:`<div id="gallery-app"></div>` + `<div id="compare-app"></div>` + `<div id="inquiry-app"></div>`
  - [ ] 4.1.8.3: spec 明确:"Vue 挂载点必须独立于 SSR 内容容器,严禁复用同一 div"
  - **验证**: 浏览器禁用 JS → SSR 内容可见;启用 JS → Vue 画廊加载,SSR 内容不被清空
  - **依赖**: Task 4.1

- [ ] **Task 4.1.9**: product-detail-client.js try-catch 降级 + modulepreload(修复 F8)
  - [ ] 4.1.9.1: `vite.config.ts` 配置 `manualChunks` 将 Vue 强制打入 `product-detail-client.js`
  - [ ] 4.1.9.2: 或 `<link rel="modulepreload" href="/assets/vue-chunk.js">` 预加载
  - [ ] 4.1.9.3: 脚本加 try-catch:
    ```js
    try {
      const { createApp } = await import('vue')
      const { GalleryApp } = await import('./GalleryApp')
      createApp(GalleryApp, { initialData: window.__PRODUCT__ }).mount('#gallery-app')
    } catch (e) {
      console.error('Vue 加载失败,SSR 内容仍可用', e)
    }
    ```
  - **验证**: 模拟 Vue chunk 加载失败,SSR 内容仍可见,控制台有降级日志
  - **依赖**: Task 4.1

- [ ] **Task 4.1.10**: 018_v2_legacy_data_cleanup.sql 双表灰度方案(修复 F20)
  - [ ] 4.1.10.1: 阶段 1 创建 `products_v2` 表(新结构):
    ```sql
    CREATE TABLE products_v2 (LIKE products INCLUDING ALL);
    ALTER TABLE products_v2 ALTER COLUMN mr_1 SET NOT NULL;
    -- ... 其他 V2 字段改造
    ```
  - [ ] 4.1.10.2: 阶段 2 ETL 导入新数据到 `products_v2`(应用层脚本)
  - [ ] 4.1.10.3: 阶段 3 切换读流量(应用层双写期间)
  - [ ] 4.1.10.4: 阶段 4 删除旧表 + 重命名:
    ```sql
    DROP TABLE products;
    ALTER TABLE products_v2 RENAME TO products;
    ```
  - [ ] 4.1.10.5: 阶段 5 重建 Meilisearch 索引(新结构)
  - [ ] 4.1.10.6: 图片对象清理需应用层脚本(非 SQL)
  - **验证**: 灰度期间 `products_v2` 与 `products` 数据一致性校验;MinIO 无孤儿对象
  - **依赖**: Task 4.1

- [ ] **Task 4.5.1**: 创建 GalleryApp.vue + props 接口(修复 F16)
  - [ ] 4.5.1.1: `frontend/src/components/GalleryApp.vue`:
    ```vue
    <script setup lang="ts">
    import { ref, onMounted } from 'vue'
    interface GalleryProps {
      images: Array<{ imageKey: string; imageUrl: string; oemNo3?: string; imageRole?: string }>
      oemNo3: string
      mr1: string
    }
    const props = defineProps<GalleryProps>()
    const currentImage = ref(props.images[0]?.imageUrl ?? '')
    </script>
    <template>
      <div class="gallery">
        <img :src="currentImage" :alt="props.oemNo3" loading="lazy" />
        <!-- 缩略图列表 -->
      </div>
    </template>
    ```
  - **依赖**: Task 4.5

- [ ] **Task 4.5.2**: 创建 CompareApp.vue + InquiryApp.vue(修复 F16)
  - [ ] 4.5.2.1: `CompareApp.vue` props 接口:`{ mr1, oemNo3, productName1 }`
  - [ ] 4.5.2.2: `InquiryApp.vue` props 接口:`{ mr1, oemNo3, brand }`
  - **依赖**: Task 4.5

- [ ] **Task 4.5.3**: 抽取 buildProductUrl 工具函数(修复 F9)
  - [ ] 4.5.3.1: `frontend/src/utils/product-url.ts`:
    ```ts
    export function buildProductUrl(p: {
      productName1: string
      productName2: string
      oemBrand?: string
      oemNo3: string
    }): string {
      const slugify = (s: string) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
      const pn1 = slugify(p.productName1 || 'product')
      const pn2 = slugify(p.productName2 || 'detail')
      const brand = slugify(p.oemBrand || 'oem')
      const oem3 = encodeURIComponent(p.oemNo3)
      return `/products/${pn1}/${pn2}/${brand}/${oem3}`
    }
    ```
  - **依赖**: Task 4.5

- [ ] **Task 4.5.4**: 全局替换 router.push('/product/...') 为 window.location.href(修复 F9/F17)
  - [ ] 4.5.4.1: 全项目 grep `router.push.*['"\`]/product/` 找出所有调用点
  - [ ] 4.5.4.2: 替换为:
    ```ts
    window.location.href = buildProductUrl(product)
    ```
  - [ ] 4.5.4.3: 涉及文件:`PublicSearchView.vue` / `PublicCompareView.vue` / `PublicProductView.vue` / `AppHeader.vue` / `SearchView.vue` / `DemoView.vue`
  - **验证**: 全项目 grep `router.push.*product/` 无遗留
  - **依赖**: Task 4.5.3

- [ ] **Task 4.5.5**: 对比列表状态 sessionStorage 持久化(修复 F17)
  - [ ] 4.5.5.1: 对比列表 store 改造:写入 sessionStorage
  - [ ] 4.5.5.2: 详情页 mount 时从 sessionStorage 恢复对比列表
  - **验证**: E2E 测试:加入对比 → 跳转详情页 → 对比列表仍保留
  - **依赖**: Task 4.5

- [ ] **Task 4.6.4**: CursorHmac 加版本前缀 + 24h TTL(修复 F5/S13)
  - [ ] 4.6.4.1: `CursorHmac.Sign` 改为:
    ```csharp
    public string Sign(string updatedAtIso, string mr1)
    {
        var expUnixTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;
        var mr1B64 = Base64UrlEncode(mr1);
        var payload = $"v2:{expUnixTs}|{updatedAtIso}|{mr1B64}";
        var hash = HMACSHA256.HashData(_currentKey, Encoding.UTF8.GetBytes(payload));
        return $"{payload}|{ToBase64Url(hash)[..16]}";
    }
    ```
  - [ ] 4.6.4.2: `VerifyAndExtract` 加版本前缀检查 + TTL 校验
  - [ ] 4.6.4.3: 过渡期 7 天支持旧 cursor(无 v2: 前缀)
  - **验证**: 单元测试 `Cursor_Expired_24h` + `Cursor_LegacyCompat_7days` 通过
  - **依赖**: Task 4.6

- [ ] **Task 4.6.5**: 新增错误码 CURSOR_INVALID / CURSOR_EXPIRED(修复 S13)
  - [ ] 4.6.5.1: ProblemDetailsFactory 加 `CURSOR_INVALID`(400) / `CURSOR_EXPIRED`(400)
  - [ ] 4.6.5.2: 前端拦截器处理 `CURSOR_EXPIRED` 自动重置到第 1 页
  - **依赖**: Task 4.6.4

- [ ] **Task 4.7**: 抽取 IProductDetailService 公共服务(修复 F19)
  - [ ] 4.7.1: `SakuraFilter.Api/Services/IProductDetailService.cs` 接口定义
  - [ ] 4.7.2: `ProductDetailService.GetByOem3Async(string oem3)` 实现(复用现有三级 fallback 逻辑)
  - [ ] 4.7.3: `Detail.cshtml.cs` PageModel 注入 `IProductDetailService`
  - [ ] 4.7.4: `PublicProductController` 注入 `IProductDetailService`
  - [ ] 4.7.5: 旧 `/api/products/{oem}` 端点标记 `[Obsolete]`
  - **验证**: 代码审查 `Detail.cshtml.cs` 与 `PublicProductController` 无重复查询代码
  - **依赖**: Task 4.1

- [ ] **Task 4.8**: 前端 types.ts ProductImageInfo 字段同步(修复 F10)
  - [ ] 4.8.1: `frontend/src/api/types.ts` 更新 `ProductImageInfo`:
    ```ts
    export interface ProductImageInfo {
      // 旧字段(过渡期保留)
      slot?: number
      // 新字段
      oemNo3?: string
      imageRole?: 'primary' | 'detail'
      namingField?: string
      imageKey: string
      imageUrl: string
      contentType: string
      sizeBytes: number
      width?: number
      height?: number
    }
    ```
  - [ ] 4.8.2: 画廊组件兼容两种格式:`const role = img.imageRole ?? (img.slot === 1 ? 'primary' : 'detail')`
  - **验证**: `npm run typecheck` 通过
  - **依赖**: Task 3.3

- [ ] **Task 4.9**: 创建 html-sanitizer.ts + 安装 dompurify 依赖(修复 F14)
  - [ ] 4.9.1: `frontend/package.json` 加 `"dompurify": "^3.0.0"` 依赖
  - [ ] 4.9.2: 新增 `frontend/src/utils/html-sanitizer.ts` 封装 DOMPurify,白名单只允许 `<mark>` 标签
  - [ ] 4.9.3: `AggregateSearchView.vue` 使用 `v-html` 渲染 `_formatted` 时调用 `sanitize()` 双保险
  - **验证**: 前端单元测试 `Search_Aggregate_XssDefense` 通过(验证 `<script>`/`<iframe>`/`<img onerror>` 被移除,`<mark>` 保留)

---

## v4 补丁任务清单(共 48 个,Phase 0-5 分布)

> 详见 spec.md 末尾"第三轮深度审查衍生漏洞修复清单(v4 修订)"第五节"v4 补丁任务清单"。
> 修订历史: v4 修复第三轮深度审查发现的 70 个衍生漏洞(高危 29 / 中危 41 / 低危 12)

### Phase 0 v4 补丁任务(11 个)

- [ ] **Task 0.2.8**: `Product.cs:122-131` CrossReference 实体加 SortOrder/MachineType/IsPublished/Oem2/RowVersion(uint)5 个属性(修复 D3-2/D3-20)
- [ ] **Task 0.2.9**: `ProductDbContext.cs:108-117` CrossReference 配置加 IsRowVersion + UNIQUE 部分索引 `uq_xrefs_brand_oem3` + sort_order 索引(修复 D3-2/D3-20)
- [ ] **Task 0.2.10**: `ProductDbContext.cs:86` 移除 OemNoNormalized 的 IsUnique(),改为部分普通索引(修复 D3-7)
- [ ] **Task 0.2.11**: `ProductDbContext.cs:104` `e.HasIndex(p => p.Mr1)` 替换为 UNIQUE 部分索引 `idx_products_mr_1_unique`(修复 D3-16/D3-19)
- [ ] **Task 0.2.12**: `ProductDbContext.cs:116` `e.HasIndex(x => new { x.OemBrand, x.OemNo3 })` 替换为 `idx_xrefs_brand_oem3_sort`(修复 D3-28)
- [ ] **Task 0.2.13**: 新增 `Partition6Placeholder.cs` 实体 + ProductDbContext 注册(修复 D3-17)
- [ ] **Task 0.2.14**: `Product.cs:195/77` 移除 UpdatedAt/CreatedAt 的 C# 默认值,DbContext 加 `HasDefaultValueSql("now()")`(修复 D3-15)
- [ ] **Task 0.2.15**: spec L489 后补 `ALTER TABLE cross_references ALTER COLUMN is_discontinued SET NOT NULL/DEFAULT false`(修复 D3-22)
- [ ] **Task 0.2.16**: spec L316-318 "主图被删除后再次上传"边界补充:重新上架 UPDATE 旧下架行,不 INSERT 新行(修复 D3-22)
- [ ] **Task 0.2.17**: spec L521 "ulong?" 改为 "uint",CrossReference xmin 配置统一(修复 D3-20)
- [ ] **Task 0.2.18**: spec v3 D4 修复补充:DROP CONSTRAINT 前先查询实际外键名(修复 D3-29)

- [ ] **Task 0.3.10**: `AdminProductService.cs:43/57/1038-1044` 移除 NormalizeOem 方法,oem_no_normalized 派生改为 mr_1 原值(修复 D3-1)
- [ ] **Task 0.3.11**: `AdminProductService.cs:100-108/247-254` 写 CrossReference 时补全 sort_order/machine_type/is_published/oem_2(修复 D3-2)
- [ ] **Task 0.3.12**: `AdminProductService.cs:1008-1036` ValidateForm 加 MR.1 必填/格式校验 + 长度上限改 10 + 控制字符过滤(修复 D3-3/D3-12)
- [ ] **Task 0.3.13**: `AdminProductService.cs:184-185` UpdateAsync 同步更新 OemNoNormalized = Mr1(修复 D3-4)
- [ ] **Task 0.3.14**: `AdminProductService.cs:57-59` 唯一性检查改用 Mr1(修复 D3-5)
- [ ] **Task 0.3.15**: `AdminProductService.cs:114-115` CreateAsync 保存 xrefs 后反向更新 products.oem_2(按 sort_order 排序后取第一个,空列表置 NULL)(修复 D3-14)

- [ ] **Task 0.4.2a**: `BuildMr1DocumentAsync` 过滤软删除 brand(b.deleted_at IS NULL)+ 预计算 OemListPublishedBrands/OemListPublishedNo3s/OemBrandsStr/OemNo3sStr/OemListSortOrderMin(修复 S3-7/S3-8/S3-21)
- [ ] **Task 0.4.4a**: Meilisearch filter 注入防御改为移除 `"` 和 `\` 策略 + 嵌套字段 filter 单元测试(修复 S3-23)
- [ ] **Task 0.4.6a**: typoTolerance stopWords 移除 "a" + separatorTokens 不加 `nonSeparatorTokens: ["-"]`(修复 S3-19/S3-20)
- [ ] **Task 0.4.8a**: Meilisearch 高亮标签专属化(`\u0001MO\u0001`/`\u0001MC\u0001`)+ 后端只还原专属标签 + 递归 `SanitizeFormatted(JToken)`(修复 D3-12/D3-13/S3-1)
- [ ] **Task 0.4.13a**: Meilisearch 双索引灰度改为 5 阶段(双写 + 读切换 + 停双写)+ `IOptionsMonitor<MeiliSearchOptions>` 热切换 + DeleteAsync 双索引同步(修复 S3-6/S3-17/S3-18)
- [ ] **Task 0.4.14a**: `Mr1IndexDoc` record 新增扁平化冗余字段(`OemListPublishedBrands`/`OemListPublishedNo3s`/`OemBrandsStr`/`OemNo3sStr`)+ filterableAttributes 补充(修复 S3-7/S3-21)
- [ ] **Task 0.4.15**: Brand sort_order 变更后台重建(`IHostedService` + `Channel<string>` + `IMemoryCache` 5 秒去重 + `search_index_pending` 表持久化兜底)(修复 S3-22)

- [ ] **Task 0.5.5**: `frontend/src/utils/http.ts` 拦截器改造:`ERROR_CODE_I18N` 字符串映射(V2 新码 + 旧 ERR_ 别名)+ `data.errorCode` 优先 + CURSOR_EXPIRED/INVALID 自动重置(修复 F2-5/F2-21)
- [ ] **Task 0.5.6**: `frontend/src/i18n/locales/zh-CN.ts` + `en-US.ts` 新增 `common.error.*` 命名空间 13 个错误码翻译(修复 F2-18)

### Phase 1 v4 补丁任务(4 个)

- [ ] **Task 1.2.9a**: PG 兜底分词 OR 拼接(`req.Q.Split` 拆 token + `EscapeLikePattern` + 参数化)(修复 S3-3)
- [ ] **Task 1.2.10a**: PG 兜底 `ORDER BY` 第 3 字段改为相关性评分(`CASE WHEN ... THEN 100 ...`)+ keyset 分页(修复 S3-5/S3-15)
- [ ] **Task 1.2.11a**: PG 兜底 `lat_machine` LATERAL 子查询完整实现(过滤 `is_discontinued=false` + LIMIT 50)(修复 S3-4)
- [ ] **Task 1.2.12**: trgm GIN 索引补充 5 个(`idx_xrefs_oem_no_3_trgm` / `idx_xrefs_oem_brand_trgm` / `idx_products_pn1_trgm` / `idx_products_pn2_trgm` / `idx_products_oem_2_trgm`)+ `pg_trgm` extension 确认(修复 S3-16)

### Phase 3 v4 补丁任务(2 个)

- [ ] **Task 3.2.10**: `AdminProductService.cs:243-244` UpdateAsync xref 全量替换改为增量更新(新增/更新/删除三类),更新类触发 xmin 乐观锁(修复 D3-21)
- [ ] **Task 3.2.11**: spec v3 D16 修复调整:`naming_field` 字段语义改为"命名快照值"(审计/追溯),前端查 `image_key` 不动态生成(修复 D3-30)

---

### Phase 4 v4 补丁任务(13 个)

- [ ] **Task 4.1.11**: `Detail.cshtml` 改用 JSON 数据岛替代 `window.__PRODUCT__`(修复 F2-1);挂载点内 SSR 兜底主图(修复 F2-15);`<script type="module">` 替换 `<script defer>`(修复 F2-9)
- [ ] **Task 4.1.12**: `product-detail-client.ts` 实现 `safeMount(id, Comp, props)` ErrorBoundary + try-catch 降级 UI(修复 F2-8)
- [ ] **Task 4.1.13**: `frontend/vite.config.ts` 多入口 build + `manualChunks: { vue: ['vue', 'vue-router', 'pinia'] }`(修复 F2-9)
- [ ] **Task 4.1.14**: `018_v2_legacy_data_cleanup.sql` 阶段 4 外键安全切换顺序(分阶段 DROP/ADD CONSTRAINT)(修复 D3-11/D3-24/F2-3/F2-4/F2-11)
- [ ] **Task 4.1.15**: spec L1128 `vue-gallery` 命名同步更新为 `gallery-app`/`compare-app`/`inquiry-app`;`product-detail-client.js` 示例代码同步(修复 F2-24)
- [ ] **Task 4.1.16**: Meilisearch 双索引切换回滚预案:阶段 5a/5b/5c 拆分 + 旧索引保留 7 天(修复 F2-23)

- [ ] **Task 4.5.6**: `CursorHmac.cs` 验签顺序调整(先 HMAC 后 TTL)+ 统一 Base64Url 编码 + 旧 cursor 过渡期分支(`LEGACY_CUTOFF_TS`)+ 双 key 验签 + `pageNum` 字段(修复 S3-9/S3-13/S3-14/S3-24/F2-2/F2-10/F2-17)
- [ ] **Task 4.5.7**: `IProductWriteStrategy` / `IProductReadStrategy` 接口 + 阶段 3 双写策略表(修复 F2-19)
- [ ] **Task 4.5.8**: `frontend/src/utils/build-product-url.ts` 实现 `buildProductUrl(p)` 工具函数 + 中文 slugify 兜底(`Uri.EscapeDataString`)(修复 F2-6/F2-12)
- [ ] **Task 4.5.9**: 全局 grep 替换 `router.push('/product/...')` 4 处遗漏(`SearchView.vue:121,207` / `AppHeader.vue:202` / `PublicCompareView.vue:336` / `PublicProductView.vue:59`)(修复 F2-12)
- [ ] **Task 4.5.10**: `PublicCompareView.vue` 对比列表 sessionStorage 仅持久化 ID 数组 + `QuotaExceededError` 降级(修复 F2-13)

- [ ] **Task 4.6.6**: `docker/nginx.conf` Googlebot UA 白名单限定 location(仅 `^/(products|product|sitemap.xml|sitemaps|robots.txt)`),admin 路径严格 RateLimit 无视 UA(修复 F2-14)
- [ ] **Task 4.6.7**: `Detail.cshtml.cs` OnGetAsync 404 渲染友好页 + 站内搜索入口(修复 F2-16)
- [ ] **Task 4.6.8**: `AdminProductService` 注入 `IProductWriteStrategy`,CreateAsync/UpdateAsync 按 strategy 决定写入目标;ETL `EtlImportService` 同理(修复 F2-19)

- [ ] **Task 4.8.1**: `frontend/src/api/types.ts` 新增 `AggregateSearchHit`(含 mr1/productName1/oemList[]/machineList[]/_formatted/_rankingScore)+ `AggregateSearchResponse` 类型;`SearchHit` 补 `mr1`/`productName1`/`oemList` 字段(修复 F2-7)
- [ ] **Task 4.8.2**: `frontend/src/api/index.ts` 新增 `searchApi.aggregate(req)` 对接 `POST /api/public/search/aggregate`;`SearchView.vue` 改用新 API + 新类型(修复 F2-7)
- [ ] **Task 4.9.1**: `frontend/src/utils/__tests__/` 新增单元测试:`html-sanitizer.test.ts` / `build-product-url.test.ts` / `GalleryApp.test.ts` / `error-code-map.test.ts`(修复 F2-20)

### Phase 5 v4 补丁任务(9 个)

- [ ] **Task 5.1.10**: `EtlImportService.cs:1832-1845` products_stage 表定义加 `mr_1`/`oem_2`/`d4_mm`/`h4_mm`/`d*_raw`/`h*_raw` 字段 + 精度改 `NUMERIC(10,2)`(修复 D3-6/D3-19/D3-25)
- [ ] **Task 5.1.11**: `EtlImportService.cs:832-879` products COPY 列清单 + JSONL 解析加 `mr_1` 字段(必填 + 格式校验 `^[A-Za-z0-9]{1,10}$`)(修复 D3-6)
- [ ] **Task 5.1.12**: `EtlImportService.cs:945-992` INSERT INTO products 列清单加 `mr_1` + ON CONFLICT 改为 `(mr_1) WHERE mr_1 IS NOT NULL`(修复 D3-7)
- [ ] **Task 5.1.13**: `EtlImportService.cs:1212-1218` `LoadExistingOemMapAsync` 改为查 `mr_1`,JSONL 字段名 `product_oem` → `mr_1`(修复 D3-8)
- [ ] **Task 5.1.14**: `EtlImportService.cs:1398-1480` xrefs_stage + COPY + INSERT 加 `sort_order`/`machine_type`/`is_published`/`oem_2` 字段(修复 D3-9)
- [ ] **Task 5.1.15**: `EtlImportService.cs:935-937` cascade 语义重新定义: cascade=false 时显式 TRUNCATE products + product_images(修复 D3-10)
- [ ] **Task 5.1.16**: `EtlImportService.cs:1457-1480` xrefs INSERT 前 DELETE 旧下架行,避免下架后重新上架时 23505(修复 D3-18)
- [ ] **Task 5.1.17**: `EtlImportService.cs:1850-1851` `GetStringOrNull` 加控制字符过滤(允许 `\t` `\n` `\r`)(修复 D3-27)
- [ ] **Task 5.1.18**: `CleanupOrphanImagesAsync` 应用层脚本:TRUNCATE product_images 后扫描 OSS 清理孤儿文件(修复 D3-23)

- [ ] **Task 4.10**: 更新 E2E 测试 URL + 创建 SEO 基线(修复 F15)
  - [ ] 4.10.1: 更新 `public-product.spec.ts` / `smoke.spec.ts` / `public-search-flow.spec.ts` 访问 SEO URL
  - [ ] 4.10.2: 创建新基线 `public-product-seo.spec.ts`(测试 SSR 内容、Vue mount、画廊交互)
  - [ ] 4.10.3: 创建 `public-product-mobile.spec.ts`(移动端响应式)
  - [ ] 4.10.4: 删除旧视觉基线截图,重新生成 V2 版本
  - **验证**: `npm run test:e2e` + `npm run test:visual` 全绿
  - **依赖**: Task 5

### Phase 5 v3 补丁任务

- [ ] **Task 5.1.7**: ETL COPY 列定义排除 xmin 系统列(修复 D22)
  - [ ] 5.1.7.1: `EtlImportService.cs` 中 COPY products_stage 和 cross_references_stage 列清单明确排除 xmin
  - **验证**: ETL 全量导入 100 万行,无 xmin 相关报错
  - **依赖**: Task 5.1

- [ ] **Task 5.1.8**: ETL ON CONFLICT 改造(修复 D5/D6)
  - [ ] 5.1.8.1: `EtlImportService.cs:976` 改为 `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL DO NOTHING`
  - [ ] 5.1.8.2: `EtlImportService.cs:993` 改为 `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL DO UPDATE SET ...`
  - [ ] 5.1.8.3: `EtlImportService.cs:1470` 改为 `ON CONFLICT (oem_brand, oem_no_3) WHERE is_discontinued = false DO NOTHING`
  - [ ] 5.1.8.4: `EtlImportService.cs:1478` 改为 `ON CONFLICT (oem_brand, oem_no_3) WHERE is_discontinued = false DO UPDATE SET ...`
  - **验证**: 集成测试 ETL 导入 V2 mock 数据,无 42P10 错误
  - **依赖**: Task 5.1

- [ ] **Task 5.1.9**: AdminProductService 反向更新 products.oem_2(修复 D8)
  - [ ] 5.1.9.1: `CreateAsync` 保存 xrefs 后补:
    ```csharp
    product.Oem2 = form.CrossReferences.FirstOrDefault()?.Oem2?.Trim();
    await _db.SaveChangesAsync(ct);
    ```
  - [ ] 5.1.9.2: `UpdateAsync` 同样改造
  - **验证**: 单元测试 `Product_Create_Oem2DerivedFromFirstXref` 通过
  - **依赖**: Task 0.3

---

## Task Dependencies(v3 修订补充)

- **Phase 0 v3 补丁**: Task 0.2 → (Task 0.2.8 ∥ Task 0.2.9);Task 0.4 → (Task 0.4.12 ∥ Task 0.4.13 ∥ Task 0.4.14);Task 0.5 → (Task 0.5.5 ∥ Task 0.5.6);Task 0.6 → Task 0.6.3;Task 0.7 → (Task 0.7.5 ∥ Task 0.7.6)
- **Phase 1 v3 补丁**: Task 1.2 → (Task 1.2.8 ∥ 1.2.9 ∥ 1.2.10 ∥ 1.2.11 ∥ 1.2.12 ∥ 1.2.13 ∥ 1.2.14 ∥ 1.2.15)
- **Phase 3 v3 补丁**: Task 3.2 → Task 3.2.9
- **Phase 4 v3 补丁**: Task 4.1 → (Task 4.1.8 ∥ 4.1.9 ∥ 4.1.10);Task 4.5 → (Task 4.5.1 ∥ 4.5.2 ∥ 4.5.3 ∥ 4.5.4 ∥ 4.5.5);Task 4.6 → (Task 4.6.4 ∥ 4.6.5);Task 4.7 独立;Task 4.8 ∥ Task 4.9;Task 4.10
- **Phase 5 v3 补丁**: Task 5.1 → (Task 5.1.7 ∥ 5.1.8 ∥ 5.1.9)

**可并行任务补充**:
- Phase 0 v3: Task 0.2.8 ∥ Task 0.2.9 ∥ Task 0.4.12 ∥ Task 0.4.13 ∥ Task 0.4.14 ∥ Task 0.5.5 ∥ Task 0.5.6 ∥ Task 0.6.3 ∥ Task 0.7.5 ∥ Task 0.7.6
- Phase 1 v3: 8 个 Task 1.2.x 全部可并行
- Phase 4 v3: Task 4.5.x 五个子任务全部可并行

---

## Task Dependencies

- **Phase 0 内部**: Task 0.1 → Task 0.2 → (Task 0.3 ∥ Task 0.4 ∥ Task 0.5 ∥ Task 0.6 ∥ Task 0.7) 并行
- **Phase 1**: Task 0.3 → Task 1.1;Task 0.4 → Task 1.2 → Task 1.3
- **Phase 2**: (Task 0.1 + Task 0.2) → Task 2.1 → Task 2.2;(Task 0.4 + Task 2.1) → Task 2.3
- **Phase 3**: (Task 0.1 + Task 0.2) → Task 3.1 → Task 3.2 → Task 3.3
- **Phase 4**: (Task 0.4 + Task 0.7 + Task 2.3) → Task 4.1 → (Task 4.2 ∥ Task 4.3 ∥ Task 4.4 ∥ Task 4.5 ∥ Task 4.6) 并行
- **Phase 5**: Task 5.1 依赖 Phase 0;Task 5.2 依赖 Task 5.1;Task 5.3 依赖所有前序

**可并行任务**:
- Phase 0: Task 0.3 ∥ Task 0.4 ∥ Task 0.5 ∥ Task 0.6 ∥ Task 0.7(分别依赖 0.2)
- Phase 1: Task 1.1 ∥ Task 1.2(分别依赖 0.3/0.4)
- Phase 2: Task 2.2 ∥ Task 2.3(分别依赖 2.1)
- Phase 3: 整 Phase 可与 Phase 2 并行(均依赖 Phase 0)
- Phase 4: Task 4.2 ∥ Task 4.3 ∥ Task 4.4 ∥ Task 4.5 ∥ Task 4.6(均依赖 4.1)

**关键路径**: Task 0.1 → 0.2 → 0.4 → 4.1 → 5.3(MR.1 主键化 → Meilisearch 重构 → Razor SSR → 全量测试)

**新增任务汇总(v2 修订)**:
- Task 0.5: ProblemDetailsFactory 错误码统一(修复漏洞 4)
- Task 0.6: nginx.conf 路由配置(修复漏洞 1,前端联动)
- Task 0.7: Program.cs 改造(修复漏洞 7,8,前端联动)
- Task 4.5: Vue client mount 子组件(修复漏洞 3)
- Task 4.6: CursorHmac 改造(修复漏洞 6)


---

## v5 补丁任务清单(共 36 个,Phase 0-5 分布)

> 详见 spec.md 末尾"第四轮深度审查衍生漏洞修复清单(v5 修订)"第五节"v5 补丁任务清单"。
> 修订历史: v5 修复第四轮深度审查发现的 48 个衍生漏洞(高危 2 / 中危 27 / 低危 19)
> 关键设计调整 8 项: ① 占位符 XSS 防御彻底修复(BMP 私用区 U+E000/U+E001 + C0 控制字符全过滤) ② IProductWriteStrategy 显式事务边界 + Create/Update/Delete/Restore 全覆盖 ③ Meilisearch 双索引改用 WriteTargets 配置列表 + _index volatile + 死信队列 ④ CursorHmac 配置化 LEGACY_CUTOFF_TS + id 字段四元组比较 ⑤ OemBrandsStr 分隔符改空格(对齐 separatorTokens) ⑥ brand 软删除后 OEM 3 仍可搜索(oem_list 保留 + CASE WHEN) ⑦ BuildSlug 单一逻辑(先 EscapeDataString 再替换非字母数字,% 保留) + mr_1 末 6 位防冲突 ⑧ ETL 与 AdminProductService advisory lock 协调(7740001/7740002)

### Phase 0 v5 补丁任务(15 个 — 通用)

- [ ] **Task 0.2.19**: `SakuraFilter.Core/Entities/Product.cs` + `SakuraFilter.Infrastructure/Data/ProductDbContext.cs` 显式配置 `e.Property(p => p.D1Mm).HasColumnType("numeric(10,2)").HasPrecision(10,2)` 等 8 个尺寸字段(d1_mm/d2_mm/h1_mm/d3_mm/d4_mm/h2_mm/h3_mm/h4_mm),与 spec.md L106-114 PG schema NUMERIC(10,2) 对齐(修复 D4-18 精度不一致)
  - **依赖**: Task 0.2.1
  - **验证**: `dotnet ef migrations script` 生成的 SQL 含 `numeric(10,2)`;单元测试 `Product_DecimalPrecision_AllFields` 通过

- [ ] **Task 0.2.20**: spec.md L1128 同步更新 Vue 挂载点命名: `<div id="vue-gallery">` → `<div id="gallery-app">`,确保与 L1610-1612 的 `gallery-app` / `compare-app` / `inquiry-app` 三挂载点一致(修复 F3-8 内部命名矛盾)
  - **依赖**: 无
  - **验证**: spec.md L1128 与 L1610-1612 字符串搜索 `id="vue-gallery"` 无命中;`id="gallery-app"` 命中 2 处

- [ ] **Task 0.2.21**: spec.md D3-14 反向更新逻辑补充 `oem_2` 取值: 当 CrossReference.Oem2 字段在 sort_order 排序后第一个为空时,fallback 到 `FirstOrDefault(x => !string.IsNullOrEmpty(x.Oem2))?.Oem2`(修复 D4-13 oem_2 反向更新空指针风险)
  - **依赖**: Task 0.3.15
  - **验证**: 单元测试 `Product_UpdateOem2_FallbackToFirstNonNull` 通过(全空列表置 NULL,有非空取首个)

- [ ] **Task 0.2.22**: spec.md D3-21 增量更新匹配条件附加 `WHERE is_published = true AND is_discontinued = false`,防止已下架 OEM 3 被错误匹配覆盖(修复 D4-12 增量更新误改下架记录)
  - **依赖**: Task 3.2.10
  - **验证**: 单元测试 `Xref_IncrementalUpdate_SkipDiscontinued` 通过(下架记录 sort_order 不变)

- [ ] **Task 0.2.23**: spec.md L489 `image.primary_naming_field` NULL 时前端展示 'legacy' 策略说明,旧数据按 `image_key` 字段直接展示,不动态生成 URL(修复 D4-14 naming_field NULL 时的前端兜底)
  - **依赖**: Task 3.2.11
  - **验证**: spec.md L489 含 'legacy' 兜底描述;前端 `AdminProductFormView.vue` 含 `namingField ?? 'legacy'` 分支

- [ ] **Task 0.2.24**: spec.md D3-9 明确给出 `xrefs_stage` 临时表完整定义 + COPY 列清单 + INSERT 列清单完整 SQL,字段顺序与 JSONL 严格对齐(修复 D4-17 ETL 字段映射黑盒)
  - **依赖**: Task 0.1.1
  - **验证**: spec.md D3-9 含完整 CREATE TEMP TABLE + COPY + INSERT SQL;`EtlImportService.ImportXrefsAsync` 实现与 spec 字段顺序一致

- [ ] **Task 0.3.16**: `SakuraFilter.Api/Services/AdminProductService.cs:1008-1036` `ValidateForm` 加 `StripControlChars` 控制字符过滤方法,移除 U+0000-U+001F(保留 \t \n \r) + U+007F-U+009F + BMP 私用区 U+E000-U+F8FF + 非字符 U+FDD0-U+FDEF + U+FFFE/U+FFFF(修复 S4-1 用户输入字面量绕过 XSS 防御)
  - **依赖**: Task 0.3.12
  - **验证**: 单元测试 `ValidateForm_StripsControlChars` 通过(输入 `\u0001MO\u0001` 字面量被过滤)

- [ ] **Task 0.3.17**: `SakuraFilter.Api/Services/AdminProductService.cs:307-342` `DeleteAsync` + `RestoreAsync` 注入 `IProductWriteStrategy`,显式开启事务 + 调用 `_writeStrategy.DeleteAsync(productId, ct)` / `RestoreAsync(productId, ct)` + 提交事务(修复 D4-4 Delete/Restore 未走双写策略)
  - **依赖**: spec v5 调整 2(IProductWriteStrategy 接口扩展)
  - **验证**: 单元测试 `Product_Delete_WriteStrategyInvoked` + `Product_Restore_WriteStrategyInvoked` 通过;`AdminProductService` 构造函数注入 `IProductWriteStrategy`

- [ ] **Task 0.3.18**: `SakuraFilter.Api/Services/AdminProductService.cs:52/150/238` `CreateAsync`/`UpdateAsync`/`DeleteAsync` 事务开始时执行 `pg_try_advisory_xact_lock(7740001)` 防止与 ETL TRUNCATE 冲突,获取失败抛 `ETL_IN_PROGRESS`(修复 D4-11 Admin 与 ETL 并发冲突)
  - **依赖**: Task 5.1.18(spec v5 ETL TRUNCATE 前 LOCK TABLE NOWAIT)
  - **验证**: 单元测试 `Admin_LockConflict_Throws_ETL_IN_PROGRESS` 通过;`_db.Database.ExecuteSqlRawAsync("SELECT pg_try_advisory_xact_lock(7740001)")` 在事务内执行

- [ ] **Task 0.3.19**: `SakuraFilter.Api/Services/AdminProductService.cs:57-59` `CreateAsync` + `UpdateAsync` 在 xref 写入前执行 `pg_try_advisory_xact_lock(7740002)` 防止与 ETL DELETE+INSERT 冲突(修复 D4-10 xref 写入与 ETL 增量冲突)
  - **依赖**: Task 0.3.18
  - **验证**: 单元测试 `Admin_XrefLock_Acquired` 通过;`ExecuteSqlRawAsync("SELECT pg_try_advisory_xact_lock(7740002)")` 在 xref 写入前调用

- [ ] **Task 0.3.20**: `SakuraFilter.Api/Controllers/AdminProductController.cs:165` `UpdateAsync` 收到 409 `XREF_CONFLICT` 时返回 `errorCode: "XREF_CONFLICT"` + `detail: "数据已被 ETL 更新,请刷新页面重试"`,前端 `AdminProductFormView.vue` `catch` 409 时 `ElMessage.warning` + 强制重新加载详情(修复 D4-21 409 错误码前端无提示)
  - **依赖**: Task 0.3.17
  - **验证**: 单元测试 `Admin_Update_XrefConflict_409` 通过;前端单元测试 `ProductForm_XrefConflict_ShowToast` 通过

- [ ] **Task 0.4.16**: `SakuraFilter.Api/Services/MeiliSearchProvider.cs` `SearchQuery` 高亮标签改用 `MARK_OPEN = "\uE000"` + `MARK_CLOSE = "\uE001"`(BMP 私用区单字符,非 C0 控制字符,修复 S4-1)
  - **依赖**: Task 0.4.8a(v4 占位符法)
  - **验证**: 单元测试 `Meili_HighlightTag_BmpPrivateArea` 通过;`SearchQuery.HighlightPreTag` 值为 `"\uE000"`

- [ ] **Task 0.4.17**: `SakuraFilter.Api/Services/MeiliSearchProvider.cs` `SanitizeFormatted` 方法重构: ① 步骤 1 把 Meilisearch 标签暂存到 `\uFDD0`/`\uFDD1`(非字符) ② 步骤 2 `WebUtility.HtmlEncode` 转义 ③ 步骤 3 移除 C0 控制字符(U+0000-U+001F,保留 \t \n \r) + BMP 私用区(U+E000-U+F8FF) + 非字符(U+FDD0-U+FDEF, U+FFFE/U+FFFF) ④ 步骤 4 还原 `\uFDD0`→`<mark>` + `\uFDD1`→`</mark>`(修复 S4-1 递归 sanitization)
  - **依赖**: Task 0.4.16
  - **验证**: 单元测试 `Meili_SanitizeFormatted_StripsUserInputMarkerLiteral` 通过(用户输入 `\uE000MO\uE000` 字面量被步骤 3 过滤);`Meili_SanitizeFormatted_RestoresMarkTag` 通过

- [ ] **Task 0.4.18**: `SakuraFilter.Api/Services/MeiliSearchProvider.cs` `BuildMr1DocumentAsync` 改用 `oem_list` 保留软删除 brand 的 OEM 3(查询不过滤 `b.DeletedAt IS NULL`),`brand_sort_order_min` 用 `publishedOemList.Where(x => x.BrandDeletedAt == null && x.BrandSortOrder.HasValue).Select(x => x.BrandSortOrder!.Value).DefaultIfEmpty(int.MaxValue).Min()`(修复 S4-11 brand 软删除与 OEM 3 可搜索语义冲突)
  - **依赖**: Task 0.4.2a(v4)
  - **验证**: 单元测试 `Meili_BuildMr1Doc_BrandSoftDeleted_Oem3StillSearchable` 通过;`Meili_BrandSortOrderMin_CaseWhen` 通过(brand 全软删除时返回 int.MaxValue)

- [ ] **Task 0.4.19**: `SakuraFilter.Api/Services/MeiliSearchProvider.cs` `Mr1IndexDoc` record 补充 `int OemListSortOrderMin` 字段 + `sortableAttributes` 配置加 `oem_list_sort_order_min`(修复 S4-16 sort_order 无冗余字段无法排序)
  - **依赖**: Task 0.4.14a(v4)
  - **验证**: `Mr1IndexDoc` 含 `OemListSortOrderMin` 属性;`UpdateIndexSettingsAsync` 中 `sortableAttributes` 含 `oem_list_sort_order_min`

### Phase 0 v5 补丁任务(Meilisearch 双索引 + Cursor 4 个)

- [ ] **Task 0.4.20**: `SakuraFilter.Api/Options/MeiliSearchOptions.cs` 新增 `WriteTargets: List<string>` 字段(默认 `["products"]`,灰度期可配置 `["products", "products_v2"]`),废弃 `IndexName` 单值字段或保留作读索引名(修复 S4-9/D4-6 _oldIndex 阶段 3 双写期为 null)
  - **依赖**: Task 0.4.13a(v4)
  - **验证**: `MeiliSearchOptions.WriteTargets` 属性存在;`appsettings.json` 含 `MeiliSearch:WriteTargets` 配置项

- [ ] **Task 0.4.21**: `SakuraFilter.Api/Services/MeiliSearchProvider.cs` ① `_index` 字段加 `volatile` 关键字 ② 新增 `RefreshWriteTargets()` 方法根据 `WriteTargets` 重建 `_writeTargets` 列表 ③ `DeleteAsync` 遍历 `_writeTargets` 全部删除,任一失败写入死信队列(`Channel<DeleteTask>` + `search_index_pending` 表持久化)(修复 S4-9/S4-10/D4-6 双索引状态机同步)
  - **依赖**: Task 0.4.20
  - **验证**: 单元测试 `Meili_DeleteAsync_AllWriteTargetsInvoked` 通过(2 个 WriteTargets 时 2 次 DeleteDocumentsAsync 调用);`Meili_DeleteAsync_DeadLetterOnFailure` 通过(模拟 1 个索引失败,死信队列写入)

- [ ] **Task 0.4.22**: `SakuraFilter.Api/Services/MeiliSearchProvider.cs` `BuildMr1DocumentAsync` 中 `OemBrandsStr` / `OemNo3sStr` 分隔符从 `|` 改为空格(对齐 Meilisearch `separatorTokens` 配置,修复 S4-13 整 token 索引问题)
  - **依赖**: Task 0.4.14a(v4)
  - **验证**: 单元测试 `Meili_OemBrandsStr_SpaceSeparated` 通过(`["BOSCH", "DENSO"]` → `"BOSCH DENSO"`);Meilisearch filter `oem_list_published_brands IN [BOSCH]` 命中

- [ ] **Task 0.4.23**: `SakuraFilter.Api/Services/MeiliSearchProvider.cs` 新增 `BuildBrandFilter(List<string> oemBrands, string matchMode)` 方法: 单值 `IN [x]` / 多值 OR `IN [a, b, c]` / 多值 AND `oem_list_published_brands IN [a] AND oem_list_published_brands IN [b]`(修复 S4-6 filter 语法不完整)
  - **依赖**: Task 0.4.22
  - **验证**: 单元测试 `Meili_BuildBrandFilter_Single` / `Meili_BuildBrandFilter_Or` / `Meili_BuildBrandFilter_And` 通过

### Phase 1 v5 补丁任务(3 个)

- [ ] **Task 1.2.13b**: `SakuraFilter.Api/Models/SearchRequest.cs` 新增 `public const int MaxTokenCount = 10;` 常量 + `public string OemBrandMatchMode { get; set; } = "OR";` 字段(单值/AND/OR,修复 S4-3 token 无上限 + S4-6 多品牌 filter 语义)
  - **依赖**: Task 1.2.1(v4)
  - **验证**: `SearchRequest.MaxTokenCount` 常量存在;`SearchRequest.OemBrandMatchMode` 属性存在;单元测试 `SearchRequest_DefaultMatchMode_IsOR` 通过

- [ ] **Task 1.2.14a**: `SakuraFilter.Api/Services/PostgresSearchProvider.cs` PG 兜底 keyset 分页 SQL 末尾追加 `p.id` 作为 UNIQUE 兜底字段: `WITH ranked AS (... GROUP BY p.id, p.mr_1, p.updated_at) SELECT r.* FROM ranked r WHERE (@cursor_updated_at IS NULL OR (r.brand_sort_order_min, r.oem_list_sort_order_min, r.updated_at, r.id) > (@cursor_brand_sort, @cursor_oem_sort, @cursor_updated_at::timestamptz, @cursor_id::bigint)) ORDER BY r.brand_sort_order_min ASC NULLS LAST, r.oem_list_sort_order_min ASC NULLS LAST, r.updated_at DESC, r.id ASC LIMIT 20`(修复 S4-4 keyset 排序字段非 UNIQUE 跳页)
  - **依赖**: Task 1.2.10a(v4)
  - **验证**: 单元测试 `PG_KeysetPagination_FourTuple_NoSkip` 通过(连续翻页 50 次无重复无跳页);`PG_KeysetPagination_CursorIncludesId` 通过(cursor 字符串含 id 字段)

- [ ] **Task 1.2.15a**: `SakuraFilter.Api/Services/PostgresSearchProvider.cs` PG 兜底 `tokens.Take(MaxTokenCount)` 限制查询 token 数量 + 短关键词(< 3 字符)走精确匹配(`oem_no_3 = @token` 或 `oem_brand = @token`,不走 ILIKE)(修复 S4-3 token 无上限 + S4-12 短关键词 ILIKE 全表扫)
  - **依赖**: Task 1.2.13b
  - **验证**: 单元测试 `PG_Search_TokenLimit_10` 通过(11 个 token 截断为 10);`PG_Search_ShortKeyword_ExactMatch` 通过(2 字符 "BO" 走 `=` 不走 ILIKE)

### Phase 3 v5 补丁任务(1 个)

- [ ] **Task 3.2.12**: `SakuraFilter.Api/Services/EtlImportService.cs:935-937` `cascade=false` 路径执行前先 `DROP CONSTRAINT fk_product_images_product` + `DROP CONSTRAINT fk_xrefs_product` 等所有 FK,TRUNCATE products/cross_references/product_images/machine_applications 后再 `ADD CONSTRAINT` 重建 FK;或直接改用 `DELETE FROM products; DELETE FROM cross_references; DELETE FROM product_images; DELETE FROM machine_applications;`(按 FK 反向顺序删除,修复 D4-19 cascade=false TRUNCATE 与 FK ON DELETE CASCADE 语义冲突导致级联清空)

### Phase 4 v5 补丁任务(9 个)

- [ ] **Task 4.1.17**: `SakuraFilter.Api/Pages/Products/Detail.cshtml` 在 `<script type="module" src="~/js/product-detail-client.js" defer></script>` 后追加 `<script>window.addEventListener('error', e => { if (e.target.tagName === 'SCRIPT' || e.target.tagName === 'LINK') { document.querySelectorAll('[id$="-app"]').forEach(el => el.innerHTML = '<div class="mount-fallback">JS 加载失败,请刷新重试</div>'); } }, true);</script>`(修复 F3-2 safeMount 无法捕获 module 加载失败)
  - **依赖**: Task 4.1.4
  - **验证**: 手动测试: 断网情况下访问 `/products/...` 页面,挂载点显示 "JS 加载失败" 提示而非空白

- [ ] **Task 4.1.18**: `SakuraFilter.Api/Pages/Products/Detail.cshtml` `<script type="module">` 跨域部署时加 `crossorigin="use-credentials"` 属性 + nginx 配置 `add_header Access-Control-Allow-Origin "https://frontend.example.com" always;` + `add_header Access-Control-Allow-Credentials "true" always;`(修复 F3-14 跨域 module 加载失败)
  - **依赖**: Task 4.1.17
  - **验证**: 部署后 `curl -I -H "Origin: https://frontend.example.com" https://cdn.example.com/js/product-detail-client.js` 返回 `Access-Control-Allow-Origin: https://frontend.example.com` + `Access-Control-Allow-Credentials: true`

- [ ] **Task 4.1.19**: `SakuraFilter.Api/wwwroot/js/product-detail-client.ts` `safeMount` catch 块内调用 `import { captureException } from '@sentry/browser'; captureException(err, { tags: { mr1: mount.dataset.mr1, oem3: mount.dataset.oem3 } });`(修复 F3-9 JS 加载/挂载失败无监控)
  - **依赖**: Task 4.1.17
  - **验证**: 手动测试: 注入错误版本的 product-detail-client.js,Sentry dashboard 收到事件 + tags 含 mr1/oem3

- [ ] **Task 4.1.20**: spec.md L1899 修正 JSON 数据岛描述: "安全保证来自 `System.Text.Json.JsonSerializer.Serialize` 的 `JavaScriptEncoder.Default` 默认转义 `<`/`>`/`&`/`'`/`\"`,必须用 `@Json.Serialize(Model.Product)` 输出,严禁 `@Html.Raw(Model.ProductJson)`"(修复 F3-1 JSON 数据岛安全描述不完整)
  - **依赖**: 无
  - **验证**: spec.md L1899 含 `JavaScriptEncoder.Default` + `@Json.Serialize` + 严禁 `@Html.Raw` 三要素;`Detail.cshtml` 用 `@Json.Serialize` 而非 `@Html.Raw`

- [ ] **Task 4.5.11**: `SakuraFilter.Api/Services/CursorHmac.cs` ① 改用 `IOptions<CursorHmacOptions>` 注入 `CurrentKey` + `PreviousKey` + `LegacyCutoffTs`(默认 `DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()`,部署时配置) ② `SignV2` 方法签名追加 `long id` 参数: `SignV2(string updatedAtIso, string mr1, int pageNum, long id)` ③ payload 格式 `v2:{expUnixTs}|{tsB64}|{mr1B64}|{pageNum}|{id}` ④ `VerifyAndExtractV2` 返回四元组 `(updatedAtIso, mr1, pageNum, id)` ⑤ `pageNum > 1000` 抛 `CURSOR_PAGE_TOO_DEEP`(修复 D4-1 LEGACY_CUTOFF_TS 硬编码 + S4-4 cursor 缺 id 字段)
  - **依赖**: Task 4.5.10(v4)
  - **验证**: 单元测试 `CursorHmac_SignV2_IncludesId` 通过;`CursorHmac_VerifyAndExtractV2_ReturnsFourTuple` 通过;`CursorHmac_LegacyCutoffTs_FromConfig` 通过;`appsettings.json` 含 `CursorHmac:CurrentKey` + `CursorHmac:PreviousKey` + `CursorHmac:LegacyCutoffTs` 配置项

- [ ] **Task 4.5.12**: `SakuraFilter.Api/Services/IProductDetailService.cs` `BuildSlug` 方法改用单一逻辑(修复 F3-3 中文 slugify 顺序混乱):
  ```csharp
  public static string BuildSlug(string raw)
  {
      if (string.IsNullOrWhiteSpace(raw)) return "untyped";
      var lower = raw.ToLowerInvariant();
      var escaped = Uri.EscapeDataString(lower);  // 中文 → %XX%XX%XX
      var slug = Regex.Replace(escaped, "[^a-zA-Z0-9%-]", "-");  // % 保留
      slug = Regex.Replace(slug, "-+", "-").Trim('-');
      if (slug.Length > 60) slug = slug[..60];
      return string.IsNullOrEmpty(slug) ? "untyped" : slug;
  }
  ```
  - **依赖**: 无
  - **验证**: 单元测试 `BuildSlug_Chinese_EscapedPreserved` 通过(`"机油滤芯"` → `"%e6%9c%ba%e6%b2%b9%e6%bb%a4%e8%8a%af"`);`BuildSlug_SpecialChar_Hyphenated` 通过(`"Oil-Filter/SP"` → `"oil-filter-sp"`);`BuildSlug_Empty_ReturnsUntyped` 通过

- [ ] **Task 4.5.13**: `SakuraFilter.Api/Services/IProductDetailService.cs` `BuildProductUrl` 方法末尾附加 `mr_1` 末 6 位防 slug 冲突: `var mr1Suffix = p.Mr1.Length > 6 ? p.Mr1[^6..] : p.Mr1; return $"/products/{pn1Slug}-{mr1Suffix}/{pn2Slug}/{brandSlug}/{oem3Slug}".ToLowerInvariant();`(修复 F3-10 多产品同 pn1/pn2/brand/oem3 时 slug 冲突)
  - **依赖**: Task 4.5.12
  - **验证**: 单元测试 `BuildProductUrl_Mr1Suffix_PreventsCollision` 通过(两个不同 MR.1 但同 pn1/pn2/brand/oem3 的产品 URL 不同);`BuildProductUrl_ShortMr1_FullString` 通过(MR.1 长度 < 6 时用完整字符串)

- [ ] **Task 4.5.14**: `frontend/src/utils/http.ts` CURSOR 自动重置改用 `router.replace({ path: route.path, query: { ...route.query, page: 1 } })` + `sessionStorage.setItem('cursor-reset-toast', '1')`,在 `App.vue` mounted 时检查 sessionStorage 显示一次性 `ElMessage.warning('分页游标已过期,已重置到第 1 页')`(修复 F3-5 CURSOR 重置整页刷新体验差)
  - **依赖**: Task 0.5.5(v4)
  - **验证**: 前端单元测试 `Http_CursorExpired_RouterReplace` 通过(不触发 `window.location.reload`);`App_CursorResetToast_OneTimeShow` 通过

- [ ] **Task 4.5.15**: `frontend/src/api/index.ts` + `frontend/src/views/public/SearchView.vue` 特性检测 `typeof searchApi.aggregate === 'function'` + 旧 API `searchApi.search` fallback: 优先调 `searchApi.aggregate(req, { signal })`,404 `ENDPOINT_NOT_FOUND` 时 fallback 到 `searchApi.search(req, { signal })` + 前端聚合 oemList(修复 F3-13 旧 API 兼容性未明)
  - **依赖**: Task 1.3.6(v4)
  - **验证**: 前端单元测试 `Search_FallbackToOldApi_On404` 通过(mock `aggregate` 返回 404,验证 `search` 被调用);`Search_UseAggregate_WhenAvailable` 通过(mock `aggregate` 返回 200,验证 `search` 不被调用)

### Phase 5 v5 补丁任务(4 个)

- [ ] **Task 5.1.19**: `SakuraFilter.Api/Services/EtlImportService.cs:1212-1220` `LoadExistingOemMapAsync` 同时返回 mr_1 map + oem_2 map,JSONL 字段优先匹配 mr_1,mr_1 缺失时 fallback 到 oem_2(修复 D4-20 旧数据无 mr_1 时 ETL 无法关联)
  - **依赖**: Task 5.1.18(v4)
  - **验证**: 单元测试 `Etl_LoadExistingOemMap_DualKey` 通过(返回 `Dictionary<(string mr1, string oem2), ExistingXref>`);`Etl_Import_FallbackToOem2_WhenMr1Missing` 通过(JSONL 行 mr_1 为空 + oem_2 = "ABC123" 时正确匹配)

- [ ] **Task 5.1.20**: `SakuraFilter.Api/Services/EtlImportService.cs` 新增 `CleanupOrphanImagesAsync(CancellationToken ct)` 方法: ① 遍历所有 `IObjectStorage` 实现(MinIO + Aliyun OSS) ② 查 `product_images WHERE uploaded_at < now() - interval '1 hour' AND product_id IS NULL`(孤立主图) + `product_images WHERE product_id IS NOT NULL AND product_id NOT IN (SELECT id FROM products)`(产品已删除但图片残留) ③ 扠除 `IObjectStorage.DeleteObjectAsync(key)` + `product_images` 表对应行(修复 D4-15/D4-16 多存储后端孤儿图片清理)
  - **依赖**: Task 5.1.19
  - **验证**: 单元测试 `Etl_CleanupOrphanImages_MultiBackend` 通过(mock 2 个 IObjectStorage,验证 2 次删除调用);`Etl_CleanupOrphanImages_TimestampFilter` 通过(1 小时内的图片不删除)

- [ ] **Task 5.1.21**: `SakuraFilter.Api/Services/XrefOemBrandService.cs` 实现 `ApplyChangeAsync(string brand, bool isDeleted)` 统一方法: ① Update/SoftDelete/Restore 全部调用此方法 ② 内部 `Channel<string>.Writer.WriteAsync(brand)` 触发 Brand sort_order 变更后台重建 ③ Channel 写入失败 fallback 到 `search_index_pending` 表持久化(`INSERT INTO search_index_pending (mr_1, action, created_at) SELECT mr_1, 'rebuild', now() FROM cross_references WHERE oem_brand = @brand`)(修复 D4-7 brand 变更无 Channel 写入 + D4-8 Channel 崩溃丢任务 + S4-7 单点故障)
  - **依赖**: Task 0.4.15(v4 Brand sort_order 变更后台重建)
  - **验证**: 单元测试 `XrefOemBrand_ApplyChange_ChannelWrite` 通过(Update/SoftDelete/Restore 都触发 Channel 写入);`XrefOemBrand_ApplyChange_FallbackToDb` 通过(Channel.Writer 抛异常时 fallback 到 `search_index_pending` 表 INSERT);`XrefOemBrand_Restore_TriggersRebuild` 通过(brand 从软删除恢复触发 Channel 写入)

- [ ] **Task 5.1.22**: `SakuraFilter.Api/Workers/IndexReplayWorker.cs` 后台轮询改造: ① `SELECT mr_1, action FROM search_index_pending ORDER BY created_at LIMIT 100 FOR UPDATE SKIP LOCKED`(跳过其他实例锁定的行) ② 每行处理时 `pg_advisory_xact_lock(mr1_hash)` 防跨实例重复处理(mr1_hash = `hashtext(mr_1)` 取模 1000000) ③ 处理成功 `DELETE FROM search_index_pending WHERE mr_1 = @mr1` ④ 失败 `UPDATE search_index_pending SET retry_count = retry_count + 1, last_error = @err WHERE mr_1 = @mr1`,retry_count > 3 时标记 `is_dead = true`(修复 S4-8 多实例重复处理)
  - **依赖**: Task 5.1.21
  - **验证**: 单元测试 `IndexReplayWorker_SkipLocked` 通过(2 个实例并发处理 100 条,无重复);`IndexReplayWorker_AdvisoryLock_PreventsDuplicate` 通过(同 mr_1 被同时提交 2 次,只处理 1 次);`IndexReplayWorker_DeadLetter_After3Retries` 通过(retry_count = 4 时 is_dead = true)

---

## Task Dependencies(v5 补丁任务)

> v5 补丁任务的关键依赖链:
> - **Task 0.2.19** → 独立(精度配置)
> - **Task 0.2.20** → 独立(spec 同步)
> - **Task 0.2.21** → 依赖 Task 0.3.15(v4 oem_2 反向更新)
> - **Task 0.2.22** → 依赖 Task 3.2.10(v4 增量更新)
> - **Task 0.2.23** → 依赖 Task 3.2.11(v4 naming_field 语义)
> - **Task 0.2.24** → 依赖 Task 0.1.1(v4 迁移脚本)
> - **Task 0.3.16** → 依赖 Task 0.3.12(v4 ValidateForm 控制字符)
> - **Task 0.3.17** → 依赖 spec v5 调整 2(IProductWriteStrategy 接口)
> - **Task 0.3.18** → 依赖 Task 5.1.18(v4 ETL TRUNCATE LOCK TABLE)
> - **Task 0.3.19** → 依赖 Task 0.3.18
> - **Task 0.3.20** → 依赖 Task 0.3.17
> - **Task 0.4.16** → 依赖 Task 0.4.8a(v4 占位符法)
> - **Task 0.4.17** → 依赖 Task 0.4.16
> - **Task 0.4.18** → 依赖 Task 0.4.2a(v4 oem_list 软删除 brand 过滤)
> - **Task 0.4.19** → 依赖 Task 0.4.14a(v4 Mr1IndexDoc 扁平化)
> - **Task 0.4.20** → 依赖 Task 0.4.13a(v4 双索引灰度)
> - **Task 0.4.21** → 依赖 Task 0.4.20
> - **Task 0.4.22** → 依赖 Task 0.4.14a
> - **Task 0.4.23** → 依赖 Task 0.4.22
> - **Task 1.2.13b** → 依赖 Task 1.2.1(v4 SearchRequest)
> - **Task 1.2.14a** → 依赖 Task 1.2.10a(v4 keyset 分页)
> - **Task 1.2.15a** → 依赖 Task 1.2.13b
> - **Task 3.2.12** → 依赖 Task 0.1.2(v4 清理脚本)
> - **Task 4.1.17** → 依赖 Task 4.1.4(v4 client mount)
> - **Task 4.1.18** → 依赖 Task 4.1.17
> - **Task 4.1.19** → 依赖 Task 4.1.17
> - **Task 4.1.20** → 独立(spec 同步)
> - **Task 4.5.11** → 依赖 Task 4.5.10(v4 CursorHmac)
> - **Task 4.5.12** → 独立
> - **Task 4.5.13** → 依赖 Task 4.5.12
> - **Task 4.5.14** → 依赖 Task 0.5.5(v4 http.ts 拦截器)
> - **Task 4.5.15** → 依赖 Task 1.3.6(v4 api/index.ts)
> - **Task 5.1.19** → 依赖 Task 5.1.18(v4 LoadExistingOemMap)
> - **Task 5.1.20** → 依赖 Task 5.1.19
> - **Task 5.1.21** → 依赖 Task 0.4.15(v4 Brand sort_order 后台重建)
> - **Task 5.1.22** → 依赖 Task 5.1.21

---

## v6 补丁任务清单(共 33 个,Phase 0-5 分布)

> v6 修订: 修复第五轮(即第六轮迭代)审查发现的 37 个衍生漏洞
> 任务分布: Phase 0 (19) + Phase 1 (3) + Phase 3 (1) + Phase 4 (7) + Phase 5 (3) = 33

### Phase 0 v6 补丁任务(19 个 — 数据关联 8 + 检索 8 + FK 3)

- [ ] **Task 0.1.25**: `SakuraFilter.Infrastructure/Data/ProductDbContext.cs` OnModelCreating 添加 FK 配置(修复 D5-7)
  - [ ] 0.1.25.1: `CrossReference.Product` 配置: `HasOne(x => x.Product).WithMany(p => p.CrossReferences).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade)`
  - [ ] 0.1.25.2: `MachineApplication.Product` 配置: 同上
  - [ ] 0.1.25.3: `ProductImage.Product` 配置: 同上
  - [ ] 0.1.25.4: 添加导航属性 `Product.CrossReferences` / `Product.MachineApplications` / `Product.Images` (ICollection<T>)
  - **验证**: `dotnet build` 通过;`ProductDbContext_FkConfiguration` 单元测试通过(检查 FK 是否声明)
  - **依赖**: Task 0.2.7(v4 OnModelCreating)

- [ ] **Task 0.1.26**: `backend/migrations/019_v6_truncate_cascade.sql` ETL TRUNCATE 改用 CASCADE 单条 SQL(修复 D5-7)
  - [ ] 0.1.26.1: 替换 EtlImportService.cs 中的 TRUNCATE 逻辑,从分表 DROP FK → TRUNCATE → ADD FK 改为:
    ```sql
    TRUNCATE products, cross_references, machine_applications, product_images RESTART IDENTITY CASCADE
    ```
  - [ ] 0.1.26.2: 脚本头注释标注"v6 修订: 依赖 ProductDbContext FK CASCADE 配置(Task 0.1.25)"
  - [ ] 0.1.26.3: 删除旧的 `DROP CONSTRAINT` + `ADD CONSTRAINT` SQL(v5 方案在无 FK 场景下是无操作)
  - **验证**: `Truncate_CascadesToChildren` 单元测试通过(TRUNCATE products 后 cross_references 行数为 0);`Fk_AddedAndDropped` 单元测试通过
  - **依赖**: Task 0.1.25

- [ ] **Task 0.2.25**: EF Core 迁移 `AddForeignKeysV6`(修复 D5-7)
  - [ ] 0.2.25.1: `dotnet ef migrations add AddForeignKeysV6` 生成迁移
  - [ ] 0.2.25.2: UP 脚本:
    ```sql
    ALTER TABLE cross_references ADD CONSTRAINT fk_xrefs_product FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE CASCADE;
    ALTER TABLE machine_applications ADD CONSTRAINT fk_machine_apps_product FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE CASCADE;
    ALTER TABLE product_images ADD CONSTRAINT fk_product_images_product FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE CASCADE;
    ```
  - [ ] 0.2.25.3: DOWN 脚本: `ALTER TABLE ... DROP CONSTRAINT ...` 三条
  - [ ] 0.2.25.4: 迁移头注释标注"v6 修订: 为 TRUNCATE CASCADE 提供基础(Task 0.1.26)"
  - **验证**: `dotnet ef migrations script --idempotent` 无报错;`psql \d cross_references` 显示 FK
  - **依赖**: Task 0.1.25

- [ ] **Task 0.2.26**: `SakuraFilter.Core/Entities/Product.cs` 添加导航属性(修复 D5-7)
  - [ ] 0.2.26.1: Product 类加 `public ICollection<CrossReference> CrossReferences { get; set; } = new List<CrossReference>();`
  - [ ] 0.2.26.2: Product 类加 `public ICollection<MachineApplication> MachineApplications { get; set; } = new List<MachineApplication>();`
  - [ ] 0.2.26.3: Product 类加 `public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();`
  - **验证**: `dotnet build` 通过;ModelSnapshot 与迁移一致
  - **依赖**: Task 0.1.25

- [ ] **Task 0.3.21**: `AdminProductService.cs` advisory_xact_lock 调用位置明确化(修复 D5-1)
  - [ ] 0.3.21.1: 在 CreateAsync/UpdateAsync/DeleteAsync/RestoreAsync 紧接 `BeginTransactionAsync` 后,通过 `DbContext.Database.GetDbConnection()` 获取连接,`CreateCommand()` 执行 `SELECT pg_try_advisory_xact_lock(@key)` 复用同一事务连接
  - [ ] 0.3.21.2: lock 失败立即 `await transaction.RollbackAsync()` + 抛 `XREF_CONFLICT` (409) + 日志 `AdvisoryLockFailed` (含 key 与持有时长)
  - [ ] 0.3.21.3: lock key 常量定义: `AdminProductService = 7740002L`,`EtlImportService = 7740001L`(已在 v5 定义)
  - **验证**: `AdvisoryXactLock_BindsToTransaction` + `AdvisoryXactLock_Failure_RollsBack` 单元测试通过
  - **依赖**: Task 0.3.17(v5 IProductWriteStrategy 接口)

- [ ] **Task 0.3.22**: IProductWriteStrategy 对账脚本 mr_1 NULL 一致性(修复 D5-2)
  - [ ] 0.3.22.1: `SakuraFilter.Api/Services/ReconcileService.cs`(新增)对账 SQL:
    ```sql
    SELECT p.mr_1, m.mr_1 AS meili_mr1
    FROM products p
    LEFT JOIN meili_index_snapshot m ON p.id = m.id
    WHERE p.mr_1 IS NOT NULL AND p.mr_1 <> m.mr_1
    ```
  - [ ] 0.3.22.2: NULL 记录单独统计: `SELECT COUNT(*) FROM products WHERE mr_1 IS NULL` + 告警阈值(NULL 比例 > 5% 时告警)
  - [ ] 0.3.22.3: 对账报告分两栏: 一致性差异 + NULL 统计
  - **验证**: `Reconcile_SkipsNullMr1` + `Reconcile_NullRatioAlert` 单元测试通过
  - **依赖**: Task 0.3.17(v5 IProductWriteStrategy 接口)

- [ ] **Task 0.4.24**: `MeiliSearchProvider.cs` WriteTargets 改用 ImmutableArray + IOptionsMonitor(修复 D5-3)
  - [ ] 0.4.24.1: 改用 `IOptionsMonitor<MeiliOptions>` + `OnChange` 回调,配置变更时原子替换 `_writeTargets` 引用
  - [ ] 0.4.24.2: WriteTargets 属性返回 `IReadOnlyList<string>`,内部存储为 `ImmutableArray<string>`
  - [ ] 0.4.24.3: 配置变更时通知所有正在进行的 DeleteAsync 通过 `CancellationToken` 取消并重启
  - **验证**: `WriteTargets_HotSwap_NoException` 单元测试通过(并发 ToList 与配置变更)
  - **依赖**: Task 0.4.20(v5 WriteTargets 配置列表)

- [ ] **Task 0.4.25**: `BuildMr1DocumentAsync` brand_sort_order_min 改 long? NULL(修复 D5-6)
  - [ ] 0.4.25.1: Mr1IndexDoc.brand_sort_order_min 类型从 `int` 改为 `long?`
  - [ ] 0.4.25.2: 全软删除时 brand_sort_order_min = NULL (而非 int.MaxValue)
  - [ ] 0.4.25.3: ORDER BY 时显式声明 `NULLS LAST`,NULL 行排在最后
  - [ ] 0.4.25.4: oem_list_sort_order_min 同样处理
  - **验证**: `BuildMr1Doc_AllBrandSoftDeleted_NullsLast` + `BuildMr1Doc_PartialBrandSoftDeleted` 单元测试通过
  - **依赖**: Task 0.4.18(v5 oem_list 软删除 brand 过滤)

- [ ] **Task 0.4.26**: `BuildMr1DocumentAsync` oem_list_sort_order_min 软删除 brand 一致性(修复 S5-10)
  - [ ] 0.4.26.1: oem_list_sort_order_min 计算时: `MIN(CASE WHEN b.is_deleted THEN NULL ELSE x.sort_order END)`
  - [ ] 0.4.26.2: 与 brand_sort_order_min 统一规则(软删除 brand 用 NULL)
  - [ ] 0.4.26.3: ORDER BY NULLS LAST 统一处理
  - **验证**: `SortOrderMin_SoftDeletedBrand_Null` + `SortOrderMin_ConsistentBetweenBrandAndOemList` 单元测试通过
  - **依赖**: Task 0.4.25

- [ ] **Task 0.4.27**: StripControlChars 与 BuildSlug 调用顺序文档化(修复 D5-8)
  - [ ] 0.4.27.1: 在 `BuildSlug` 方法头部加 `// WHY: 调用顺序: StripControlChars 在 BuildSlug 之前(先剥离再编码)`
  - [ ] 0.4.27.2: 在 `AdminProductFormView.vue` 表单提交逻辑中明确调用顺序
  - [ ] 0.4.27.3: 性能预算文档: 60 字符输入总耗时 < 10μs
  - **验证**: `StripControlChars_Before_BuildSlug_Order` 单元测试通过(含控制字符的输入)
  - **依赖**: Task 0.4.16(v5 SanitizeFormatted 占位符法)

- [ ] **Task 0.4.28**: `MeiliSearchProvider.cs` SanitizeFormatted 步骤 0 + 步骤 3 修复(修复 S5-1/S5-4/S5-12)
  - [ ] 0.4.28.1: 步骤 0(新增): 在步骤 1 之前,先过滤用户输入中已有的 \uFDD0/\uFDD1 字面量:
    ```csharp
    input = input.Replace("\uFDD0", "").Replace("\uFDD1", "");
    ```
  - [ ] 0.4.28.2: 步骤 3: 还原逻辑改为:
    ```csharp
    foreach (var c in escaped)
    {
        if (c == '\uFDD0') sb.Append("<mark>");
        else if (c == '\uFDD1') sb.Append("</mark>");
        else sb.Append(c);
    }
    ```
  - [ ] 0.4.28.3: 步骤 4(新增): 还原后再次扫描,如果仍有 \uFDD0/\uFDD1 残留(用户输入字面量),记日志 + 移除
  - [ ] 0.4.28.4: 改用 BMP 私用区 U+E000/U+E001(与 v5 spec 一致),而非 \uFDD0/\uFDD1(Unicode 非字符)
  - **验证**: `SanitizeFormatted_PreservesHighlight` + `SanitizeFormatted_StripsUserLiteralFDD0` + `PlaceholderBmp_CrossComponentCompatible` 单元测试通过
  - **依赖**: Task 0.4.16(v5 占位符法)

- [ ] **Task 0.4.29**: `PostgresSearchProvider.cs` keyset 显式 DESC + COALESCE 哨兵(修复 S5-2/S5-3)
  - [ ] 0.4.29.1: keyset WHERE 条件改用显式方向:
    ```sql
    WHERE ROW(COALESCE(b, 9223372036854775807), COALESCE(o, 9223372036854775807), u, i) < ROW(@prev_b, @prev_o, @prev_u, @prev_i)
    ORDER BY b ASC, o ASC, u DESC, i DESC LIMIT 20
    ```
  - [ ] 0.4.29.2: long.MaxValue 作为"无有效 brand"的哨兵,与 NULLS LAST 语义对齐
  - [ ] 0.4.29.3: COALESCE 不影响索引使用(PostgreSQL 14+ 支持表达式索引)
  - **验证**: `Keyset_SecondPage_ReturnsCorrectRows` + `Keyset_DescDirection_Consistent` + `Keyset_NullBrandSortOrder_PaginatesCorrectly` 单元测试通过
  - **依赖**: Task 1.2.14a(v5 keyset 分页)

- [ ] **Task 0.4.30**: `MeiliSearchProvider.cs` tokens.Take 权重排序 + 停用词剔除(修复 S5-5)
  - [ ] 0.4.30.1: 截断前先按 token 长度降序排序(长 token 通常更重要)
  - [ ] 0.4.30.2: 停用词优先剔除: 先过滤 stopWords,再 Take
  - [ ] 0.4.30.3: MaxTokenCount 从配置注入,默认 10,可调
  - **验证**: `Tokens_Take_PreservesImportantTokens` + `Tokens_StopWordsFiltered` 单元测试通过
  - **依赖**: Task 1.2.13b(v5 SearchRequest MaxTokenCount)

- [ ] **Task 0.4.31**: `MeiliSearchProvider.cs` BuildBrandFilter 转义 + 长度上限(修复 S5-6/S5-15)
  - [ ] 0.4.31.1: 实现工具方法 `EscapeMeiliFilterValue(string value)`:
    ```csharp
    public static string EscapeMeiliFilterValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
    ```
  - [ ] 0.4.31.2: BuildBrandFilter AND 模式品牌数上限 20,超出抛 `BRAND_FILTER_TOO_LONG`
  - [ ] 0.4.31.3: BuildBrandFilter 调用 EscapeMeiliFilterValue 转义品牌名
  - **验证**: `BuildBrandFilter_EscapesQuote` + `BuildBrandFilter_EscapesBackslash` + `BuildBrandFilter_TooManyBrands` 单元测试通过
  - **依赖**: Task 0.4.20(v5 WriteTargets 配置列表)

- [ ] **Task 0.4.32**: `BuildMr1DocumentAsync` OemBrandsStr 改用 \u0001 分隔符(修复 S5-7)
  - [ ] 0.4.32.1: OemBrandsStr 内部分隔符改用 `\u0001` (SOH, Start of Heading, 非可见字符)
  - [ ] 0.4.32.2: Meilisearch separatorTokens 配置加入 `\u0001`
  - [ ] 0.4.32.3: Meilisearch searchableAttributes 配置不变(OemBrandsStr 作为一个字段)
  - **验证**: `OemBrandsStr_SpaceInBrandName_Preserved` + `OemBrandsStr_SohSeparator_Tokenized` 单元测试通过
  - **依赖**: Task 0.4.18(v5 OemBrandsStr 空格分隔)

- [ ] **Task 0.4.33**: `MeiliSearchProvider.cs` WriteTargets Channel 容量限制(修复 S5-8)
  - [ ] 0.4.33.1: Channel 容量限制为 10000: `Channel.CreateBounded<DeleteTask>(10000)`
  - [ ] 0.4.33.2: 满时 `WriteAsync` 阻塞等待(默认超时 30 秒) + 日志告警 `DeadLetterQueueFull`
  - [ ] 0.4.33.3: 超时后写入持久化 `search_index_pending` 表(DB 兜底)
  - **验证**: `DeadLetterQueue_Full_FallsBackToDb` + `DeadLetterQueue_Full_LogsAlert` 单元测试通过
  - **依赖**: Task 0.4.20(v5 WriteTargets 配置列表)

- [ ] **Task 0.4.34**: `CursorHmac.cs` SignV2 稳定签名 + long.TryParse(修复 S5-9)
  - [ ] 0.4.34.1: SignV2 签名内容: `mr1 + ":" + id + ":" + brandSortOrderMin + ":" + updatedAtTicks` (不含 expUnixTs)
  - [ ] 0.4.34.2: expUnixTs 作为 cursor 前缀明文: `cursor = base64(expUnixTs + "." + hmac签名)`
  - [ ] 0.4.34.3: 验签时: 先解析 expUnixTs 判断过期,再验签,最后 `long.TryParse` 防异常
  - **验证**: `SignV2_StableSignature` + `VerifyAndExtractV2_MalformedCursor_NoException` 单元测试通过
  - **依赖**: Task 4.5.11(v5 CursorHmac 双 key)

- [ ] **Task 0.4.35**: `PostgresSearchProvider.cs` 短关键词大小写不敏感(修复 S5-11)
  - [ ] 0.4.35.1: PG 短关键词匹配改用 `LOWER(oem_brand) = LOWER(@q)` (或 citext 扩展)
  - [ ] 0.4.35.2: Meilisearch 配置 `matchingStrategy: last` + 短关键词特殊处理
  - [ ] 0.4.35.3: Meilisearch/PG 行为一致性测试
  - **验证**: `ShortKeyword_CaseInsensitive_MeiliPgConsistent` 单元测试通过
  - **依赖**: Task 0.4.29(v6 keyset 修复)

- [ ] **Task 0.4.36**: `MeiliSearchProvider.cs` BMP 私用区版本支持文档 + 降级方案(修复 S5-13)
  - [ ] 0.4.36.1: 文档明确要求 Meilisearch 1.6+ (BMP 私用区稳定支持)
  - [ ] 0.4.36.2: 降级方案: Meilisearch < 1.6 改用 HTML escape + 正则还原 `<mark>` (性能略差)
  - [ ] 0.4.36.3: 启动时检测 Meilisearch 版本,低于 1.6 走降级路径
  - **验证**: `PlaceholderBmp_Meili16_Supported` + `PlaceholderBmp_DegradedPath_OlderMeili` 单元测试通过
  - **依赖**: Task 0.4.28(v6 SanitizeFormatted 修复)

- [ ] **Task 0.4.37**: `CursorHmac.cs` 双 key 轮转窗口缩短为 24h(修复 S5-14)
  - [ ] 0.4.37.1: 轮转窗口从 7 天缩短为 24 小时
  - [ ] 0.4.37.2: 文档明确: PreviousKey 必须与 CurrentKey 同等保护,泄露任一都需立即轮转
  - [ ] 0.4.37.3: 验签顺序保持(CurrentKey 优先)
  - **验证**: `CursorHmac_DualKey_RotationWindow` 单元测试通过
  - **依赖**: Task 0.4.34(v6 SignV2 稳定签名)

- [ ] **Task 0.4.38**: `search_index_pending` 定期清理(修复 S5-15)
  - [ ] 0.4.38.1: 后台任务每天凌晨 3 点清理: `DELETE FROM search_index_pending WHERE is_processed = true AND updated_at < now() - interval '30 days'`
  - [ ] 0.4.38.2: 清理日志记录删除行数
  - **验证**: `SearchIndexPending_Cleanup_30Days` 单元测试通过
  - **依赖**: Task 5.1.22(v5 IndexReplayWorker)

### Phase 1 v6 补丁任务(3 个 — 前端 XSS 修复)

- [ ] **Task 1.3.9**: `AggregateSearchView.vue` SanitizeFormatted 配合后端步骤 0(修复 S5-1/S5-4)
  - [ ] 1.3.9.1: 前端 v-html 渲染前,先用 DOMPurify 白名单只允许 `<mark>` 标签
  - [ ] 1.3.9.2: 双保险: 后端 SanitizeFormatted 步骤 0 过滤 + 前端 DOMPurify 白名单
  - **验证**: `Search_Aggregate_XssDefense` 前端单元测试通过
  - **依赖**: Task 0.4.28(v6 SanitizeFormatted 修复)

- [ ] **Task 1.3.10**: `html-sanitizer.ts` DOMPurify 白名单只允许 `<mark>` + 暂存字符过滤(修复 S5-12)
  - [ ] 1.3.10.1: DOMPurify 配置: `ALLOWED_TAGS: ['mark'], ALLOWED_ATTR: []`
  - [ ] 1.3.10.2: 输入预处理: 移除 \uFDD0/\uFDD1 字面量(与后端步骤 0 一致)
  - **验证**: `HtmlSanitizer_StripsAllExceptMark` 单元测试通过
  - **依赖**: Task 1.3.9

- [ ] **Task 1.3.11**: `frontend/src/api/index.ts` searchApi.aggregate try-catch 404 fallback(修复 F4-3)
  - [ ] 1.3.11.1: 实现 `searchWithFallback` 函数:
    ```typescript
    async function searchWithFallback(req: SearchRequest, signal?: AbortSignal): Promise<SearchResponse> {
      try {
        return await searchApi.aggregate(req, { signal })
      } catch (e) {
        if (e instanceof HttpError && e.status === 404) {
          return await searchApi.legacySearch(req, { signal })
        }
        throw e
      }
    }
    ```
  - **验证**: `AggregateApi_Fallback_On404` + `AggregateApi_Non404Error_Rethrown` 单元测试通过
  - **依赖**: Task 1.3.6(v4 api/index.ts)

### Phase 3 v6 补丁任务(1 个 — 图片清理时区)

- [ ] **Task 3.2.13**: `AdminProductImageService.cs` CleanupOrphanImagesAsync 多存储异常隔离 + UTC 时区(修复 D5-4)
  - [ ] 3.2.13.1: 每个 IObjectStorage 实现的清理用 try-catch 包裹,失败记录到日志但继续下一个
  - [ ] 3.2.13.2: 时间戳统一用 UTC: `uploaded_at < DateTime.UtcNow.AddHours(-1)` + 数据库列类型统一 `timestamptz`
  - [ ] 3.2.13.3: 单次清理失败的存储后端记录到 `cleanup_failures` 表(id/storage_backend/last_failure_at/retry_count),下次清理优先重试
  - **验证**: `CleanupOrphanImages_PartialFailure_Continues` + `CleanupOrphanImages_UtcTimezone` 单元测试通过
  - **依赖**: Task 5.1.20(v5 CleanupOrphanImagesAsync)

### Phase 4 v6 补丁任务(7 个 — 前端 URL/SEO 修复)

- [ ] **Task 4.1.21**: `BuildProductUrl` mr1Suffix 调用 BuildSlug 转义(修复 F4-1)
  - [ ] 4.1.21.1: `mr1Suffix = BuildSlug(mr1.Substring(Math.Max(0, mr1.Length - 6)))`
  - [ ] 4.1.21.2: 虽然 MR.1 校验 `^[A-Za-z0-9]{1,10}$` 已限制字符,但 BuildSlug 提供防御性兜底
  - **验证**: `BuildProductUrl_Mr1Suffix_Escaped` 单元测试通过
  - **依赖**: Task 4.1.17(v5 client mount)

- [ ] **Task 4.1.22**: `BuildSlug` %XX 大写 + TrimIncompletePercentEncoding(修复 F4-2/F4-5)
  - [ ] 4.1.22.1: 调整顺序: 先 EscapeDataString 再 lower(但只对非 %XX 部分 lower):
    ```csharp
    public static string BuildSlug(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "untyped";
        var escaped = Uri.EscapeDataString(raw);
        var lower = Regex.Replace(escaped, "%[0-9A-Fa-f]{2}|.", m => 
            m.Value.StartsWith("%") ? m.Value.ToUpperInvariant() : m.Value.ToLowerInvariant());
        var slug = Regex.Replace(lower, "[^a-zA-Z0-9%-]", "-");
        slug = Regex.Replace(slug, "-+", "-").Trim('-');
        if (slug.Length > 60) slug = TrimIncompletePercentEncoding(slug[..60]);
        return string.IsNullOrEmpty(slug) ? "untyped" : slug;
    }
    ```
  - [ ] 4.1.22.2: 实现 `TrimIncompletePercentEncoding`:
    ```csharp
    private static string TrimIncompletePercentEncoding(string s)
    {
        if (s.EndsWith("%")) return s[..^1];
        if (s.Length >= 2 && s[^2] == '%' && IsHexDigit(s[^1])) return s[..^2];
        return s;
    }
    private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
    ```
  - **验证**: `BuildSlug_PercentEncoding_UpperCase` + `BuildSlug_Truncate_PreservesPercentEncoding` + `BuildSlug_Truncate_RemovesIncompletePercent` 单元测试通过
  - **依赖**: Task 4.1.20(v5 BuildSlug 单一逻辑)

- [ ] **Task 4.5.16**: `frontend/src/utils/http.ts` 动态 import router 避免循环依赖(修复 F4-4)
  - [ ] 4.5.16.1: http.ts 401 处理改用动态 import:
    ```typescript
    if (status === 401) {
      const { default: router } = await import('@/router')
      router.replace('/login?redirect=' + encodeURIComponent(window.location.pathname))
    }
    ```
  - **验证**: `Http_401_DynamicImportRouter_NoCircular` 单元测试通过
  - **依赖**: Task 4.5.14(v5 http.ts 拦截器)

- [ ] **Task 4.5.17**: `Detail.cshtml` crossorigin + CookiePolicy SameSite=None(修复 F4-6)
  - [ ] 4.5.17.1: 文档明确: crossorigin="use-credentials" 必须配合 SameSite=None; Secure
  - [ ] 4.5.17.2: appsettings.json 加 CookiePolicy 配置: `SameSite=None, Secure=true`
  - [ ] 4.5.17.3: Program.cs 加 `app.UseCookiePolicy(new CookiePolicyOptions { MinimumSameSitePolicy = SameSiteMode.None, Secure = CookieSecurePolicy.Always })`
  - **验证**: `CookiePolicy_SameSiteNone_WithCrossOrigin` 单元测试通过
  - **依赖**: Task 4.1.17(v5 client mount)

- [ ] **Task 4.5.18**: `Detail.cshtml` error 事件处理器检查 #app 已挂载 + script 标签(修复 F4-7/F4-12)
  - [ ] 4.5.18.1: error 处理器先检查 `document.getElementById('app').children.length > 0`,已挂载则跳过
  - [ ] 4.5.18.2: 检查 `event.target` 是否为 `<script>` 标签(资源加载错误 vs 运行时错误):
    ```javascript
    window.addEventListener('error', (event) => {
      if (window.__fallbackMounted) return
      if (document.getElementById('app')?.children.length > 0) return;
      if (!event.target || !['SCRIPT', 'LINK', 'IMG'].includes(event.target.tagName)) return;
      window.__fallbackMounted = true
      mountFallback();
    }, true);
    ```
  - **验证**: `ErrorListener_SkipsMountedApp` + `ErrorListener_OnlyScriptLoad` + `MountFallback_Dedup` 单元测试通过
  - **依赖**: Task 4.1.17(v5 client mount)

- [ ] **Task 4.5.19**: `frontend/src/utils/safeSessionStorage.ts` 工具方法 + Safari 隐私模式降级(修复 F4-8)
  - [ ] 4.5.19.1: 新增 `safeSessionStorage.ts`:
    ```typescript
    const memoryStore = new Map<string, string>()
    export const safeSessionStorage = {
      setItem(key: string, value: string): void {
        try { sessionStorage.setItem(key, value) }
        catch { memoryStore.set(key, value) }
      },
      getItem(key: string): string | null {
        try { return sessionStorage.getItem(key) }
        catch { return memoryStore.get(key) ?? null }
      },
      removeItem(key: string): void {
        try { sessionStorage.removeItem(key) }
        catch { memoryStore.delete(key) }
      }
    }
    ```
  - [ ] 4.5.19.2: 所有 sessionStorage 调用替换为 safeSessionStorage
  - **验证**: `SessionStorage_SafariPrivateMode_FallbackToMemory` 单元测试通过
  - **依赖**: Task 4.5.14(v5 http.ts 拦截器)

- [ ] **Task 4.5.20**: `frontend/src/utils/errorMonitor.ts` captureException 缓冲队列 + 类型适配(修复 F4-9/F4-13)
  - [ ] 4.5.20.1: errorMonitor 加 init 状态标志 + 缓冲队列(最多 50 条):
    ```typescript
    let initialized = false
    const buffer: unknown[] = []
    export function captureException(e: unknown): void {
      if (initialized) {
        Sentry.captureException(e instanceof Error ? e : new Error(String(e)))
      } else if (buffer.length < 50) {
        buffer.push(e)
      }
    }
    export function init() {
      Sentry.init(...)
      initialized = true
      buffer.forEach(e => Sentry.captureException(e))
      buffer.length = 0
    }
    ```
  - [ ] 4.5.20.2: 类型适配层: captureException 接受 unknown,内部转换为 Error
  - **验证**: `CaptureException_BeforeInit_Buffered` + `CaptureException_TypeAdapted` 单元测试通过
  - **依赖**: Task 4.5.14(v5 http.ts 拦截器)

### Phase 5 v6 补丁任务(3 个 — ETL oem_2 多值 + 表单草稿)

- [ ] **Task 5.1.23**: `EtlImportService.cs` LoadExistingOemMapAsync oem_2 多值检测 + 拒绝 fallback(修复 D5-5)
  - [ ] 5.1.23.1: LoadExistingOemMapAsync 检测到 oem_2 多值时返回 `Dictionary<string, List<string>>` 而非 `Dictionary<string, string>`
  - [ ] 5.1.23.2: 调用方检测到多值时拒绝 fallback,记录 `Oem2Ambiguous` 告警 + 跳过该记录
  - [ ] 5.1.23.3: 告警阈值: oem_2 多值比例 > 1% 时阻断 ETL 导入,要求人工清理
  - **验证**: `LoadOemMap_Oem2Ambiguous_SkipsRecord` 单元测试通过
  - **依赖**: Task 5.1.19(v5 LoadExistingOemMapAsync)

- [ ] **Task 5.1.24**: ETL `import_skips` 表记录 oem_2 多值跳过(修复 D5-5)
  - [ ] 5.1.24.1: 新增 `import_skips` 表(id/file_name/row_number/reason/created_at)
  - [ ] 5.1.24.2: LoadExistingOemMapAsync 检测到多值时写入 import_skips 表
  - [ ] 5.1.24.3: 后台管理页面展示 import_skips 记录,供人工清理
  - **验证**: `ImportSkips_RecordOem2Ambiguous` 单元测试通过
  - **依赖**: Task 5.1.23

- [ ] **Task 5.1.25**: `AdminProductFormView.vue` FormDraft 表单数据 localStorage 持久化(修复 F4-10)
  - [ ] 5.1.25.1: 表单数据自动持久化到 localStorage(debounce 500ms):
    ```typescript
    const debouncedSave = useDebounceFn((data) => {
      localStorage.setItem(`product_draft_${mr1}`, JSON.stringify(data))
    }, 500)
    watch(formData, debouncedSave, { deep: true })
    ```
  - [ ] 5.1.25.2: 409 时提示"是否恢复本地草稿?"
  - [ ] 5.1.25.3: 草稿 7 天后自动过期清理
  - **验证**: `FormDraft_AutoSaveAndRestore` 单元测试通过
  - **依赖**: Task 4.5.14(v5 http.ts 拦截器)

---

## Task Dependencies(v6 补丁任务)

> v6 补丁任务的关键依赖链:
> - **Task 0.1.25** → 依赖 Task 0.2.7(v4 OnModelCreating)
> - **Task 0.1.26** → 依赖 Task 0.1.25
> - **Task 0.2.25** → 依赖 Task 0.1.25
> - **Task 0.2.26** → 依赖 Task 0.1.25
> - **Task 0.3.21** → 依赖 Task 0.3.17(v5 IProductWriteStrategy 接口)
> - **Task 0.3.22** → 依赖 Task 0.3.17(v5 IProductWriteStrategy 接口)
> - **Task 0.4.24** → 依赖 Task 0.4.20(v5 WriteTargets 配置列表)
> - **Task 0.4.25** → 依赖 Task 0.4.18(v5 oem_list 软删除 brand 过滤)
> - **Task 0.4.26** → 依赖 Task 0.4.25
> - **Task 0.4.27** → 依赖 Task 0.4.16(v5 SanitizeFormatted 占位符法)
> - **Task 0.4.28** → 依赖 Task 0.4.16(v5 占位符法)
> - **Task 0.4.29** → 依赖 Task 1.2.14a(v5 keyset 分页)
> - **Task 0.4.30** → 依赖 Task 1.2.13b(v5 SearchRequest MaxTokenCount)
> - **Task 0.4.31** → 依赖 Task 0.4.20(v5 WriteTargets 配置列表)
> - **Task 0.4.32** → 依赖 Task 0.4.18(v5 OemBrandsStr 空格分隔)
> - **Task 0.4.33** → 依赖 Task 0.4.20(v5 WriteTargets 配置列表)
> - **Task 0.4.34** → 依赖 Task 4.5.11(v5 CursorHmac 双 key)
> - **Task 0.4.35** → 依赖 Task 0.4.29(v6 keyset 修复)
> - **Task 0.4.36** → 依赖 Task 0.4.28(v6 SanitizeFormatted 修复)
> - **Task 0.4.37** → 依赖 Task 0.4.34(v6 SignV2 稳定签名)
> - **Task 0.4.38** → 依赖 Task 5.1.22(v5 IndexReplayWorker)
> - **Task 1.3.9** → 依赖 Task 0.4.28(v6 SanitizeFormatted 修复)
> - **Task 1.3.10** → 依赖 Task 1.3.9
> - **Task 1.3.11** → 依赖 Task 1.3.6(v4 api/index.ts)
> - **Task 3.2.13** → 依赖 Task 5.1.20(v5 CleanupOrphanImagesAsync)
> - **Task 4.1.21** → 依赖 Task 4.1.17(v5 client mount)
> - **Task 4.1.22** → 依赖 Task 4.1.20(v5 BuildSlug 单一逻辑)
> - **Task 4.5.16** → 依赖 Task 4.5.14(v5 http.ts 拦截器)
> - **Task 4.5.17** → 依赖 Task 4.1.17(v5 client mount)
> - **Task 4.5.18** → 依赖 Task 4.1.17(v5 client mount)
> - **Task 4.5.19** → 依赖 Task 4.5.14(v5 http.ts 拦截器)
> - **Task 4.5.20** → 依赖 Task 4.5.14(v5 http.ts 拦截器)
> - **Task 5.1.23** → 依赖 Task 5.1.19(v5 LoadExistingOemMapAsync)
> - **Task 5.1.24** → 依赖 Task 5.1.23
> - **Task 5.1.25** → 依赖 Task 4.5.14(v5 http.ts 拦截器)

---

## v7 补丁任务清单(共 27 个,Phase 0-5 分布)

> v7 修订: 修复第六轮(即第七轮迭代)审查发现的 33 个衍生漏洞 + 3 项 v6 事实性误判纠正
> 关键调整: E1/E2/E3 纠正 v6 对当前代码状态的事实性误判(AddForeignKeysV6 必失败/LoadExistingOemMapAsync 读 oem_no_normalized/TRUNCATE CASCADE 遗漏 product_images)

### Phase 0 v7 补丁任务(14 个 — 数据关联 6 + 检索 7 + FK 1)

- [ ] **Task 0.1.4**: 数据库迁移 `SyncFkConfigurationsV7`(修复 E1/D6-2 — 取消 AddForeignKeysV6)
  - [ ] 0.1.4.1: 删除 v6 计划的 `AddForeignKeysV6` 迁移文件(如已创建)
  - [ ] 0.1.4.2: 新建空迁移 `SyncFkConfigurationsV7`(Up/Down 为空,仅同步 ModelSnapshot)
  - [ ] 0.1.4.3: `ProductDbContext.OnModelCreating` 添加显式 HasOne 配置:
    ```csharp
    modelBuilder.Entity<ProductImage>()
        .HasOne(p => p.Product).WithMany(p => p.Images)
        .HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Cascade);
    modelBuilder.Entity<CrossReference>()
        .HasOne(x => x.Product).WithMany(p => p.CrossReferences)
        .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    modelBuilder.Entity<MachineApplication>()
        .HasOne(m => m.Product).WithMany(p => p.MachineApplications)
        .HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Cascade);
    ```
  - [ ] 0.1.4.4: 迁移头注释: "FK 已存在(20260702025150_InitialCreate),本迁移为占位同步 ModelSnapshot"
  - **验证**: `dotnet ef migrations script --idempotent` 无 42710 错误;ModelSnapshot 包含 HasOne 配置
  - **依赖**: 无(独立修复 v6 D5-7 误判)

- [ ] **Task 0.1.5**: 数据库迁移 `AddKeysetRedundantFieldsV7`(修复 S6-4)
  - [ ] 0.1.5.1: products 表加冗余字段:
    ```sql
    ALTER TABLE products ADD COLUMN brand_sort_order_min int;
    ALTER TABLE products ADD COLUMN oem_list_sort_order_min int;
    ```
  - [ ] 0.1.5.2: 创建复合表达式索引:
    ```sql
    CREATE INDEX CONCURRENTLY idx_products_keyset_v7
    ON products (
      COALESCE(brand_sort_order_min, 2147483647) ASC,
      COALESCE(oem_list_sort_order_min, 2147483647) ASC,
      updated_at DESC, id DESC
    )
    WHERE deleted_at IS NULL AND mr_1 IS NOT NULL;
    ```
  - [ ] 0.1.5.3: Product 实体加 `BrandSortOrderMin` / `OemListSortOrderMin` 属性
  - [ ] 0.1.5.4: EF Core 配置(`HasDefaultValue(null)` + 索引声明)
  - **验证**: EXPLAIN ANALYZE 显示使用 `idx_products_keyset_v7`(非 Seq Scan)
  - **依赖**: Task 0.1.4

- [ ] **Task 0.1.6**: 数据库迁移 `AddLowerExpressionIndexesV7`(修复 S6-10)
  - [ ] 0.1.6.1: 启用扩展:
    ```sql
    CREATE EXTENSION IF NOT EXISTS pg_trgm;
    ```
  - [ ] 0.1.6.2: 创建 LOWER 表达式索引:
    ```sql
    CREATE INDEX CONCURRENTLY idx_products_name1_lower 
        ON products (LOWER(product_name_1)) 
        WHERE deleted_at IS NULL AND mr_1 IS NOT NULL;
    CREATE INDEX CONCURRENTLY idx_products_name2_lower 
        ON products (LOWER(product_name_2))
        WHERE deleted_at IS NULL AND mr_1 IS NOT NULL;
    CREATE INDEX CONCURRENTLY idx_xrefs_oem3_lower 
        ON cross_references (LOWER(oem_no_3))
        WHERE is_published = true AND is_discontinued = false;
    ```
  - [ ] 0.1.6.3: 创建 trigram GIN 索引(模糊匹配):
    ```sql
    CREATE INDEX CONCURRENTLY idx_products_name1_trgm 
        ON products USING gin (product_name_1 gin_trgm_ops)
        WHERE deleted_at IS NULL AND mr_1 IS NOT NULL;
    ```
  - **验证**: EXPLAIN ANALYZE 显示使用 `idx_products_name1_lower`(非 Seq Scan)
  - **依赖**: 无

- [ ] **Task 0.1.7**: 数据库迁移 `AddCleanupFailuresStatusV7`(修复 D6-6/D6-9)
  - [ ] 0.1.7.1: cleanup_failures 表加字段:
    ```sql
    ALTER TABLE cleanup_failures ADD COLUMN status varchar(20) NOT NULL DEFAULT 'pending';
    ALTER TABLE cleanup_failures ADD COLUMN cleaned_at timestamptz;
    ALTER TABLE cleanup_failures ADD COLUMN retry_count int NOT NULL DEFAULT 0;
    ALTER TABLE cleanup_failures ADD COLUMN last_error text;
    ALTER TABLE cleanup_failures ADD CONSTRAINT chk_cleanup_status 
        CHECK (status IN ('pending', 'in_progress', 'success', 'failed', 'failed_permanent'));
    CREATE INDEX idx_cleanup_failures_status 
        ON cleanup_failures(status) 
        WHERE status IN ('pending', 'failed');
    ```
  - [ ] 0.1.7.2: CleanupFailure 实体加对应属性
  - [ ] 0.1.7.3: EF Core 配置(`HasDefaultValue` + CHECK 约束声明)
  - **验证**: `psql \d cleanup_failures` 显示新字段 + 约束 + 索引
  - **依赖**: 无

- [ ] **Task 0.3.7**: AdminProductService.PurgeAllAsync 实现(修复 E3/D6-1)
  - [ ] 0.3.7.1: 新增 `PurgeAllAsync(CancellationToken ct)` 方法,步骤:
    1. 查询所有图片 URL: `_db.ProductImages.Select(img => img.ImageUrl).ToListAsync(ct)`
    2. 批量删除对象存储中的图片: `_storage.DeleteBatchAsync(imageUrls, ct)`
    3. TRUNCATE 所有业务表(显式列出):
       ```sql
       TRUNCATE products, cross_references, machine_applications, product_images, 
                search_index_pending, cleanup_failures, partition6_placeholder
       RESTART IDENTITY CASCADE;
       ```
    4. 清理 Meilisearch 索引:遍历 WriteTargets 调用 `DeleteAllDocumentsAsync`
  - [ ] 0.3.7.2: TRUNCATE 包在事务中 + try-catch 回滚
  - [ ] 0.3.7.3: 步骤 2 失败不阻断(记录警告,继续 TRUNCATE)
  - **验证**: 集成测试 `PurgeAll_ImagesDeleted` + `PurgeAll_StorageFailure_NoBlock` 通过
  - **依赖**: Task 0.1.4

- [ ] **Task 0.3.8**: AdminProductService.UpdateProductRedundantFieldsAsync 实现(修复 S6-4)
  - [ ] 0.3.8.1: 新增方法,查询 cross_references + oem_brand_dict 计算 brand_sort_order_min + oem_list_sort_order_min
  - [ ] 0.3.8.2: CreateAsync/UpdateAsync 末尾调用此方法(同事务内)
  - [ ] 0.3.8.3: cross_references 变更(sort_order/oem_brand_id)也触发更新
  - **验证**: 单元测试 `UpdateRedundantFields_ConsistentWithJoin` 通过
  - **依赖**: Task 0.1.5

- [ ] **Task 0.4.12**: MeiliSearchProvider.BuildMr1DocumentAsync 数组字段改造(修复 S6-5/S6-8/S6-12)
  - [ ] 0.4.12.1: `oem_list_published_brands` 改数组类型:
    ```csharp
    doc["oem_list_published_brands"] = publishedOemList
        .Select(x => x.OemBrand).Distinct().ToArray();
    doc["oem_list_published_oem3s"] = publishedOemList
        .Select(x => x.OemNo3).Distinct().ToArray();
    ```
  - [ ] 0.4.12.2: `brand_sort_order_min_or_max` 冗余字段(NULL → long.MaxValue,修复 D6-5):
    ```csharp
    var brandSortOrderMin = publishedOemList
        .Select(x => x.OemBrand?.SortOrder ?? int.MaxValue)
        .DefaultIfEmpty(int.MaxValue).Min();
    doc["brand_sort_order_min"] = brandSortOrderMin == int.MaxValue ? null : (long?)brandSortOrderMin;
    doc["brand_sort_order_min_or_max"] = brandSortOrderMin == int.MaxValue 
        ? long.MaxValue : (long)brandSortOrderMin;
    ```
  - [ ] 0.4.12.3: 入口过滤用户数据中的 PUA 字符(修复 S6-12):
    ```csharp
    var name1 = StripPua(p.ProductName1);
    var name2 = StripPua(p.ProductName2);
    // 其他字段同样过滤
    ```
  - [ ] 0.4.12.4: 短品牌缩写(<= 3 字符)追加完整品牌名到 `brands_aliases` 字段
  - [ ] 0.4.12.5: sortableAttributes 配置改为 `brand_sort_order_min_or_max`(非 `brand_sort_order_min`)
  - [ ] 0.4.12.6: searchableAttributes 配置数组字段(`oem_list_published_brands` / `oem_list_published_oem3s`)
  - [ ] 0.4.12.7: 移除 separatorTokens 配置(数组字段自动处理)
  - [ ] 0.4.12.8: stopWords 配置 `["AG", "SA", "Co", "Ltd"]`(短品牌缩写)
  - **验证**: 单元测试 `Search_BrandWithSpace_Matched` + `Search_BrandSortOrder_NullLast` + `BuildDocument_PuaStripped` 通过
  - **依赖**: Task 0.1.5, Task 0.4.28(v6 SanitizeFormatted)

- [ ] **Task 0.4.13**: MeiliSearchProvider.SanitizeFormatted 步骤 0 字符集统一(修复 S6-1)
  - [ ] 0.4.13.1: 步骤 0 改为过滤 U+E000/U+E001(主)+ 兼容 \uFDD0/\uFDD1(历史):
    ```csharp
    input = input.Replace("\uE000", "")
                 .Replace("\uE001", "")
                 .Replace("\uFDD0", "")
                 .Replace("\uFDD1", "");
    ```
  - [ ] 0.4.13.2: 步骤 3 还原逻辑改为 if-else(S5-1 已修复,本任务确认字符集一致)
  - **验证**: 单元测试 `SanitizeFormatted_XssDefense_E000Literal` 通过(用户输入 U+E000 字面量被过滤)
  - **依赖**: Task 0.4.28(v6 SanitizeFormatted)

- [ ] **Task 0.4.14**: MeiliSearchProvider.DeleteAsync 幂等 404 处理(修复 D6-7)
  - [ ] 0.4.14.1: 捕获 MeilisearchApiException 404 异常,记录日志但不重试:
    ```csharp
    catch (MeilisearchApiException ex) when (ex.Code == "document_not_found" || ex.StatusCode == 404)
    {
        _logger.LogInformation("Meilisearch 文档已不存在,跳过删除,索引 {Index},IDs {Ids}", 
            idx.Uid, string.Join(",", ids));
    }
    ```
  - [ ] 0.4.14.2: CleanupService.CleanupOneAsync 重试前先 GetDocumentAsync 检查存在性
  - **验证**: 单元测试 `Delete_Idempotent_404` + `Cleanup_CheckExistence_First` 通过
  - **依赖**: 无

- [ ] **Task 0.4.15**: MeiliSearchProvider.ChannelWriteTimeout 超时降级(修复 S6-7)
  - [ ] 0.4.15.1: IndexAsync 用 `CancellationTokenSource.CreateLinkedTokenSource` + 5s 超时:
    ```csharp
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(5));
    try { await _channel.Writer.WriteAsync(doc, cts.Token); }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        _logger.LogWarning("Channel 入队 5s 超时,降级到 DB,MR.1={Mr1}", doc.Mr1);
        await FallbackToDb(new IndexTask(doc), ct);
    }
    ```
  - [ ] 0.4.15.2: appsettings.json 加 `MeiliSearch:ChannelWriteTimeoutSeconds: 5`
  - [ ] 0.4.15.3: IOptionsMonitor 监听配置变更动态调整超时
  - **验证**: 单元测试 `ChannelWrite_Timeout_DbFallback` 通过
  - **依赖**: 无

- [ ] **Task 0.4.16**: MeiliSearchProvider.FallbackToDb 不重置 retry_count(修复 S6-6)
  - [ ] 0.4.16.1: ExecuteUpdateAsync 改为递增 retry_count + 永久失败标记:
    ```csharp
    await _db.SearchIndexPending
        .Where(t => t.Mr1 == task.Mr1 && t.IndexName == task.IndexName)
        .ExecuteUpdateAsync(s => s
            .SetProperty(t => t.RetryCount, t => t.RetryCount + 1)
            .SetProperty(t => t.Status, t => t.RetryCount + 1 >= 5 ? "failed_permanent" : "pending")
            .SetProperty(t => t.LastError, task.Error)
            .SetProperty(t => t.UpdatedAt, DateTimeOffset.UtcNow), ct);
    ```
  - [ ] 0.4.16.2: 永久失败任务数 > 0 触发告警(同 D6-9)
  - **验证**: 单元测试 `DbFallback_NoResetRetryCount` + `DbFallback_PermanentFailure` 通过
  - **依赖**: Task 0.1.7

- [ ] **Task 0.4.17**: PostgresSearchProvider 聚合搜索降级路径用 BMP PUA 占位符(修复 S6-3)
  - [ ] 0.4.17.1: SQL 用 `concat(U&E'\\uE000', field, U&E'\\uE001')` 包裹匹配字段:
    ```sql
    SELECT p.mr_1, p.product_name_1, p.product_name_2,
           concat(U&E'\\uE000', p.product_name_1, U&E'\\uE001') AS _formatted_name1,
           concat(U&E'\\uE000', p.product_name_2, U&E'\\uE001') AS _formatted_name2
    FROM products p
    WHERE p.deleted_at IS NULL AND p.mr_1 IS NOT NULL
      AND (LOWER(p.product_name_1) LIKE LOWER(@q) OR 
           LOWER(p.product_name_2) LIKE LOWER(@q))
    ```
  - [ ] 0.4.17.2: 调用 SanitizeFormatted(与主路径共用,无 XSS 风险)
  - [ ] 0.4.17.3: 移除"正则还原 <mark>"逻辑(S5-13 降级方案废弃)
  - **验证**: 单元测试 `Search_Fallback_XssDefense` 通过(用户输入 `<mark>` 字面量被过滤)
  - **依赖**: Task 0.4.13

- [ ] **Task 0.4.18**: CursorHmac.VerifyAndExtractV2 long.TryParse + 范围校验(修复 S6-2)
  - [ ] 0.4.18.1: long.Parse 改 long.TryParse,失败抛 CURSOR_INVALID
  - [ ] 0.4.18.2: expUnixTs 范围校验:`now - 86400 <= expUnixTs <= now + 86400 * 7`
  - [ ] 0.4.18.3: int.Parse / long.Parse 全部改 TryParse
  - **验证**: 单元测试 `Cursor_TamperedExpUnixTs_Rejected` + `Cursor_MalformedExp_Rejected` 通过
  - **依赖**: 无

- [ ] **Task 0.4.19**: MeiliFilterEscapeExtensions 补全转义(修复 S6-9)
  - [ ] 0.4.19.1: EscapeMeiliFilterValue 新增转义:
    - `\\` → `\\\\`
    - `"` → `\\"`
    - `'` → `\\'`(单引号)
    - `[` → `\\[`(方括号开)
    - `]` → `\\]`(方括号闭)
    - `\0` → 丢弃(null 字节)
  - **验证**: 单元测试 `EscapeMeiliFilter_AllSpecialChars` 通过
  - **依赖**: 无

- [ ] **Task 0.4.20**: StringUtils.StripControlChars 补全不可见字符(修复 D6-8)
  - [ ] 0.4.20.1: 新增 InvisibleChars HashSet:
    ```csharp
    private static readonly HashSet<char> InvisibleChars = new()
    {
        '\u200B', '\u200C', '\u200D', '\uFEFF', '\u00A0', '\u2060',
        '\u2028', '\u2029', '\u00AD'
    };
    ```
  - [ ] 0.4.20.2: StripControlChars 过滤 char.IsControl + InvisibleChars
  - **验证**: 单元测试 `StripControlChars_InvisibleChars` 通过(覆盖所有 9 个不可见字符)
  - **依赖**: 无

- [ ] **Task 0.4.21**: AllowPuaJavaScriptEncoder 自定义编码器(修复 S6-11)
  - [ ] 0.4.21.1: 新建 `SakuraFilter.JsonEncoders.cs`,实现 AllowPuaJavaScriptEncoder(允许 BMP PUA U+E000~U+F8FF 原样输出)
  - [ ] 0.4.21.2: Program.cs `AddJsonOptions` 配置自定义 encoder
  - **验证**: 单元测试 `JsonSerializer_PuaPreserved` 通过(U+E000/U+E001 不被转义)
  - **依赖**: 无

### Phase 1 v7 补丁任务(4 个 — 前端 XSS/Session/FormDraft)

- [ ] **Task 1.3.12**: frontend/src/utils/html-sanitizer.ts 步骤 0 字符集同步(修复 S6-1 前端)
  - [ ] 1.3.12.1: DOMPurify 预处理改为过滤 U+E000/U+E001(主)+ 兼容 \uFDD0/\uFDD1:
    ```typescript
    function preprocess(input: string): string {
      return input.replace(/[\uE000\uE001\uFDD0\uFDD1]/g, '')
    }
    ```
  - [ ] 1.3.12.2: 与后端 SanitizeFormatted 步骤 0 字符集一致
  - **验证**: 前端单元测试 `HtmlSanitizer_E000Stripped` 通过
  - **依赖**: Task 1.3.10(v6 html-sanitizer)

- [ ] **Task 1.3.13**: frontend/src/utils/safeStorage.ts 重构(修复 F5-2)
  - [ ] 1.3.13.1: 启动时检测 sessionStorage 可用性(try-catch setItem/removeItem 测试)
  - [ ] 1.3.13.2: safeGetItem sessionStorage 返回 null 时尝试 memoryStore:
    ```typescript
    if (sessionStorageAvailable) {
      try {
        const value = sessionStorage.getItem(key)
        if (value !== null) return value
      } catch {}
    }
    return memoryStore.has(key) ? memoryStore.get(key)! : null
    ```
  - [ ] 1.3.13.3: safeSetItem 总是写入 memoryStore + 尝试写 sessionStorage
  - [ ] 1.3.13.4: safeRemoveItem 同步删除 memoryStore + sessionStorage
  - **验证**: 前端单元测试 `SafeStorage_SafariPrivateMode_ReadFromMemory` 通过
  - **依赖**: 无

- [ ] **Task 1.3.14**: frontend/src/composables/useFormDraft.ts 多标签冲突修复(修复 F5-3)
  - [ ] 1.3.14.1: 草稿 key 加 sessionId(UUID v4):
    ```typescript
    const SESSION_ID = uuidv4()
    const draftKey = computed(() => `draft_${mr1.value || `new_${SESSION_ID}`}`)
    ```
  - [ ] 1.3.14.2: BroadcastChannel 多标签同步(`draft_${mr1.value || 'new'}` 频道)
  - [ ] 1.3.14.3: 草稿加 expiresAt(7 天 TTL),loadDraft 检查过期自动清理
  - [ ] 1.3.14.4: cleanupExpiredDrafts 启动时清理过期草稿
  - **验证**: 前端单元测试 `FormDraft_MultiTab_BroadcastSync` + `FormDraft_Expired_Cleanup` 通过
  - **依赖**: 无

- [ ] **Task 1.3.15**: frontend/src/utils/errorMonitor.ts captureException 去重 + 兜底 flush(修复 F5-10)
  - [ ] 1.3.15.1: buffer 改 Map<string, unknown>,key = `${message}|${stack[0]}`
  - [ ] 1.3.15.2: buffer 容量 50,LRU 淘汰
  - [ ] 1.3.15.3: init 后 flush buffer 到 Sentry
  - [ ] 1.3.15.4: 30s 安全兜底,init 未调用时 console.error 缓冲内容
  - **验证**: 前端单元测试 `CaptureException_Dedup` + `CaptureException_BufferLimit` 通过
  - **依赖**: 无

### Phase 3 v7 补丁任务(1 个 — 孤儿图片清理)

- [ ] **Task 3.2.13.1**: CleanupOrphanImagesService 定期孤儿文件清理(修复 E3/D6-1)
  - [ ] 3.2.13.1.1: 新增 `CleanupOrphanImagesService.cs`,实现 `CleanupOrphansAsync`:
    1. 列出对象存储所有图片: `_storage.ListAllAsync("products/", ct)`
    2. 列出 DB 所有图片 URL: `_db.ProductImages.Select(img => img.ImageUrl).ToListAsync(ct)`
    3. 差集 = 孤儿文件,批量删除
  - [ ] 3.2.13.1.2: 注册为 HostedService,每周日凌晨 3 点执行(Cron 表达式 `0 3 * * 0`)
  - [ ] 3.2.13.1.3: 告警: 孤儿文件数 > 100 触发告警
  - **验证**: 集成测试 `CleanupOrphans_NoFalsePositive` + `CleanupOrphans_DeletedFiles` 通过
  - **依赖**: Task 0.3.7

### Phase 4 v7 补丁任务(7 个 — 前端 URL/Cookie/Error 修复)

- [ ] **Task 4.5.21**: frontend/src/utils/url.ts buildProductUrl mr1Suffix 直接 URL 编码(修复 F5-1)
  - [ ] 4.5.21.1: mr1Suffix 不走 buildSlug,直接 `encodeURIComponent(p.mr1)`:
    ```typescript
    export function buildProductUrl(p: Product): string {
      const pn1 = buildSlug(p.productName1)
      const pn2 = buildSlug(p.productName2)
      const brand = buildSlug(p.oemBrand)
      const mr1Suffix = encodeURIComponent(p.mr1)  // F5-1: 保留大小写
      return `/products/${pn1}/${pn2}/${brand}/${mr1Suffix}`
    }
    ```
  - [ ] 4.5.21.2: 后端 Detail.cshtml.cs OnGetAsync 用 Uri.UnescapeDataString 解码 + 大小写敏感查询
  - **验证**: 集成测试 `Url_Mr1_CasePreserved` 通过(ABC123 → /products/.../ABC123 → 反查 ABC123)
  - **依赖**: Task 4.5.14(v5 http.ts)

- [ ] **Task 4.5.22**: frontend/src/utils/http.ts handle401 同步设置 isRedirecting(修复 F5-4)
  - [ ] 4.5.22.1: handle401 同步设置 isRedirecting = true(在 router.replace 之前):
    ```typescript
    export function handle401() {
      if (isRedirecting) return
      isRedirecting = true  // 同步设置
      // ... 动态 import router
    }
    ```
  - [ ] 4.5.22.2: router.replace.finally 延迟 1500ms 重置 isRedirecting
  - [ ] 4.5.22.3: 导出 isHttpRedirecting() 供 PublicSearchView 检查
  - [ ] 4.5.22.4: PublicSearchView watch route.query 同步检查 isHttpRedirecting
  - **验证**: 前端单元测试 `Http401_NoUrlSyncLoop` 通过
  - **依赖**: Task 4.5.14

- [ ] **Task 4.5.23**: frontend/src/utils/http.ts handle401 保留 returnUrl + chunk 加载失败兜底(修复 F5-9)
  - [ ] 4.5.23.1: 计算 returnUrl = `encodeURIComponent(window.location.pathname + window.location.search)`
  - [ ] 4.5.23.2: 动态 import router 成功: `router.replace('/login?return=' + returnUrl)`
  - [ ] 4.5.23.3: 动态 import 失败: `window.location.href = '/login?return=' + returnUrl`
  - [ ] 4.5.23.4: LoginView.vue 接收 returnUrl query,登录成功后跳转
  - **验证**: 前端单元测试 `Http401_ReturnUrlPreserved` + `Http401_ChunkFailure_NativeRedirect` 通过
  - **依赖**: Task 4.5.22

- [ ] **Task 4.5.24**: Program.cs CookiePolicy 环境区分(修复 F5-5)
  - [ ] 4.5.24.1: Development 环境用 `CookieSecurePolicy.SameAsRequest` + `SameSiteMode.Lax`
  - [ ] 4.5.24.2: Production 环境用 `CookieSecurePolicy.Always` + `SameSiteMode.Strict`
  - [ ] 4.5.24.3: 认证 Cookie 同步配置 `Cookie.SecurePolicy`
  - **验证**: 集成测试 `CookiePolicy_Dev_HttpWorks` + `CookiePolicy_Prod_HttpsOnly` 通过
  - **依赖**: 无

- [ ] **Task 4.5.25**: frontend/src/main.ts error 事件捕获运行时错误 + unhandledrejection(修复 F5-6)
  - [ ] 4.5.25.1: error 事件区分资源加载错误和运行时错误:
    ```javascript
    if (event.target && event.target !== window && event.target.tagName) {
      // 资源加载错误
      const tag = event.target.tagName.toUpperCase()
      if (['SCRIPT', 'LINK', 'IMG'].includes(tag)) {
        handleResourceError(event); return
      }
    }
    // 运行时错误
    handleRuntimeError(event)
    // mount-fallback 逻辑
    ```
  - [ ] 4.5.25.2: 新增 `unhandledrejection` 事件监听(Promise 异常)
  - **验证**: 前端单元测试 `ErrorEvent_RuntimeError_Captured` 通过
  - **依赖**: Task 4.5.17(v6 mount-fallback)

- [ ] **Task 4.5.26**: frontend/src/main.ts __fallbackMounted 路由切换重置(修复 F5-7)
  - [ ] 4.5.26.1: `router.afterEach` 延迟 1000ms 重置 `window.__fallbackMounted = false`:
    ```typescript
    router.afterEach(() => {
      setTimeout(() => { window.__fallbackMounted = false }, 1000)
    })
    ```
  - **验证**: 前端单元测试 `FallbackMount_ResetOnRouteChange` 通过
  - **依赖**: Task 4.5.17

- [ ] **Task 4.5.27**: frontend/src/api/index.ts searchWithFallback 404 上报 + 配置开关(修复 F5-8)
  - [ ] 4.5.27.1: 404 时 `console.error` + `captureException`(Sentry 上报)
  - [ ] 4.5.27.2: 仅在 `import.meta.env.VITE_ENABLE_LEGACY_FALLBACK === 'true'` 时才 fallback:
    ```typescript
    if (!import.meta.env.VITE_ENABLE_LEGACY_FALLBACK) {
      throw new Error('聚合搜索 API 不可用,请联系管理员')
    }
    ```
  - [ ] 4.5.27.3: .env.development 设 `VITE_ENABLE_LEGACY_FALLBACK=true`
  - [ ] 4.5.27.4: .env.production 设 `VITE_ENABLE_LEGACY_FALLBACK=false`
  - **验证**: 前端单元测试 `SearchFallback_404_ReportsError` + `SearchFallback_NoFallback_Throws` 通过
  - **依赖**: Task 1.3.11(v6 searchWithFallback)

### Phase 5 v7 补丁任务(2 个 — ETL oem_2 + 启动版本检测)

- [ ] **Task 5.1.26**: EtlImportService.LoadExistingOem2MapAsync 新增(修复 E2/D6-3)
  - [ ] 5.1.26.1: 新增独立方法(不影响现有 LoadExistingOemMapAsync):
    ```csharp
    private async Task<Dictionary<string, List<string>>> LoadExistingOem2MapAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var rows = await _db.CrossReferences
            .Where(x => productIds.Contains(x.ProductId) && x.Oem2 != null)
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Oem2List = g.Select(x => x.Oem2!).Distinct().ToList() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.ProductId.ToString(), r => r.Oem2List);
    }
    ```
  - [ ] 5.1.26.2: ETL 流程中调用,oem_2 多值占比 > 1% 记录告警(不阻断)
  - [ ] 5.1.26.3: 单元测试 `Etl_Oem2MultiValue_Detection` 通过
  - **依赖**: Task 0.2.2(CrossReference 实体 Oem2 属性)

- [ ] **Task 5.1.27**: Program.cs 启动时 Meilisearch 版本检测 + 健康检查服务(修复 S6-13)
  - [ ] 5.1.27.1: `CheckMeiliVersionAsync` 方法,3s 超时:
    - 不可达时记录警告,搜索自动降级到 PG
    - 版本 < 1.6.0 记录警告,部分功能受限
    - 版本 >= 1.6.0 记录信息,正常启动
  - [ ] 5.1.27.2: `MeiliHealthCheckService` BackgroundService 每 60s 重试连接:
    - 不可达时 SetAvailability(false) 降级到 PG
    - 恢复时 SetAvailability(true) 切回 Meilisearch
  - [ ] 5.1.27.3: ISearchProvider 加 `SetAvailability(bool)` + `IsMeiliAvailable` 属性
  - **验证**: 集成测试 `Startup_MeiliUnreachable_PgFallback` + `HealthCheck_MeiliRecover_SwitchBack` 通过
  - **依赖**: 无

### Phase 0 v7 补丁任务(数据关联对账脚本 1 个)

- [ ] **Task 0.1.8**: v7 对账脚本三维度 NULL 漂移检测(修复 D6-4)
  - [ ] 0.1.8.1: 修改对账脚本为 FULL OUTER JOIN + 三维度计数:
    ```sql
    SELECT
      COUNT(*) FILTER (WHERE p.id IS NULL) AS v2_only,
      COUNT(*) FILTER (WHERE m.id IS NULL) AS v1_only,
      COUNT(*) FILTER (WHERE p.mr_1 IS NULL AND m.mr_1 IS NOT NULL) AS null_to_nonnull,
      COUNT(*) FILTER (WHERE p.mr_1 IS NOT NULL AND m.mr_1 IS NULL) AS nonnull_to_null,
      COUNT(*) FILTER (WHERE p.mr_1 IS NOT NULL AND m.mr_1 IS NOT NULL AND p.mr_1 <> m.mr_1) AS value_mismatch
    FROM products p
    FULL OUTER JOIN products_v2 m ON p.id = m.id;
    ```
  - [ ] 0.1.8.2: 任一指标 > 0 触发告警,阻断进入阶段 4
  - **验证**: 集成测试 `Reconciliation_NullDrift_Detected` 通过
  - **依赖**: 无

## Task Dependencies(v7 补丁任务)

> - **Task 0.1.4** → 独立(修复 v6 D5-7 误判)
> - **Task 0.1.5** → 依赖 Task 0.1.4
> - **Task 0.1.6** → 独立
> - **Task 0.1.7** → 独立
> - **Task 0.1.8** → 独立
> - **Task 0.3.7** → 依赖 Task 0.1.4
> - **Task 0.3.8** → 依赖 Task 0.1.5
> - **Task 0.4.12** → 依赖 Task 0.1.5, Task 0.4.28
> - **Task 0.4.13** → 依赖 Task 0.4.28
> - **Task 0.4.14** → 独立
> - **Task 0.4.15** → 独立
> - **Task 0.4.16** → 依赖 Task 0.1.7
> - **Task 0.4.17** → 依赖 Task 0.4.13
> - **Task 0.4.18** → 独立
> - **Task 0.4.19** → 独立
> - **Task 0.4.20** → 独立
> - **Task 0.4.21** → 独立
> - **Task 1.3.12** → 依赖 Task 1.3.10
> - **Task 1.3.13** → 独立
> - **Task 1.3.14** → 独立
> - **Task 1.3.15** → 独立
> - **Task 3.2.13.1** → 依赖 Task 0.3.7
> - **Task 4.5.21** → 依赖 Task 4.5.14
> - **Task 4.5.22** → 依赖 Task 4.5.14
> - **Task 4.5.23** → 依赖 Task 4.5.22
> - **Task 4.5.24** → 独立
> - **Task 4.5.25** → 依赖 Task 4.5.17
> - **Task 4.5.26** → 依赖 Task 4.5.17
> - **Task 4.5.27** → 依赖 Task 1.3.11
> - **Task 5.1.26** → 依赖 Task 0.2.2
> - **Task 5.1.27** → 独立

---

# v8 补丁任务清单(27 个)

> **修订时间**: 2026-07-17
> **触发原因**: 第七轮深度审查发现 64 项衍生漏洞 + v7 24 项高危事实性误判
> **执行顺序**: Phase 0(前置任务 8) → Phase 1(数据关联 7) → Phase 3(检索逻辑 6) → Phase 4(前后端联动 6)

## Phase 0: v8 前置任务(8 个)

### Pre-Task-V8-1: 创建 cleanup_failures 表 + CleanupFailure 实体 [高]

**修复**: D7-8 / E11
**文件**:
- `backend/src/SakuraFilter.Core/Entities/CleanupFailure.cs` (新建)
- `backend/src/SakuraFilter.Infrastructure/Data/Configurations/CleanupFailureConfiguration.cs` (新建)
- `backend/src/SakuraFilter.Infrastructure/Data/Migrations/<timestamp>_AddCleanupFailuresTable.cs` (新建)

**子任务**:
- [ ] 1.1: 创建 CleanupFailure 实体:
  ```csharp
  public class CleanupFailure
  {
      public long Id { get; set; }
      [Column("file_key")] public string FileKey { get; set; } = "";
      [Column("backend")] public string Backend { get; set; } = ""; // minio | aliyun
      [Column("failure_type")] public string FailureType { get; set; } = ""; // delete_failed | list_failed
      [Column("error_message")] public string ErrorMessage { get; set; } = "";
      [Column("retry_count")] public int RetryCount { get; set; }
      [Column("status")] public string Status { get; set; } = "pending"; // pending|in_progress|success|failed|failed_permanent
      [Column("last_attempt_at")] public DateTime? LastAttemptAt { get; set; }
      [Column("next_retry_at")] public DateTime? NextRetryAt { get; set; }
      [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
      [Column("updated_at")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
  }
  ```
- [ ] 1.2: 创建 EF 配置 + ToTable("cleanup_failures")
- [ ] 1.3: 创建迁移文件,DDL 见 spec.md E11
- [ ] 1.4: 注册到 ProductDbContext: `public DbSet<CleanupFailure> CleanupFailures => Set<CleanupFailure>();`

**验证**:
- `dotnet ef migrations has-pending-model-changes` 无 diff
- `dotnet ef database update` 成功
- 表存在性查询 `SELECT 1 FROM cleanup_failures LIMIT 1` 成功

**依赖**: 无

### Pre-Task-V8-2: 扩展 IObjectStorage 接口 [高]

**修复**: D7-3 / E6
**文件**:
- `backend/src/SakuraFilter.Core/Interfaces/IObjectStorage.cs` (修改)
- `backend/src/SakuraFilter.Infrastructure/Storage/MinioStorage.cs` (修改)
- `backend/src/SakuraFilter.Infrastructure/Storage/AliyunOssStorage.cs` (修改)

**子任务**:
- [ ] 2.1: IObjectStorage 新增 2 方法:
  ```csharp
  Task<IAsyncEnumerable<string>> ListAllAsync(string? prefix = null, CancellationToken ct = default);
  Task DeleteBatchAsync(IEnumerable<string> keys, CancellationToken ct = default);
  ```
- [ ] 2.2: MinioStorage 实现 ListAllAsync(用 ListObjectsAsync 迭代器)
- [ ] 2.3: MinioStorage 实现 DeleteBatchAsync(批量 DeleteObjectsAsync,1000 条/批)
- [ ] 2.4: AliyunOssStorage 实现 ListAllAsync(用 ListObjectsV2 迭代)
- [ ] 2.5: AliyunOssStorage 实现 DeleteBatchAsync(批量 DeleteObjects,1000 条/批)

**验证**:
- 单元测试 `MinioStorage_ListAll_Pagination` 通过
- 单元测试 `MinioStorage_DeleteBatch_Idempotent` 通过(404 静默)
- 单元测试 `AliyunOssStorage_ListAll_Pagination` 通过
- 单元测试 `AliyunOssStorage_DeleteBatch_Idempotent` 通过

**依赖**: 无

### Pre-Task-V8-3: SEO 多段 URL 独立路由(可选,低优先级) [低]

**修复**: E14
**文件**:
- `frontend/src/router/index.ts` (修改)
- `frontend/src/views/public/PublicProductView.vue` (修改)

**子任务**:
- [ ] 3.1: 新增路由 `/products/:pn1/:pn2/:brand/:mr1Suffix`(与现有 /product/:oem 并存)
- [ ] 3.2: PublicProductView.vue 兼容两种路由参数读取
- [ ] 3.3: 现有 /product/:oem 路由保持不变(后向兼容)

**验证**:
- 现有路由 `/product/ABC123` 仍可访问
- 新路由 `/products/pn1/pn2/brand/mr1suffix` 可访问且解析正确

**依赖**: 无

### Pre-Task-V8-4: 创建 frontend/src/utils/url.ts [高]

**修复**: F6-1 / F6-17 / F6-20 / E19
**文件**: `frontend/src/utils/url.ts` (新建)

**子任务**:
- [ ] 4.1: 实现 buildProductUrl:
  ```typescript
  export function buildProductUrl(oem: string): string {
    return `/product/${encodeURIComponent(oem)}`
  }
  ```
- [ ] 4.2: 实现 getProductSlugFromRoute
- [ ] 4.3: 实现 isSafeRedirect(开放重定向防护):
  ```typescript
  export function isSafeRedirect(path: string): boolean {
    if (!path) return false
    if (!path.startsWith('/')) return false
    if (path.startsWith('//')) return false
    if (/^\/[^/].*/.test(path) === false) return false
    return true
  }
  ```
- [ ] 4.4: 重构 LoginView.vue 使用 isSafeRedirect
- [ ] 4.5: 重构 http.ts redirectToLogin 使用 isSafeRedirect

**验证**:
- 单元测试 `isSafeRedirect_RejectsExternalUrl` 通过(`//evil.com` 拒绝)
- 单元测试 `isSafeRedirect_RejectsProtocolRelative` 通过(`//attacker.com` 拒绝)
- 单元测试 `buildProductUrl_EncodesSpecialChars` 通过(`&`/`?`/`#` 编码)

**依赖**: 无

### Pre-Task-V8-5: 创建 frontend/src/utils/safeStorage.ts [高]

**修复**: F6-21 / E19
**文件**: `frontend/src/utils/safeStorage.ts` (新建)

**子任务**:
- [ ] 5.1: 实现 safeLocalStorage(getItem/setItem/removeItem,try-catch 包裹)
- [ ] 5.2: 实现 safeSessionStorage(同上,改 sessionStorage)
- [ ] 5.3: quota exceeded 时静默失败,返回 false
- [ ] 5.4: 重构 errorMonitor.ts 使用 safeLocalStorage
- [ ] 5.5: 重构 ErrorBoundary.vue 使用 safeLocalStorage

**验证**:
- 单元测试 `safeLocalStorage_HandlesQuotaExceeded` 通过(模拟 QuotaExceededError 返回 false)
- 单元测试 `safeLocalStorage_GetItemReturnsNullOnException` 通过

**依赖**: 无

### Pre-Task-V8-6: 创建 Mr1Validator 静态工具 [高]

**修复**: F6-2 / E23
**文件**: `backend/src/SakuraFilter.Core/Validation/Mr1Validator.cs` (新建)

**子任务**:
- [ ] 6.1: 实现 Mr1Validator.IsValid(见 spec.md E23)
  - 长度校验:10 位
  - 字符集校验:0-9 A-Z
  - CHK 校验位算法:前 9 位加权求和取模 36
- [ ] 6.2: ETL 导入时调用 Mr1Validator.IsValid,失败记录到错误日志
- [ ] 6.3: Admin 创建/编辑产品时调用 Mr1Validator.IsValid,失败返回 400

**验证**:
- 单元测试 `Mr1Validator_ValidChk` 通过(合法 MR.1 通过)
- 单元测试 `Mr1Validator_InvalidLength` 通过(9 位/11 位拒绝)
- 单元测试 `Mr1Validator_InvalidCharset` 通过(小写/特殊字符拒绝)
- 单元测试 `Mr1Validator_InvalidChk` 通过(CHK 位错误拒绝)
- 单元测试 `Mr1Validator_NullOrEmpty` 通过(空值拒绝)

**依赖**: 无

### Pre-Task-V8-7: 升级 Meilisearch SDK 到 1.6+(可选,延后执行) [中]

**修复**: S7-2
**文件**: `backend/src/SakuraFilter.Search/SakuraFilter.Search.csproj` (修改)

**子任务**:
- [ ] 7.1: 评估升级风险(API 签名变更)
- [ ] 7.2: 升级 MeiliSearch 包到 1.6+
- [ ] 7.3: 修复 MeiliSearchProvider 全部方法签名
- [ ] 7.4: 全量回归测试

**注意**: v8 不强制执行,列入 v9 评估。当前保持 0.15.4。

**验证**:
- 所有 MeiliSearchProvider 单元测试通过
- 集成测试通过

**依赖**: 无

### Pre-Task-V8-8: 创建 MeiliFilterEscapeExtensions [中]

**修复**: S7-6 / S7-14
**文件**: `backend/src/SakuraFilter.Search/Extensions/MeiliFilterEscapeExtensions.cs` (新建)

**子任务**:
- [ ] 8.1: 实现 EscapeMeiliFilterValue:
  ```csharp
  public static string EscapeMeiliFilterValue(string value)
  {
      if (string.IsNullOrEmpty(value)) return "\"\"";
      var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
      return $"\"{escaped}\"";
  }
  ```
- [ ] 8.2: 在 MeiliSearchProvider.SearchAsync 中使用此方法

**验证**:
- 单元测试 `EscapeMeiliFilter_Backslash` 通过(`\` → `\\`)
- 单元测试 `EscapeMeiliFilter_DoubleQuote` 通过(`"` → `\"`)
- 单元测试 `EscapeMeiliFilter_EmptyString` 通过(返回 `""`)
- 单元测试 `EscapeMeiliFilter_NormalString` 通过(原样返回带引号)

**依赖**: 无

## Phase 1: v8 数据关联修复任务(7 个)

### Task V8-1.1: CrossReference FK 配置修正 [高]

**修复**: D7-1 / D7-16 / E4
**文件**: `backend/src/SakuraFilter.Infrastructure/Data/Configurations/CrossReferenceConfiguration.cs` (修改)

**子任务**:
- [ ] 1.1.1: 改用 `HasOne<Product>()` 无参重载
- [ ] 1.1.2: HasConstraintName 与 InitialCreate 一致: `fk_cross_references_products_product_id`
- [ ] 1.1.3: OnDelete(Cascade)

**验证**:
- `dotnet ef migrations has-pending-model-changes` 无 diff
- 现有 FK 不被重建

**依赖**: 无

### Task V8-1.2: TRUNCATE 列表修正 [高]

**修复**: D7-2 / D7-15 / E5
**文件**: `backend/src/SakuraFilter.Etl/EtlImportService.cs` (修改,ResetAllDataAsync 方法)

**子任务**:
- [ ] 1.2.1: TRUNCATE 列表改为动态白名单(见 spec.md D7-15):
  ```sql
  DO $$ DECLARE tbl TEXT;
  BEGIN
    FOR tbl IN SELECT table_name FROM information_schema.tables
      WHERE table_schema='public' AND table_name IN (
        'products','cross_references','machine_applications','product_images',
        'product_history','search_index_pending','search_index_dead_letter',
        'etl_progress_log','cleanup_failures'
      )
    LOOP EXECUTE format('TRUNCATE TABLE %I RESTART IDENTITY CASCADE', tbl);
    END LOOP;
  END $$;
  ```
- [ ] 1.2.2: 移除对 cleanup_failures / partition6_placeholder 的硬编码引用

**验证**:
- 全量重置后 9 张表(含 cleanup_failures)均清空
- 不存在的表不报错(白名单过滤)

**依赖**: Pre-Task-V8-1

### Task V8-1.3: ProductImage 字段名统一 [高]

**修复**: D7-4 / E7
**文件**: 项目全局搜索 `ImageUrl` 引用 ProductImage 字段处

**子任务**:
- [ ] 1.3.1: grep `pi\.ImageUrl` / `image\.ImageUrl` 全部改为 `ImageKey`
- [ ] 1.3.2: 核实无遗漏

**验证**:
- `dotnet build` 无错误
- 单元测试 `ProductImage_ImageKey_FieldName` 通过

**依赖**: 无

### Task V8-1.4: CrossReference 字段引用修正 [高]

**修复**: D7-5 / E8
**文件**: 项目全局搜索 CrossReference.IsPublished/OemBrandId/SortOrder 引用处

**子任务**:
- [ ] 1.4.1: grep `IsPublished`/`OemBrandId`/`SortOrder` 引用 CrossReference 处,全部移除或改用替代方案
- [ ] 1.4.2: Brand 排序改用 JOIN xref_oem_brand(见 spec.md D7-12)
- [ ] 1.4.3: IsPublished 概念改用 `!IsDiscontinued`

**验证**:
- `dotnet build` 无错误
- 搜索结果 Brand 排序正确

**依赖**: 无

### Task V8-1.5: ProductIndexDoc 扩展 + BuildProductIndexDocAsync [高]

**修复**: D7-6 / E9
**文件**:
- `backend/src/SakuraFilter.Search/ISearchProvider.cs` (修改 ProductIndexDoc)
- `backend/src/SakuraFilter.Search/MeiliSearchProvider.cs` (新增 BuildProductIndexDocAsync)

**子任务**:
- [ ] 1.5.1: ProductIndexDoc 新增字段(见 spec.md E9):
  - Mr1
  - BrandSortOrder
  - OemListPublishedBrands (string[])
  - OemListPublishedOem3s (string[])
- [ ] 1.5.2: 新增 BuildProductIndexDocAsync 方法替代 BuildMr1DocumentAsync:
  ```csharp
  private async Task<ProductIndexDoc> BuildProductIndexDocAsync(Product product, CancellationToken ct)
  {
      using var scope = _sp.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
      var xref = await db.XrefOemBrands
          .Where(x => x.Brand == product.OemBrand && x.DeletedAt == null)
          .Select(x => new { x.SortOrder })
          .FirstOrDefaultAsync(ct);
      var published = await db.CrossReferences
          .Where(c => c.ProductId == product.Id && !c.IsDiscontinued)
          .Select(c => new { c.OemBrand, c.OemNo3 })
          .ToListAsync(ct);
      return new ProductIndexDoc
      {
          Id = product.Id,
          OemNoNormalized = product.OemNoNormalized,
          OemNoDisplay = product.OemNoDisplay,
          Mr1 = product.Mr1,
          Remark = product.Remark,
          Type = product.Type,
          D1Mm = product.D1Mm,
          D2Mm = product.D2Mm,
          H3Mm = product.H3Mm,
          H1Mm = product.H1Mm,
          Media = product.Media,
          IsDiscontinued = product.IsDiscontinued,
          UpdatedAtUnix = new DateTimeOffset(product.UpdatedAt).ToUnixTimeSeconds(),
          BrandSortOrder = xref?.SortOrder ?? int.MaxValue,
          OemListPublishedBrands = published.Select(p => p.OemBrand ?? "").Distinct().ToArray(),
          OemListPublishedOem3s = published.Select(p => p.OemNo3 ?? "").Distinct().ToArray()
      };
  }
  ```

**验证**:
- `dotnet build` 无错误
- 单元测试 `BuildProductIndexDocAsync_IncludesMr1AndBrandSort` 通过
- 单元测试 `BuildProductIndexDocAsync_NullBrandSortDefaultsToMax` 通过

**依赖**: 无

### Task V8-1.6: EtlImportService 调用方式修正 [中]

**修复**: D7-9 / D7-17 / E9
**文件**: `backend/src/SakuraFilter.Etl/EtlImportService.cs` (修改)

**子任务**:
- [ ] 1.6.1: SyncFkConfigurationsV7 中 LoadExistingOem2MapAsync 调用改为:
  ```csharp
  await using var conn = new NpgsqlConnection(_pgConn);
  await conn.OpenAsync(ct);
  var existingMap = await LoadExistingOem2MapAsync(conn, ct);
  ```
- [ ] 1.6.2: 所有 ProductDbContext 调用改为 `_sp.CreateScope()` 动态获取

**验证**:
- `dotnet build` 无错误
- ETL 全量导入测试通过

**依赖**: 无

### Task V8-1.7: cleanup_failures 状态机 + 超时回收 [中]

**修复**: D7-10
**文件**: `backend/src/SakuraFilter.Api/Services/CleanupOrphanImagesService.cs` (新建,因 C26 不存在)

**子任务**:
- [ ] 1.7.1: 创建 CleanupOrphanImagesService(BackgroundService)
- [ ] 1.7.2: 实现 5min 超时回收(见 spec.md D7-10)
- [ ] 1.7.3: 实现孤儿文件检测(对比 product_images.image_key 与 ListAllAsync)
- [ ] 1.7.4: 失败记录到 cleanup_failures 表
- [ ] 1.7.5: 注册到 ServiceCollectionExtensions.AddHostedServices

**验证**:
- 单元测试 `CleanupOrphanImages_DetectsOrphans` 通过
- 单元测试 `CleanupFailures_StuckInProgressReset` 通过
- 单元测试 `CleanupFailures_RetryAfter5Min` 通过

**依赖**: Pre-Task-V8-1, Pre-Task-V8-2

## Phase 3: v8 检索逻辑修复任务(6 个)

### Task V8-3.1: PG SQL Unicode 转义语法修正 [高]

**修复**: S7-3 / S7-17 / E12
**文件**: 项目全局搜索 `U&E'` 引用处

**子任务**:
- [ ] 3.1.1: grep `U&E'\\\\uE000'` 全部改为 `U&'\uE000'`
- [ ] 3.1.2: grep `U&E'\\\\uE001'` 全部改为 `U&'\uE001'`

**验证**:
- `dotnet build` 无错误
- 集成测试 `PgHighlight_UnicodeEscape_SingleBackslash` 通过

**依赖**: 无

### Task V8-3.2: Product 软删除字段统一 [高]

**修复**: S7-18 / E13
**文件**: 项目全局搜索 `deleted_at` 引用 Product 处

**子任务**:
- [ ] 3.2.1: grep `deleted_at IS NULL` 在 Product 查询中改为 `is_discontinued = false`
- [ ] 3.2.2: XrefOemBrand 查询保持 `deleted_at IS NULL`(此实体真实有此字段)

**验证**:
- `dotnet build` 无错误
- 单元测试 `ProductQuery_FiltersByIsDiscontinued` 通过

**依赖**: 无

### Task V8-3.3: IndexReplayWorker 死信判定 + 字段补充 [中]

**修复**: S7-7 / S7-8 / S7-22 / E21
**文件**:
- `backend/src/SakuraFilter.Core/Entities/SearchIndexPending.cs` (修改,核实字段)
- `backend/src/SakuraFilter.Infrastructure/Data/Migrations/<timestamp>_AddSearchIndexPendingRetryColumns.cs` (新建,如需)
- `backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs` (修改)

**子任务**:
- [ ] 3.3.1: 核实 SearchIndexPending 实体字段
- [ ] 3.3.2: 若无 retry_count/is_dead/last_error 字段,新增迁移:
  ```sql
  ALTER TABLE search_index_pending ADD COLUMN IF NOT EXISTS retry_count INT NOT NULL DEFAULT 0;
  ALTER TABLE search_index_pending ADD COLUMN IF NOT EXISTS is_dead BOOLEAN NOT NULL DEFAULT false;
  ALTER TABLE search_index_pending ADD COLUMN IF NOT EXISTS last_error TEXT;
  ```
- [ ] 3.3.3: IndexReplayWorker 失败时 UPDATE retry_count = retry_count + 1
- [ ] 3.3.4: retry_count >= MaxRetryCount(5) 时标记 is_dead = true

**验证**:
- 单元测试 `IndexReplayWorker_RetryCountIncrement` 通过
- 单元测试 `IndexReplayWorker_MarkDeadAfter5Retries` 通过
- 集成测试 `SearchIndexPending_RetryCountFieldExists` 通过

**依赖**: 无

### Task V8-3.4: Meilisearch filter 转义修正 [中]

**修复**: S7-6 / S7-14
**文件**: 项目全局搜索 Meilisearch filter 调用处

**子任务**:
- [ ] 3.4.1: 移除对 `'`/`[`/`]` 的转义(Meilisearch 不支持)
- [ ] 3.4.2: 统一使用 Pre-Task-V8-8 的 EscapeMeiliFilterValue

**验证**:
- 单元测试 `MeiliFilter_NoSingleQuoteEscape` 通过
- 单元测试 `MeiliFilter_NoBracketEscape` 通过

**依赖**: Pre-Task-V8-8

### Task V8-3.5: stopWords 配置修正 [中]

**修复**: S7-5
**文件**: `backend/src/SakuraFilter.Search/MeiliSearchProvider.cs` (修改)

**子任务**:
- [ ] 3.5.1: 移除 stopWords 配置(或仅配置 "the"/"a"/"an")
- [ ] 3.5.2: 添加 synonyms 配置:品牌名缩写映射

**验证**:
- 单元测试 `Search_BrandWithSpace_Matched` 通过("BMW AG" 完整匹配)
- 单元测试 `Search_NoStopWordFiltering` 通过("Johnson" 不被过滤)

**依赖**: 无

### Task V8-3.6: JavaScriptEncoder 全局配置 [中]

**修复**: S7-9 / E22
**文件**: `backend/src/SakuraFilter.Api/Program.cs` (修改)

**子任务**:
- [ ] 3.6.1: 注册全局 JsonSerializerOptions:
  ```csharp
  services.ConfigureHttpJsonOptions(options =>
  {
      options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
      options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
  });
  ```
- [ ] 3.6.2: 移除 AllowPuaJavaScriptEncoder 引用(若存在)

**验证**:
- 集成测试 `ApiResponse_PuaCharsNotEscaped` 通过
- 集成测试 `ApiResponse_CamelCaseNaming` 通过

**依赖**: 无

## Phase 4: v8 前后端联动修复任务(6 个)

### Task V8-4.1: LoginView 开放重定向防护 [高]

**修复**: F6-16 / F6-17 / E16
**文件**: `frontend/src/views/LoginView.vue` (修改)

**子任务**:
- [ ] 4.1.1: 引入 isSafeRedirect(来自 Pre-Task-V8-4)
- [ ] 4.1.2: L46-47 改为:
  ```typescript
  import { isSafeRedirect } from '@/utils/url'
  const rawRedirect = (route.query.redirect as string) || '/admin/products'
  const safeRedirect = isSafeRedirect(rawRedirect) ? rawRedirect : '/admin/products'
  router.push(safeRedirect)
  ```

**验证**:
- 单元测试 `LoginView_RejectsExternalRedirect` 通过(`//evil.com` 重定向到 /admin/products)
- 单元测试 `LoginView_AcceptsInternalRedirect` 通过(`/admin/users` 接受)

**依赖**: Pre-Task-V8-4

### Task V8-4.2: http.ts isRedirecting 防并发 [高]

**修复**: F6-4 / F6-6 / E17
**文件**: `frontend/src/utils/http.ts` (修改)

**子任务**:
- [ ] 4.2.1: 顶部新增 `let isRedirecting = false`
- [ ] 4.2.2: redirectToLogin 函数加入并发锁(见 spec.md E17)
- [ ] 4.2.3: 释放改为 router.isReady().finally(替代 setTimeout 1500ms)

**验证**:
- 单元测试 `Http_NoConcurrentRedirect` 通过(多个 401 仅触发一次跳转)
- 单元测试 `Http_isRedirectingReleasesAfterRouteChange` 通过

**依赖**: 无

### Task V8-4.3: ErrorBoundary 集成 errorMonitor [中]

**修复**: F6-9 / F6-12 / E18
**文件**: `frontend/src/components/ErrorBoundary.vue` (修改)

**子任务**:
- [ ] 4.3.1: 移除 localStorage.setItem('sakura_error_log', ...) 代码
- [ ] 4.3.2: 引入 captureException 来自 @/utils/errorMonitor
- [ ] 4.3.3: onErrorCaptured 内调用 captureException(err, { tags: { source: 'ErrorBoundary' } })

**验证**:
- 单元测试 `ErrorBoundary_WritesToErrorMonitor` 通过
- 单元测试 `ErrorBoundary_NoLocalStorageDirectWrite` 通过

**依赖**: Pre-Task-V8-5

### Task V8-4.4: errorMonitor AbortError 过滤 + window 事件绑定 [中]

**修复**: F6-10 / F6-15 / F6-13 / F6-18 / F6-19
**文件**: `frontend/src/utils/errorMonitor.ts` (修改)

**子任务**:
- [ ] 4.4.1: initMonitor 内绑定 window.onerror:
  ```typescript
  window.addEventListener('error', (e) => {
    captureException(e.error || e.message, { tags: { source: 'window.onerror' } })
  })
  ```
- [ ] 4.4.2: initMonitor 内绑定 unhandledrejection,过滤 AbortError:
  ```typescript
  window.addEventListener('unhandledrejection', (e) => {
    if (e.reason?.name === 'AbortError') return
    captureException(e.reason, { tags: { source: 'unhandledrejection' } })
  })
  ```
- [ ] 4.4.3: initMonitor 内不调用 setTimeout(所有定时器由 installVueErrorHandler 与 shutdownMonitor 管理)
- [ ] 4.4.4: shutdownMonitor 解绑 window 事件监听

**验证**:
- 单元测试 `ErrorMonitor_FiltersAbortError` 通过
- 单元测试 `ErrorMonitor_CapturesWindowError` 通过
- 单元测试 `ErrorMonitor_CapturesUnhandledRejection` 通过
- 单元测试 `ErrorMonitor_NoSetTimeoutInInit` 通过

**依赖**: Pre-Task-V8-5

### Task V8-4.5: CursorHmac V2 Ticks 格式 + V1 兼容 [高]

**修复**: E20(硬约束违反)
**文件**: `backend/src/SakuraFilter.Api/Services/CursorHmac.cs` (修改)

**子任务**:
- [ ] 4.5.1: 新增 Sign(long ticks, long id) 方法,V2 格式(见 spec.md E20)
- [ ] 4.5.2: 修改 VerifyAndExtract 支持 V2 优先 + V1 兼容
- [ ] 4.5.3: 所有调用方改为传 ticks(非 ISO8601)
- [ ] 4.5.4: 旧 V1 cursor 在兼容期内仍可验证

**验证**:
- 单元测试 `CursorHmac_V2SignAndVerify` 通过
- 单元测试 `CursorHmac_V1BackwardCompat` 通过(旧 cursor 仍可验证)
- 单元测试 `CursorHmac_TicksFormat` 通过(验证返回 long.Ticks 非 ISO8601)
- 单元测试 `CursorHmac_RejectsTamperedTicks` 通过(签名错误拒绝)

**依赖**: 无

### Task V8-4.6: BroadcastChannelCompat 多标签页同步 [中]

**修复**: F6-8 / F6-11 / F6-22
**文件**: `frontend/src/utils/broadcast.ts` (新建)

**子任务**:
- [ ] 4.6.1: 实现 BroadcastChannelCompat(见 spec.md F6-8)
- [ ] 4.6.2: 降级到 localStorage storage 事件
- [ ] 4.6.3: 应用到 auth-logout 频道(多标签页同步登出)
- [ ] 4.6.4: 应用到 form-draft 频道(多标签页草稿同步,可选)

**验证**:
- 单元测试 `BroadcastChannelCompat_PostMessageWithChannel` 通过
- 单元测试 `BroadcastChannelCompat_FallbackToStorage` 通过(模拟 BroadcastChannel 不存在)
- 单元测试 `BroadcastChannelCompat_MultiTabLogoutSync` 通过

**依赖**: 无

## v8 任务依赖链

```
Pre-Task-V8-1 (cleanup_failures) ──┐
                                    ├──→ Task V8-1.2 (TRUNCATE 列表)
                                    ├──→ Task V8-1.7 (CleanupOrphanImagesService)
                                    │
Pre-Task-V8-2 (IObjectStorage 扩展) ─┤
                                    ├──→ Task V8-1.7
                                    │
Pre-Task-V8-3 (SEO 多段 URL, 可选) ──→ 独立
                                    │
Pre-Task-V8-4 (url.ts) ─────────────┐
                                    ├──→ Task V8-4.1 (LoginView 防护)
                                    │
Pre-Task-V8-5 (safeStorage.ts) ─────┐
                                    ├──→ Task V8-4.3 (ErrorBoundary)
                                    ├──→ Task V8-4.4 (errorMonitor)
                                    │
Pre-Task-V8-6 (Mr1Validator) ───────→ 独立
                                    │
Pre-Task-V8-7 (SDK 升级, 延后) ─────→ 独立
                                    │
Pre-Task-V8-8 (MeiliFilterEscape) ──┐
                                    ├──→ Task V8-3.4 (filter 转义)
                                    │
Task V8-1.1 (CrossReference FK) ───→ 独立
Task V8-1.2 (TRUNCATE) ───────────→ 依赖 Pre-Task-V8-1
Task V8-1.3 (ImageKey 统一) ──────→ 独立
Task V8-1.4 (CrossReference 字段) → 独立
Task V8-1.5 (ProductIndexDoc 扩展) → 独立
Task V8-1.6 (EtlImportService 调用) → 独立
Task V8-1.7 (CleanupOrphanImages) → 依赖 Pre-Task-V8-1, Pre-Task-V8-2
Task V8-3.1 (PG SQL 转义) ────────→ 独立
Task V8-3.2 (软删除字段) ─────────→ 独立
Task V8-3.3 (IndexReplayWorker) ──→ 独立
Task V8-3.4 (filter 转义) ────────→ 依赖 Pre-Task-V8-8
Task V8-3.5 (stopWords) ─────────→ 独立
Task V8-3.6 (JavaScriptEncoder) ─→ 独立
Task V8-4.1 (LoginView 防护) ─────→ 依赖 Pre-Task-V8-4
Task V8-4.2 (http.ts isRedirecting) → 独立
Task V8-4.3 (ErrorBoundary) ─────→ 依赖 Pre-Task-V8-5
Task V8-4.4 (errorMonitor) ──────→ 依赖 Pre-Task-V8-5
Task V8-4.5 (CursorHmac V2) ─────→ 独立
Task V8-4.6 (BroadcastChannelCompat) → 独立
```

**总计**: 27 个 v8 补丁任务(8 前置 + 7 数据关联 + 6 检索逻辑 + 6 前后端联动)

---

# v9 补丁任务清单(第八轮审查衍生漏洞修复 + v8 凭空假设纠正)

> **修订日期**: 2026-07-17
> **任务总数**: 20 个(5 前置 + 8 数据关联 + 4 检索逻辑 + 3 前后端联动)
> **核心原则**: 所有任务引用的字段/方法/文件名必须经 Grep + Read 双重核实,杜绝凭空假设
> **依赖关系**: v9 任务在 v8 任务完成后执行,v9 修正 v8 中的 10 项凭空假设

## Phase 0: v9 前置任务(5 个)

### Pre-Task-V9-1: 业务方确认 MR.1 CHK 校验算法
**阻塞**: Task V9-1.6 (Mr1Validator 实现)
**交付物**: 业务方提供的 CHK 算法文档(或确认无 CHK 校验,仅长度+字符集)
**占位方案**: 前 9 位字符 ASCII 求和取模 36(业务方确认前使用)
**验收标准**: 
- [ ] 业务方书面确认 CHK 算法或确认无 CHK 校验
- [ ] 算法文档存档至 `docs/mr1-chk-algorithm.md`

### Pre-Task-V9-2: 确认 Meili filter 字段名统一方案
**阻塞**: Task V9-2.x (MeiliSearchProvider 修改)
**决策点**:
- 方案 A: filter 字段名改 camelCase(与 SDK 序列化一致),需重建 Meili 索引
- 方案 B: 保持 snake_case,但需配置 Meili filterableAttributes 为 snake_case
**推荐**: 方案 A
**验收标准**:
- [ ] 确认方案 A 或 B
- [ ] 若选 A,准备重建索引脚本(`DELETE INDEX products` + `CREATE INDEX products` + 重新 IndexAsync)

### Pre-Task-V9-3: 确认 isSafeRedirect URL 规范化方案
**阻塞**: Task V9-4.x (url.ts 实现)
**推荐**: 方案 A(先 `new URL(path, origin)` 规范化,再校验 hostname)
**验收标准**:
- [ ] 确认方案 A 或 B
- [ ] 测试用例覆盖 `/\evil.com`、`//evil.com`、`https://evil.com`、`/login` 正常路径

### Pre-Task-V9-4: 确认 ErrorBoundary 与 errorMonitor 统一方案
**阻塞**: Task V9-4.x (errorMonitor 统一)
**推荐**: 方案 A(ErrorBoundary 改为调用 errorMonitor.captureException)
**验收标准**:
- [ ] 确认方案 A 或 B
- [ ] AdminErrorView 能读取到 ErrorBoundary 捕获的错误

### Pre-Task-V9-5: 确认 V2 cursor 兼容窗口期
**阻塞**: Task V9-1.x (CursorHmac V2 实现)
**推荐**: 方案 A(V2 上线后保留 V1 Verify 30 天)
**验收标准**:
- [ ] 确认兼容期天数(默认 30 天)
- [ ] 文档记录 V1 废弃时间表

## Phase 1: 数据关联维度(8 个)

### Task V9-1.1: 新建 InitMr1PrimaryKey 迁移(替代 SyncFkConfigurationsV7)
**修复**: V9-F1
**依赖**: 无
**文件**: 新建 `backend/src/SakuraFilter.Infrastructure/Data/Migrations/<timestamp>_InitMr1PrimaryKey.cs`
**子任务**:
- [ ] 1.1.1: 执行 `dotnet ef migrations add InitMr1PrimaryKey`
- [ ] 1.1.2: 迁移 Up 方法:
  ```csharp
  migrationBuilder.AddColumn<string>(
      name: "mr_1",
      table: "products",
      type: "varchar(10)",
      nullable: true);
  // 数据回填(从 oem_2 派生,临时方案)
  migrationBuilder.Sql("UPDATE products SET mr_1 = oem_2 WHERE mr_1 IS NULL AND oem_2 IS NOT NULL;");
  // 唯一索引(注意 CONCURRENTLY 见 S8-6)
  migrationBuilder.CreateIndex(
      name: "idx_products_mr1",
      table: "products",
      column: "mr_1",
      unique: true,
      filter: "mr_1 IS NOT NULL");
  ```
- [ ] 1.1.3: 迁移 Down 方法: DropColumn mr_1
- [ ] 1.1.4: ModelSnapshot 同步
**验证**:
- `dotnet ef database update` 成功
- `SELECT mr_1 FROM products LIMIT 5` 返回非 NULL
- 唯一索引存在: `\d+ products` 显示 idx_products_mr1

### Task V9-1.2: 新建 CrossReferenceConfiguration 独立配置文件
**修复**: V9-F2
**依赖**: 无
**文件**: 新建 `backend/src/SakuraFilter.Infrastructure/Data/Configurations/CrossReferenceConfiguration.cs`
**子任务**:
- [ ] 1.2.1: 创建 IEntityTypeConfiguration<CrossReference> 实现
  ```csharp
  public class CrossReferenceConfiguration : IEntityTypeConfiguration<CrossReference>
  {
      public void Configure(EntityTypeBuilder<CrossReference> e)
      {
          e.ToTable("cross_references");
          e.HasKey(x => x.Id);
          e.Property(x => x.ProductName1).HasMaxLength(100);
          e.Property(x => x.OemBrand).HasMaxLength(100);
          e.Property(x => x.OemNo3).HasMaxLength(100);
          e.HasIndex(x => x.ProductId);
          e.HasIndex(x => new { x.OemBrand, x.OemNo3 });
          // V2 新增: Product 导航属性
          e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
      }
  }
  ```
- [ ] 1.2.2: 修改 ProductDbContext.OnModelCreating L108-117,替换内联配置为:
  ```csharp
  modelBuilder.ApplyConfiguration(new CrossReferenceConfiguration());
  ```
- [ ] 1.2.3: 删除原内联 CrossReference 配置(L108-117)
**验证**:
- `dotnet build` 成功
- `dotnet ef migrations add VerifyCrossRefConfig` 生成的 ModelSnapshot 与原一致(无 schema 变更)

### Task V9-1.3: 修改 ImportProductsAsync TRUNCATE 逻辑(替代 ResetAllDataAsync)
**修复**: V9-F3
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Etl/EtlImportService.cs` L935-937
**子任务**:
- [ ] 1.3.1: 确认 TRUNCATE 列表保持现有 3 张表(products/cross_references/machine_applications)
- [ ] 1.3.2: 注释说明"CASCADE 已覆盖无 FK 约束的关联表"
- [ ] 1.3.3: 不新增 product_images 到 TRUNCATE 列表(该表无 FK,CASCADE 已覆盖)
**验证**:
- ETL 全量导入后,product_images 表数据保留(因无 FK 约束,CASCADE 不影响)
- cross_references/machine_applications 表数据被清空

### Task V9-1.4: 复用死信表机制(废弃 is_dead 字段方案)
**修复**: V9-F5, D8-21
**依赖**: 无
**文件**: 不修改任何文件(保持现有 IndexReplayWorker.ProcessDeadLetterAsync 逻辑)
**子任务**:
- [ ] 1.4.1: 删除 v8 spec 中所有 `ALTER TABLE search_index_pending ADD is_dead` 语句
- [ ] 1.4.2: 删除 v8 spec 中所有 `UPDATE ... SET is_dead = true` 语句
- [ ] 1.4.3: D7-12 修复方案改为"调用现有 ProcessDeadLetterAsync 移动到死信表"
- [ ] 1.4.4: SearchIndexPending 保持现有字段(retry_count/last_error/created_at/next_retry_at)
**验证**:
- IndexReplayWorker 单元测试通过(retry_count >= 5 时移动到 search_index_dead_letter)
- SearchIndexPending 实体无 is_dead/updated_at 字段

### Task V9-1.5: ProductIndexDoc 扩展字段(位置参数 + 可选参数)
**修复**: V9-F7
**依赖**: Task V9-1.1
**文件**: `backend/src/SakuraFilter.Search/ISearchProvider.cs` L32-45
**子任务**:
- [ ] 1.5.1: ProductIndexDoc 追加 3 个可选位置参数:
  ```csharp
  public record ProductIndexDoc(
      long Id,
      string OemNoNormalized,
      string OemNoDisplay,
      string? Remark,
      string Type,
      decimal? D1Mm,
      decimal? D2Mm,
      decimal? H3Mm,
      decimal? H1Mm,
      string? Media,
      bool IsDiscontinued,
      long UpdatedAtUnix,
      // V2 新增(可选,有默认值,向后兼容)
      string? Mr1 = null,
      string? OemBrand = null,
      int? BrandSortOrder = null
  );
  ```
- [ ] 1.5.2: EtlImportService L1158-1166 同步更新位置构造,补充 3 个新字段:
  ```csharp
  var doc = new ProductIndexDoc(
      p.Id, p.OemNoNormalized, p.OemNoDisplay, p.Remark, p.Type,
      p.D1Mm, p.D2Mm, p.H3Mm, p.H1Mm, p.Media, p.IsDiscontinued,
      new DateTimeOffset(p.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds(),
      p.Mr1,  // V2 新增
      p.OemBrand,  // V2 新增(通过 product.OemBrand,见 V9-R1)
      brandSortOrder  // V2 新增(通过 XrefOemBrand 查询,见 S8-15)
  );
  ```
**验证**:
- `dotnet build` 成功
- 现有调用方无需修改(因新增参数有默认值)
- Meili 索引文档包含 mr1/oemBrand/brandSortOrder 字段

### Task V9-1.6: Mr1Validator 静态工具(CHK 算法占位)
**修复**: V9-F10, F7-7, F7-14
**依赖**: Pre-Task-V9-1
**文件**: 新建 `backend/src/SakuraFilter.Core/Validation/Mr1Validator.cs`
**子任务**:
- [ ] 1.6.1: 创建 Mr1Validator 静态类:
  ```csharp
  public static class Mr1Validator
  {
      public const int ExpectedLength = 10;
      // 字符集待 Pre-Task-V9-1 确认(默认 0-9A-Z)
      public const string Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
      
      public static bool IsValid(string? mr1)
      {
          if (string.IsNullOrEmpty(mr1)) return false;
          if (mr1.Length != ExpectedLength) return false;
          if (mr1.Any(c => !Charset.Contains(c))) return false;
          // CHK 校验(待业务方确认,占位实现)
          // TODO: Pre-Task-V9-1 确认后替换为真实算法
          return ValidateChkPlaceholder(mr1);
      }
      
      // 占位 CHK 算法:前 9 位 ASCII 求和取模 36
      // WHY 占位: 无业务文档,需 Pre-Task-V9-1 确认
      private static bool ValidateChkPlaceholder(string mr1)
      {
          var sum = mr1.Take(9).Sum(c => (int)c);
          var chk = sum % 36;
          var chkChar = chk < 10 ? (char)('0' + chk) : (char)('A' + chk - 10);
          return mr1[9] == chkChar;
      }
  }
  ```
- [ ] 1.6.2: 单元测试:
  ```csharp
  [Theory]
  [InlineData("1234567890", true)]  // 待 Pre-Task-V9-1 确认预期值
  [InlineData("ABCDEFGHIJ", true)]
  [InlineData("123456789", false)]   // 长度不足
  [InlineData("12345678901", false)] // 长度超长
  [InlineData("123456789!", false)]  // 非法字符
  public void Mr1Validator_IsValid(string mr1, bool expected) { ... }
  ```
**验证**:
- `dotnet test` 通过
- ETL 与 Admin 均调用 Mr1Validator.IsValid

### Task V9-1.7: EtlEndpoints 限流与认证核实
**修复**: D8-17
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Api/Endpoints/EtlEndpoints.cs`
**子任务**:
- [ ] 1.7.1: 核实 EtlEndpoints.cs 是否同时配置 RequireAuthorization + RequireRateLimiting
- [ ] 1.7.2: 若缺失,补充:
  ```csharp
  group.MapPost("/import", ...).RequireAuthorization("AdminPolicy").RequireRateLimiting("etl");
  ```
- [ ] 1.7.3: 验证 X-Admin-Token header 校验逻辑
**验证**:
- 无 token 请求返回 401
- 超过 30/min 请求返回 429

### Task V9-1.8: ListAllAsync 签名调整(IAsyncEnumerable)
**修复**: D8-19
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Api/Services/IObjectStorage.cs`(或对应接口文件)
**子任务**:
- [ ] 1.8.1: 核实 IObjectStorage 现有签名
- [ ] 1.8.2: ListAllAsync 签名调整为:
  ```csharp
  Task<IAsyncEnumerable<string>> ListAllAsync(string? prefix = null, CancellationToken ct = default);
  ```
- [ ] 1.8.3: MinIO/Aliyun OSS 实现同步更新
**验证**:
- `dotnet build` 成功
- ListAllAsync 返回 IAsyncEnumerable,支持流式枚举

## Phase 2: 检索逻辑维度(4 个)

### Task V9-2.1: Meili filter 字段名统一为 camelCase
**修复**: S8-14
**依赖**: Pre-Task-V9-2
**文件**: `backend/src/SakuraFilter.Search/MeiliSearchProvider.cs` L75-94
**子任务**:
- [ ] 2.1.1: filter 字段名从 snake_case 改为 camelCase:
  ```csharp
  // 修改前(错误)
  filters.Add($"type = \"{EscapeFilter(req.Type)}\"");
  filters.Add($"d1_mm >= {lo} AND d1_mm <= {hi}");
  filters.Add($"is_discontinued = false");
  // 修改后(正确,与 SDK 序列化一致)
  filters.Add($"type = \"{EscapeFilter(req.Type)}\"");
  filters.Add($"d1Mm >= {lo} AND d1Mm <= {hi}");
  filters.Add($"isDiscontinued = false");
  ```
- [ ] 2.1.2: 重建 Meili 索引(Pre-Task-V9-2 方案 A):
  ```bash
  curl -X DELETE http://localhost:7700/indexes/products
  curl -X POST http://localhost:7700/indexes -d '{"uid":"products","primaryKey":"id"}'
  ```
- [ ] 2.1.3: 配置 filterableAttributes:
  ```bash
  curl -X PUT http://localhost:7700/indexes/products/settings/filterable-attributes \
    -d '["type","d1Mm","d2Mm","h1Mm","isDiscontinued","mr1","oemBrand","brandSortOrder"]'
  ```
- [ ] 2.1.4: 重新 IndexAsync 全量数据
**验证**:
- 搜索带 type 过滤返回正确结果
- 搜索带 d1Mm 范围过滤返回正确结果

### Task V9-2.2: EscapeFilter 转义反斜杠
**修复**: S8-4
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Search/MeiliSearchProvider.cs` L141
**子任务**:
- [ ] 2.2.1: EscapeFilter 改为:
  ```csharp
  private static string EscapeFilter(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
  ```
**验证**:
- 单元测试: EscapeFilter(`test\"path`) 返回 `test\\\"path`
- 搜索带反斜杠的 type 返回正确结果

### Task V9-2.3: CONCURRENTLY 索引单独执行(事务外)
**修复**: S8-6
**依赖**: Task V9-1.1
**文件**: `backend/src/SakuraFilter.Infrastructure/Data/Migrations/<timestamp>_InitMr1PrimaryKey.cs`
**子任务**:
- [ ] 2.3.1: 迁移 Up 方法中 CONCURRENTLY 索引单独执行:
  ```csharp
  // 先提交迁移事务
  migrationBuilder.Sql("COMMIT;");
  migrationBuilder.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_products_mr1 ON products (mr1) WHERE mr_1 IS NOT NULL;");
  // 重新开启事务
  migrationBuilder.Sql("BEGIN;");
  ```
- [ ] 2.3.2: 或使用 migrationBuilder.Sql 在事务外执行(需测试 EF Core 行为)
**验证**:
- `dotnet ef database update` 成功
- 索引存在: `\d+ products` 显示 idx_products_mr1

### Task V9-2.4: BuildProductIndexDocAsync 批量预拉 XrefOemBrand(防 N+1)
**修复**: S8-15
**依赖**: Task V9-1.5
**文件**: `backend/src/SakuraFilter.Etl/EtlImportService.cs`
**子任务**:
- [ ] 2.4.1: 新增 BuildProductIndexDocAsync 方法,批量预拉 XrefOemBrand:
  ```csharp
  private async Task<List<ProductIndexDoc>> BuildProductIndexDocAsync(
      List<Product> products, ProductDbContext db, CancellationToken ct)
  {
      // 批量预拉所有 XrefOemBrand,内存按 Brand 分组(O(1) 查找)
      var brands = await db.XrefOemBrands
          .Where(b => b.DeletedAt == null)
          .ToDictionaryAsync(b => b.Brand, b => b.SortOrder, ct);
      
      var docs = new List<ProductIndexDoc>(products.Count);
      foreach (var p in products)
      {
          var brandSortOrder = p.OemBrand != null && brands.TryGetValue(p.OemBrand, out var so) 
              ? so : int.MaxValue;
          docs.Add(new ProductIndexDoc(
              p.Id, p.OemNoNormalized, p.OemNoDisplay, p.Remark, p.Type,
              p.D1Mm, p.D2Mm, p.H3Mm, p.H1Mm, p.Media, p.IsDiscontinued,
              new DateTimeOffset(p.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds(),
              p.Mr1, p.OemBrand, brandSortOrder
          ));
      }
      return docs;
  }
  ```
- [ ] 2.4.2: IndexAsync 调用方改为调用 BuildProductIndexDocAsync
**验证**:
- 1M 产品索引构建,DB 查询次数 ≤ 2(1 次拉产品 + 1 次拉 XrefOemBrand)
- 索引构建时间 < 60s

## Phase 3: 前后端联动维度(3 个)

### Task V9-3.1: CursorHmac V2 新增 SignV2 + VerifyAndExtractV2
**修复**: V9-F4, V9-F6, V9-F8, V9-F9
**依赖**: Pre-Task-V9-5
**文件**: `backend/src/SakuraFilter.Api/Services/CursorHmac.cs`
**子任务**:
- [ ] 3.1.1: 新增 SignV2 方法(不修改原 Sign):
  ```csharp
  public string SignV2(long ticks, long id)
  {
      var payload = $"{ticks}|{id}";
      var hash = HMACSHA256.HashData(_currentKey, Encoding.UTF8.GetBytes(payload));
      return $"V2:{ticks}|{id}|{ToBase64Url(hash)[..16]}";
  }
  ```
- [ ] 3.1.2: 新增 VerifyAndExtractV2 方法(返回可空元组,V2 优先 V1 兜底):
  ```csharp
  public (long Ticks, long Id)? VerifyAndExtractV2(string cursor)
  {
      // V2 优先
      if (cursor.StartsWith("V2:"))
      {
          var body = cursor[3..];
          var parts = body.Split('|', 3);
          if (parts.Length != 3) throw new ArgumentException("V2 cursor 格式错误");
          if (!long.TryParse(parts[0], out var ticks)) throw new ArgumentException("V2 ticks 段解析失败");
          if (!long.TryParse(parts[1], out var id)) throw new ArgumentException("V2 id 段解析失败");
          if (!VerifyKey(_currentKey, parts[0], id, parts[2])
              && (_previousKey == null || !VerifyKey(_previousKey, parts[0], id, parts[2])))
              throw new ArgumentException("V2 cursor 签名验证失败");
          return (ticks, id);
      }
      // V1 兼容(仅主列表,历史页已用 Ticks 与 V2 天然兼容)
      var v1Result = VerifyAndExtract(cursor);  // 调用原方法,返回 (string updatedAtIso, long id)
      if (DateTime.TryParse(v1Result.updatedAtIso, null, 
          System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
          return (dt.Ticks, v1Result.id);
      throw new ArgumentException("V1 cursor updatedAt 解析失败");
  }
  ```
- [ ] 3.1.3: AdminProductService 主列表 L866-868 改为调用 SignV2:
  ```csharp
  var ticks = lastUtc.Ticks;
  var sig = _cursorHmac.SignV2(ticks, last.Id);
  nextCursor = $"V2:{ticks}|{last.Id}|{sig}";
  ```
- [ ] 3.1.4: AdminProductService 主列表 cursor 解析改为 VerifyAndExtractV2
- [ ] 3.1.5: 历史页 L400-401 保持不变(已用 Ticks,与 V2 天然兼容)
**验证**:
- V2 cursor 签名/验签通过
- V1 cursor 在兼容期内验签通过
- 历史页 cursor 不受影响

### Task V9-3.2: isSafeRedirect URL 规范化
**修复**: F7-2, F7-5
**依赖**: Pre-Task-V9-3
**文件**: 新建 `frontend/src/utils/url.ts`
**子任务**:
- [ ] 3.2.1: 创建 url.ts:
  ```typescript
  /**
   * 安全重定向校验
   * WHY: 防止开放重定向漏洞(如 `/\evil.com` 被浏览器规范化为 `//evil.com`)
   * HOW: 先用 new URL 规范化,再校验 hostname
   */
  export function isSafeRedirect(target: string): boolean {
    try {
      const url = new URL(target, window.location.origin)
      return url.hostname === window.location.hostname
    } catch {
      return false
    }
  }
  
  export function safeRedirect(target: string): string | null {
    return isSafeRedirect(target) ? target : null
  }
  ```
- [ ] 3.2.2: http.ts redirectToLogin 调用 isSafeRedirect
- [ ] 3.2.3: 单元测试:
  ```typescript
  expect(isSafeRedirect('/login')).toBe(true)
  expect(isSafeRedirect('/\\evil.com')).toBe(false)
  expect(isSafeRedirect('//evil.com')).toBe(false)
  expect(isSafeRedirect('https://evil.com')).toBe(false)
  expect(isSafeRedirect('javascript:alert(1)')).toBe(false)
  ```
**验证**:
- vitest 通过所有测试用例

### Task V9-3.3: ErrorBoundary 统一到 errorMonitor
**修复**: F7-13
**依赖**: Pre-Task-V9-4
**文件**: `frontend/src/components/ErrorBoundary.vue` L21-38
**子任务**:
- [ ] 3.3.1: ErrorBoundary onErrorCaptured 改为调用 errorMonitor.captureException:
  ```typescript
  import { captureException } from '@/utils/errorMonitor'
  
  onErrorCaptured((err: any) => {
    const info = {
      message: err?.message || String(err),
      stack: err?.stack || '',
      timestamp: new Date().toISOString(),
      url: window.location.href
    }
    error.value = info
    // 统一调用 errorMonitor(单一数据源)
    captureException(err, { component: 'ErrorBoundary' })
    return false
  })
  ```
- [ ] 3.3.2: 删除原 localStorage `sakura_error_log` 写入逻辑(L31-38)
- [ ] 3.3.3: 验证 AdminErrorView 能读取到 ErrorBoundary 捕获的错误
**验证**:
- ErrorBoundary 触发后,errorMonitor.getEvents() 返回包含该错误
- AdminErrorView 显示 ErrorBoundary 捕获的错误
- localStorage `sakura_error_log` 不再写入(可保留旧数据用于迁移)

## v9 任务依赖链

```
Pre-Task-V9-1 (CHK 算法确认) ──────→ Task V9-1.6 (Mr1Validator)
Pre-Task-V9-2 (Meili filter 方案) ─→ Task V9-2.1 (filter 字段名统一)
Pre-Task-V9-3 (isSafeRedirect 方案)→ Task V9-3.2 (url.ts)
Pre-Task-V9-4 (errorMonitor 方案) ─→ Task V9-3.3 (ErrorBoundary 统一)
Pre-Task-V9-5 (V2 兼容期) ────────→ Task V9-3.1 (CursorHmac V2)

Task V9-1.1 (InitMr1PrimaryKey 迁移) ─→ Task V9-1.5 (ProductIndexDoc 扩展)
                                      └→ Task V9-2.3 (CONCURRENTLY 索引)
Task V9-1.5 (ProductIndexDoc 扩展) ───→ Task V9-2.4 (BuildProductIndexDocAsync)
Task V9-1.2 (CrossReferenceConfig) ──→ 独立
Task V9-1.3 (TRUNCATE 修改) ─────────→ 独立
Task V9-1.4 (死信表复用) ────────────→ 独立
Task V9-1.7 (EtlEndpoints 核实) ─────→ 独立
Task V9-1.8 (ListAllAsync 签名) ─────→ 独立
Task V9-2.2 (EscapeFilter) ──────────→ 独立
```

**总计**: 20 个 v9 补丁任务(5 前置 + 8 数据关联 + 4 检索逻辑 + 3 前后端联动)

---

# v10 补丁任务清单(基于 spec.md 第十一章)

> **修订日期**: 2026-07-17
> **触发原因**: 第九轮深度审查发现 v9 仍有 11 项高危凭空假设(自称"0 项凭空假设"是讽刺),其中 V9-R1 错误"纠正"了第八轮审查的正确结论,导致 Task V9-1.5/S8-15 伪代码无法编译
> **核心机制**: 引入"行号+类名"双重核实 — 所有字段引用必须确认所属类;所有 API 调用必须核对签名
> **任务总数**: 23 个(3 前置 + 8 数据关联 + 5 检索逻辑 + 5 前后端联动 + 2 其他修正)

## v10 前置任务(Pre-Task-V10-1 ~ Pre-Task-V10-3)

### Pre-Task-V10-1: 核实 Product.OemBrand 字段是否真的不存在(双重确认)
- **核实方式**: Read [Product.cs#L8-L131](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Core/Entities/Product.cs#L8)
- **核实结论**: 
  - Product 类(L8-95)**没有** OemBrand 字段
  - L127 的 `[Column("oem_brand")] public string? OemBrand` 属于 L122 的 `CrossReference` 类
  - Product 类有 `Oem2`(L23)字段,但**无 OemBrand**
- **影响**: 撤销 V9-R1,恢复第八轮 D8-14/S8-11 结论
- **状态**: ✅ 已完成(本轮 Read 核实)

### Pre-Task-V10-2: 核实 mr_1 字段+索引是否已存在(双重确认)
- **核实方式**: Grep `mr_1` across migrations + Read ModelSnapshot
- **核实结论**:
  - [Product.cs#L22](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Core/Entities/Product.cs#L22) 已有 `[Column("mr_1")] public string? Mr1`
  - InitialCreate 迁移已创建 mr_1 列
  - AddProductsOem2Mr1Indexes 迁移已创建 `ix_products_mr_1` 非 UNIQUE 索引
- **影响**: 废弃 Task V9-1.1 InitMr1PrimaryKey,改为 UpgradeMr1IndexToUnique
- **状态**: ✅ 已完成(本轮 Grep+Read 核实)

### Pre-Task-V10-3: 核实 IObjectStorage.ListAllAsync 是否真的不存在(双重确认)
- **核实方式**: Read [IObjectStorage.cs#L6-L22](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Core/Interfaces/IObjectStorage.cs#L6)
- **核实结论**: 仅 5 个方法(UploadAsync/DeleteAsync/GetUrl/GetPresignedUrlAsync/ExistsAsync),**无 ListAllAsync**
- **影响**: Task V9-1.8 改为"新增方法"(非"签名调整")
- **状态**: ✅ 已完成(本轮 Read 核实)

---

## v10 数据关联模块任务(Task V10-1.1 ~ Task V10-1.8)

### Task V10-1.1: UpgradeMr1IndexToUnique 迁移(替代 Task V9-1.1)
**修复**: V10-F2(mr_1 字段已存在)、V10-F6(列名 mr1 错误)、V10-F18(CONCURRENTLY 破坏原子性)
**调整**: A3(UpgradeMr1IndexToUnique)、A7(mr_1 列名)、A13(非 CONCURRENTLY)
**依赖**: Pre-Task-V10-2
**文件**: 新建 `backend/src/SakuraFilter.Infrastructure/Data/Migrations/<timestamp>_UpgradeMr1IndexToUnique.cs`
**子任务**:
- [ ] 1.1.1: 废弃 Task V9-1.1 的 InitMr1PrimaryKey 迁移(mr_1 字段已存在)
- [ ] 1.1.2: 生成迁移:
  ```bash
  dotnet ef migrations add UpgradeMr1IndexToUnique
  ```
- [ ] 1.1.3: 迁移 Up 方法内容(非 CONCURRENTLY,避免破坏事务原子性 — V10-F18):
  ```csharp
  // 1. 数据去重: 保留最小 id 的记录,其余置 NULL(V10-F2: 字段已存在无需 ADD COLUMN)
  migrationBuilder.Sql(@"
      UPDATE products SET mr_1 = NULL 
      WHERE id NOT IN (
          SELECT MIN(id) FROM products 
          WHERE mr_1 IS NOT NULL 
          GROUP BY mr_1
      );");
  // 2. DROP 旧非 UNIQUE 索引
  migrationBuilder.DropIndex(name: "ix_products_mr_1", table: "products");
  // 3. CREATE UNIQUE 部分索引(V10-F6: 列名用 mr_1 非 mr1)
  migrationBuilder.CreateIndex(
      name: "ix_products_mr_1_unique",
      table: "products",
      column: "mr_1",
      unique: true,
      filter: "mr_1 IS NOT NULL");
  ```
- [ ] 1.1.4: Down 方法回滚(DROP UNIQUE + CREATE 非 UNIQUE)
- [ ] 1.1.5: 不改 mr_1 类型(保持 text,避免数据截断风险)
**验证**:
- `dotnet ef database update` 成功
- `SELECT indexname, indexdef FROM pg_indexes WHERE tablename='products';` 显示 ix_products_mr_1_unique
- 重复 mr_1 值插入被拒绝(测试: `INSERT INTO products(mr_1) VALUES('DUP')` 两次,第二次失败)
- NULL mr_1 不受唯一约束(测试: 多行 mr_1=NULL 插入成功)

### Task V10-1.2: CrossReferenceConfig OnDelete(Cascade)(替代 Task V9-1.2)
**修复**: V10-F14(HasOne 与现有 FK 冲突)
**调整**: A20(OnDelete Cascade)
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs` L108-117
**子任务**:
- [ ] 1.2.1: 新增 CrossReferenceConfiguration 内联配置:
  ```csharp
  // V10-F14: 显式声明 OnDelete 与现有 FK 一致(InitialCreate L200-205 Cascade)
  e.HasOne<Product>()
   .WithMany()
   .HasForeignKey(x => x.ProductId)
   .OnDelete(DeleteBehavior.Cascade);
  ```
- [ ] 1.2.2: 迁移生成后,对比 ModelSnapshot 确认无重复 FK 创建语句
- [ ] 1.2.3: 验证现有 FK `fk_cross_references_products_product_id` 未被替换
**验证**:
- `dotnet ef migrations add CrossReferenceConfig` 生成的迁移 Up 方法无重复 CREATE CONSTRAINT
- ModelSnapshot 中只有一个 `fk_cross_references_products_product_id` FK

### Task V10-1.3: TRUNCATE 修改(继承 Task V9-1.3,无变化)
**修复**: D8-18(cleanup_failures 表不存在)
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Etl/EtlImportService.cs` L935-937
**子任务**:
- [ ] 1.3.1: TRUNCATE 列表仅保留实际存在的 8 张表(删除不存在的 cleanup_failures)
**验证**:
- ETL 全量重建模式 cascade=true 触发 TRUNCATE 不报"relation does not exist"

### Task V10-1.4: 死信表复用描述修正(替代 Task V9-1.4)
**修复**: V10-F13(ProcessDeadLetterAsync 复用 RetryCount 非 RecoveryCount)
**调整**: A19(描述修正)
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs` L138-218
**子任务**:
- [ ] 1.4.1: 核实 [IndexReplayWorker.cs#L186](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs#L186) 实际是 `existingDead.RetryCount = p.RetryCount`
- [ ] 1.4.2: 注释/spec 描述修正: "同 payload 已 recovered 的死信会复用其行(status 重置为 active),并更新 RetryCount/LastError,RecoveryCount 保持不变"
**验证**:
- 代码现状与描述一致(无需修改代码,仅修正 spec)

### Task V10-1.5: ProductIndexDoc 扩展通过 CrossReferences 导航(替代 Task V9-1.5)
**修复**: V10-F1(Product.OemBrand 不存在)、V10-F7(p.OemBrand 无法编译)
**调整**: A1(撤销 V9-R1)、A2(通过 CrossReferences 导航)
**依赖**: Pre-Task-V10-1
**文件**: `backend/src/SakuraFilter.Search/ISearchProvider.cs` L32-45
**子任务**:
- [ ] 1.5.1: ProductIndexDoc record 扩展为 14 个字段(新增 mr1/oemBrand/brandSortOrder):
  ```csharp
  public record ProductIndexDoc(
      int Id, string OemNoNormalized, string OemNoDisplay, string? Remark,
      string Type, decimal? D1Mm, decimal? D2Mm, decimal? H3Mm, decimal? H1Mm,
      string? Media, bool IsDiscontinued, long UpdatedAtUnix,
      // V2 新增
      string? Mr1,
      string? OemBrand,           // 通过 CrossReferences 导航获取(V10-F7)
      int BrandSortOrder          // 通过 XrefOemBrands 关联(V10-F15)
  );
  ```
- [ ] 1.5.2: 撤销 V9-R1 错误"纠正"(spec.md L6399-L6405)
- [ ] 1.5.3: 恢复第八轮 D8-14/S8-11 结论(Product 实体只有 Oem2 字段,无 OemBrand)
**验证**:
- `dotnet build` 通过(ProductIndexDoc 类型定义正确)
- 编译时无 `p.OemBrand` 引用错误

### Task V10-1.6: Mr1Validator CHK 跳过占位(替代 Task V9-1.6)
**修复**: V10-F17(CHK 占位实现会拒绝真实数据)
**调整**: A12(跳过 CHK 校验)
**依赖**: Pre-Task-V9-1(CHK 算法确认,业务方未确认前跳过)
**文件**: 新建 `backend/src/SakuraFilter.Core/Validation/Mr1Validator.cs`
**子任务**:
- [ ] 1.6.1: Mr1Validator 占位实现(仅长度+字符集校验,跳过 CHK):
  ```csharp
  public static class Mr1Validator
  {
      public const int ExpectedLength = 10;
      public const string Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
      
      public static bool IsValid(string? mr1)
      {
          if (string.IsNullOrEmpty(mr1)) return false;
          if (mr1.Length != ExpectedLength) return false;
          if (mr1.Any(c => !Charset.Contains(c))) return false;
          // CHK 校验跳过(待 Pre-Task-V9-1 业务方确认)
          // WHY 跳过: 占位算法与真实算法不同会拒绝所有真实数据(V10-F17)
          // TODO: Pre-Task-V9-1 确认后启用 CHK 校验
          return true;  // 仅长度+字符集校验通过
      }
  }
  ```
- [ ] 1.6.2: 单元测试仅验证长度+字符集(见 Task V10-4.2)
**验证**:
- 单元测试 5 个用例全部通过(见 Task V10-4.2)
- 真实数据导入时 Mr1Validator 不拒绝合法记录

### Task V10-1.7: EtlEndpoints + AdminEtlEndpoints 补充 RequireAuthorization("Admin")(替代 Task V9-1.7)
**修复**: V10-F5(AdminPolicy 凭空假设)、V10-F12(AdminEtlEndpoints 漏认证)
**调整**: A6(策略名 Admin)、A18(范围扩展到 AdminEtlEndpoints)
**依赖**: Pre-Task-V10-1
**文件**: 
- `backend/src/SakuraFilter.Api/Endpoints/EtlEndpoints.cs`
- `backend/src/SakuraFilter.Api/Endpoints/AdminEtlEndpoints.cs` L21
**子任务**:
- [ ] 1.7.1: EtlEndpoints.cs 所有写操作端点补充 RequireAuthorization:
  ```csharp
  // V10-F5: 策略名 "Admin" 非 "AdminPolicy"
  group.MapPost("/trigger", ...).RequireAuthorization("Admin");
  group.MapDelete("/task", ...).RequireAuthorization("Admin");
  ```
- [ ] 1.7.2: AdminEtlEndpoints.cs L21 补充 RequireAuthorization(V10-F12 扩展):
  ```csharp
  var group = app.MapGroup("/api/admin/etl").WithTags("AdminEtl")
      .RequireAuthorization("Admin")              // V10-F5: "Admin" 非 "AdminPolicy"
      .RequireRateLimiting("etl");                 // 原有
  ```
- [ ] 1.7.3: 验证策略名 "Admin" 在 [ServiceCollectionExtensions.cs#L178](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Extensions/ServiceCollectionExtensions.cs#L178) 已注册
**验证**:
- 未携带 admin token 访问 /api/admin/etl/trigger 返回 401/403
- 未携带 admin token 访问 /api/etl/trigger 返回 401/403
- 携带 admin token 访问正常

### Task V10-1.8: 新增 IObjectStorage.ListAllAsync 方法(替代 Task V9-1.8)
**修复**: V10-F3(ListAllAsync 方法凭空假设)
**调整**: A4(新增方法)
**依赖**: Pre-Task-V10-3
**文件**: 
- `backend/src/SakuraFilter.Core/Interfaces/IObjectStorage.cs`
- `backend/src/SakuraFilter.Infrastructure/Storage/MinioStorage.cs`
- `backend/src/SakuraFilter.Infrastructure/Storage/AliyunOssStorage.cs`
- `backend/src/SakuraFilter.Infrastructure/Storage/LocalStorage.cs`
**子任务**:
- [ ] 1.8.1: IObjectStorage.cs 新增 ListAllAsync 方法(V10-F3: 新增非签名调整):
  ```csharp
  /// <summary>列举对象 key(分页 token 由实现自管理)</summary>
  Task<IAsyncEnumerable<string>> ListAllAsync(string? prefix = null, CancellationToken ct = default);
  ```
- [ ] 1.8.2: MinioStorage 实现 ListAllAsync(用 minio.ListObjectsAsync)
- [ ] 1.8.3: AliyunOssStorage 实现 ListAllAsync(用 OssClient.ListObjects)
- [ ] 1.8.4: LocalStorage 实现 ListAllAsync(用 Directory.EnumerateFiles)
- [ ] 1.8.5: 单元测试 mock IObjectStorage 验证调用
**验证**:
- `dotnet build` 通过(所有实现类都新增了方法)
- 单元测试验证 MinIO/Aliyun/Local 三种实现返回的 key 列表正确

---

## v10 检索逻辑模块任务(Task V10-2.1 ~ Task V10-2.5)

### Task V10-2.1: SyncSearchIndexAsync 全量重建端点(扩展 Task V9-2.1)
**修复**: V10-F16(全量重建机制缺失)
**调整**: A11(新增 admin 端点)
**依赖**: Task V10-1.5
**文件**: 
- `backend/src/SakuraFilter.Api/Endpoints/AdminSearchEndpoints.cs`(新增端点)
- `backend/src/SakuraFilter.Etl/EtlImportService.cs` SyncSearchIndexAsync
**子任务**:
- [ ] 2.1.1: 新增 admin 端点强制全量重建(V10-F16):
  ```csharp
  group.MapPost("/reindex", async (EtlImportService etl, CancellationToken ct) =>
  {
      // 临时设 importStartedAt = DateTime.MinValue,强制全量同步
      await etl.SyncSearchIndexAsync(DateTime.MinValue, ct);
      return Results.Ok(new { message = "全量重建完成" });
  }).RequireAuthorization("Admin");
  ```
- [ ] 2.1.2: 重建期间让 ResilientSearchProvider 切到 PG 兜底
- [ ] 2.1.3: 增加锁机制防止并发重建
**验证**:
- POST /api/admin/search/reindex 触发全量重建
- 重建期间公开搜索切到 PG 兜底,不返回空结果
- 并发重建第二次请求返回 409 Conflict

### Task V10-2.2: EscapeFilter 增强(继承 Task V9-2.2,无变化)
**修复**: S8-14(Meili filter 转义不足)
**依赖**: 无
**文件**: `backend/src/SakuraFilter.Search/MeiliSearchProvider.cs` L141
**子任务**:
- [ ] 2.2.1: EscapeFilter 增加转义字符(`\` `(` `)` `~` 等)
- [ ] 2.2.2: 单元测试验证恶意 filter 字符串被正确转义
**验证**:
- 单元测试通过(包含 `"` `\` `(` `)` `~` 的 filter 不破坏 Meili 查询)

### Task V10-2.3: mr_1 列名修正 + UNIQUE 索引统一(并入 Task V10-1.1)
**修复**: V10-F6(列名 mr1 错误)
**调整**: A7(mr_1 列名)
**依赖**: Task V10-1.1
**说明**: 此任务已并入 Task V10-1.1 的 1.1.3 子任务(列名用 mr_1 非 mr1,UNIQUE 索引名 ix_products_mr_1_unique)
**验证**: 同 Task V10-1.1

### Task V10-2.4: BuildProductIndexDocAsync 修正(替代 Task V9-2.4)
**修复**: V10-F7(p.OemBrand 无法编译)、V10-F8(单位错误)、V10-F9(缺失 SpecifyKind)、V10-F15(N+1 修复 brands 字典循环外加载)
**调整**: A8(秒)、A9(SpecifyKind)、A10(brands 循环外)
**依赖**: Task V10-1.5
**文件**: `backend/src/SakuraFilter.Etl/EtlImportService.cs` L1158-1211
**子任务**:
- [ ] 2.4.1: BuildProductIndexDocAsync 方法签名增加 brands 参数(V10-F15):
  ```csharp
  private List<ProductIndexDoc> BuildProductIndexDocAsync(
      IEnumerable<Product> products, 
      Dictionary<string, int> brands)  // V10-F15: 循环外加载
  ```
- [ ] 2.4.2: SyncSearchIndexAsync 循环外加载 brands 字典一次:
  ```csharp
  var brands = await db.XrefOemBrands
      .Where(b => b.DeletedAt == null)
      .ToDictionaryAsync(b => b.Brand, b => b.SortOrder, ct);
  foreach (var batch in batches)
  {
      var docs = BuildProductIndexDocAsync(batch, brands);  // 传入 brands
      // ...
  }
  ```
- [ ] 2.4.3: BuildProductIndexDocAsync 内通过 CrossReferences 导航获取 OemBrand(V10-F7):
  ```csharp
  foreach (var p in products)
  {
      // V10-F7: 通过 CrossReferences 导航属性获取首个 OemBrand
      // WHY: Product 类无 OemBrand 字段(V10-F1),OemBrand 属于 CrossReference 类
      var oemBrand = p.CrossReferences.FirstOrDefault()?.OemBrand;
      var brandSortOrder = oemBrand != null && brands.TryGetValue(oemBrand, out var so) 
          ? so : int.MaxValue;
      docs.Add(new ProductIndexDoc(
          p.Id, p.OemNoNormalized, p.OemNoDisplay ?? "", p.Remark, p.Type ?? "UNKNOWN",
          p.D1Mm, p.D2Mm, p.H3Mm, p.H1Mm, p.Media, p.IsDiscontinued,
          // V10-F8: 保持 ToUnixTimeSeconds()(非毫秒)
          // V10-F9: 保持 SpecifyKind(避免 Day 9.9 已修复的 bug)
          new DateTimeOffset(DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc), TimeSpan.Zero)
              .ToUnixTimeSeconds(),
          p.Mr1,           // V2 新增
          oemBrand,        // V2 新增(通过 CrossReferences)
          brandSortOrder   // V2 新增
      ));
  }
  ```
- [ ] 2.4.4: SyncSearchIndexAsync 查询时 Include CrossReferences:
  ```csharp
  .Select(p => new { 
      Product = p, 
      CrossReferences = p.CrossReferences.Select(c => new { c.OemBrand }).ToList() 
  })
  ```
**验证**:
- `dotnet build` 通过(无 p.OemBrand 编译错误)
- 现有 UpdatedAtUnix 单位保持秒(非毫秒),旧 cursor 排序正常
- 1M 数据全量重建时 brands 字典仅加载 1 次(非 1000 次)
- Meili 索引数据验证 oemBrand/brandSortOrder 字段非空

### Task V10-2.5: V9-R3 撤销(替代 v9 伪修复)
**修复**: V10-F11(V9-R3 凭空假设 v8 body 是两段)
**调整**: A17(撤销 V9-R3)
**依赖**: 无
**文件**: spec.md L6419(V9-R3 章节)
**子任务**:
- [ ] 2.5.1: 撤销 V9-R3(第八轮审查 F7-6 三 核心结论正确)
- [ ] 2.5.2: 保留 V9-F4 修复方案(用 `VerifyKey(parts[0], id, parts[2])` 传两段)
- [ ] 2.5.3: spec 描述修正: "v8 spec payload 格式确实错误(传三段 body 而非两段 ticks|id),由 V9-F4 修复方案处理"
**验证**:
- spec.md L6419 章节标注"已撤销,见 V10-F11"
- CursorHmac.VerifyAndExtractV2 实现使用两段 payload(非三段)

---

## v10 前后端联动模块任务(Task V10-3.1 ~ Task V10-3.5)

### Task V10-3.1: CursorHmac V2 兼容(继承 Task V9-3.1,撤销 V9-R3 部分)
**修复**: F7-6 三(cursor payload 格式错误)、V10-F11(V9-R3 凭空假设)
**调整**: A17(V9-R3 撤销)
**依赖**: Pre-Task-V9-5(V2 兼容期)
**文件**: `backend/src/SakuraFilter.Api/Services/CursorHmac.cs`
**子任务**:
- [ ] 3.1.1: 新增 SignV2(long ticks, long id) 方法(两段 payload):
  ```csharp
  public string SignV2(long ticks, long id)
  {
      var payload = $"{ticks}|{id}";  // 两段(V10-F11: 签名 payload 仅两段)
      // ... HMAC 签名
  }
  ```
- [ ] 3.1.2: 新增 VerifyAndExtractV2 方法(三段 body 拆出两段 payload 验签):
  ```csharp
  public (long ticks, long id) VerifyAndExtractV2(string cursor)
  {
      // cursor = V2:{ticks}|{id}|{sig} 三段(V10-F11: v8 body 确实三段)
      var body = cursor[3..];
      var parts = body.Split('|', 3);
      if (parts.Length != 3) throw new ArgumentException("V2 cursor 格式错误");
      if (!long.TryParse(parts[0], out var ticks)) throw new ArgumentException();
      if (!long.TryParse(parts[1], out var id)) throw new ArgumentException();
      // 验签时传两段(parts[0]=ticks, id, parts[2]=sig) — V9-F4 修复方案
      if (!VerifyKey(_currentKey, parts[0], id, parts[2])
          && (_previousKey == null || !VerifyKey(_previousKey, parts[0], id, parts[2])))
          throw new ArgumentException("V2 cursor 签名验证失败");
      return (ticks, id);
  }
  ```
- [ ] 3.1.3: AdminProductService 主列表 L866-868 改为调用 SignV2
- [ ] 3.1.4: AdminProductService 主列表 cursor 解析改为 VerifyAndExtractV2
- [ ] 3.1.5: 历史页 L400-401 保持不变(已用 Ticks,与 V2 天然兼容)
**验证**:
- V2 cursor 签名/验签通过(两段 payload,三段 body)
- V1 cursor 在兼容期内验签通过
- 历史页 cursor 不受影响

### Task V10-3.2: isSafeRedirect 增加 protocol 校验(替代 Task V9-3.2)
**修复**: V10-F19(漏校验 protocol)
**调整**: A14(hostname + protocol)
**依赖**: Pre-Task-V9-3
**文件**: 新建 `frontend/src/utils/url.ts`
**子任务**:
- [ ] 3.2.1: 创建 url.ts(V10-F19 增加 protocol 校验):
  ```typescript
  /**
   * 安全重定向校验
   * WHY: 防止开放重定向漏洞(如 javascript://example.com/alert(1))
   * HOW: 先用 new URL 规范化,再校验 protocol + hostname
   */
  export function isSafeRedirect(target: string): boolean {
    try {
      const url = new URL(target, window.location.origin)
      // V10-F19: 增加 protocol 校验,防 javascript:// 绕过
      return (url.protocol === 'http:' || url.protocol === 'https:')
        && url.hostname === window.location.hostname
    } catch {
      return false
    }
  }
  
  export function safeRedirect(target: string): string | null {
    return isSafeRedirect(target) ? target : null
  }
  ```
- [ ] 3.2.2: http.ts redirectToLogin 调用 isSafeRedirect
- [ ] 3.2.3: 单元测试(V10-F19 补充 protocol 用例):
  ```typescript
  expect(isSafeRedirect('/login')).toBe(true)
  expect(isSafeRedirect('/\\evil.com')).toBe(false)
  expect(isSafeRedirect('//evil.com')).toBe(false)
  expect(isSafeRedirect('https://evil.com')).toBe(false)
  expect(isSafeRedirect('javascript:alert(1)')).toBe(false)
  // V10-F19 新增
  expect(isSafeRedirect('javascript://example.com/x')).toBe(false)
  expect(isSafeRedirect('data://example.com/x')).toBe(false)
  ```
**验证**:
- vitest 通过所有测试用例(含 protocol 校验)

### Task V10-3.3: ErrorBoundary 统一到 errorMonitor tags.source(替代 Task V9-3.3)
**修复**: V10-F10(captureException API 不匹配)
**调整**: 无(component 改为 tags.source)
**依赖**: Pre-Task-V9-4
**文件**: `frontend/src/components/ErrorBoundary.vue` L21-38
**子任务**:
- [ ] 3.3.1: ErrorBoundary onErrorCaptured 改为调用 errorMonitor.captureException(V10-F10 API 修正):
  ```typescript
  import { captureException } from '@/utils/errorMonitor'
  
  onErrorCaptured((err: any) => {
    const info = {
      message: err?.message || String(err),
      stack: err?.stack || '',
      timestamp: new Date().toISOString(),
      url: window.location.href
    }
    error.value = info
    // V10-F10: captureException options 无 component 字段
    // 现有 API: { level?, tags?, extra? }
    captureException(err, { tags: { source: 'ErrorBoundary' } })
    return false
  })
  ```
- [ ] 3.3.2: 删除原 localStorage `sakura_error_log` 写入逻辑(L31-38)
- [ ] 3.3.3: 验证 AdminErrorView 能读取到 ErrorBoundary 捕获的错误
**验证**:
- ErrorBoundary 触发后,errorMonitor.getEvents() 返回包含该错误,tags.source='ErrorBoundary'
- AdminErrorView 显示 ErrorBoundary 捕获的错误
- localStorage `sakura_error_log` 不再写入

### Task V10-3.4: F7-3 不适用(替代 Task V9-3.4)
**修复**: V10-F20(项目不支持 IE 11)
**调整**: A15(不适用)
**依赖**: 无
**文件**: 无(仅 spec 修正)
**子任务**:
- [ ] 3.4.1: spec.md F7-3 直接降级为"不适用":
  ```
  F7-3 [不适用] 项目不支持 IE 11
  - frontend 无 browserslist 配置,Vite 默认目标现代浏览器
  - Promise.finally 在所有目标浏览器原生支持
  - 无需 polyfill,无需修改
  ```
- [ ] 3.4.2: 删除 v9 spec L6595 语法错误的伪代码(`try { } catch { } then()`)
**验证**:
- spec.md F7-3 章节标注"不适用"
- 无代码修改

### Task V10-3.5: F7-12 http.ts 硬跳转修复(新增)
**修复**: V10-F21(F7-12 凭空假设 v8 spec 有硬跳转)
**调整**: A16(指向 http.ts L94 真实代码)
**依赖**: 无
**文件**: `frontend/src/utils/http.ts` L88-96
**子任务**:
- [ ] 3.5.1: 核实 [http.ts#L94](file:///d:/projects/sakurafilter/frontend/src/utils/http.ts#L94) `window.location.href = ...` 是真实硬跳转
- [ ] 3.5.2: spec F7-12 问题描述改为指向 http.ts L94:
  ```
  F7-12 [中] http.ts redirectToLogin 用 window.location.href 硬跳转
  - 真实代码事实: http.ts L94 `window.location.href = ...` 硬跳转丢失 SPA 上下文
  - 修复方案: 用 router.push 替代(需在 router.isReady 后调用)
  ```
- [ ] 3.5.3: http.ts redirectToLogin 改为 router.push:
  ```typescript
  import router from '@/router'
  
  function redirectToLogin() {
    if (router.isReady()) {
      router.push({ name: 'login', query: { redirect: window.location.pathname } })
    } else {
      // 兜底: router 未就绪时仍用硬跳转(避免无限循环)
      window.location.href = `/login?redirect=${encodeURIComponent(window.location.pathname)}`
    }
  }
  ```
**验证**:
- 401 响应触发 redirectToLogin 后,SPA 上下文保留(Vue 状态不丢失)
- router 未就绪场景兜底硬跳转正常

---

## v10 其他低危修正任务(Task V10-4.1 ~ Task V10-4.2)

### Task V10-4.1: F7-4 删除 mr_1_needs_review SQL(修正 V9 spec)
**修复**: V10-F4(mr_1_needs_review 字段凭空假设)
**调整**: A5(删除该 SQL)
**依赖**: 无
**文件**: spec.md L6605(F7-4 修复方案)
**子任务**:
- [ ] 4.1.1: 删除 spec 中 `UPDATE products SET mr_1_needs_review = true WHERE mr_1 IS NULL;` SQL
- [ ] 4.1.2: 仅保留从 oem_2 派生 mr_1 的 SQL:
  ```sql
  -- 从 oem_2 派生 mr_1(临时方案,业务方确认后替换)
  UPDATE products SET mr_1 = oem_2 WHERE mr_1 IS NULL AND oem_2 IS NOT NULL;
  -- 无需标记复核行,业务方确认 CHK 算法后再统一校验
  ```
**验证**:
- spec.md L6605 无 mr_1_needs_review 引用

### Task V10-4.2: Mr1Validator 单元测试预期值修正
**修复**: V10-F22(V9-F10 占位实现单元测试预期值不可知)
**调整**: 无(配合 V10-F17 CHK 跳过)
**依赖**: Task V10-1.6
**文件**: 新建 `backend/tests/SakuraFilter.Core.Tests/Validation/Mr1ValidatorTests.cs`
**子任务**:
- [ ] 4.2.1: 单元测试仅验证长度+字符集(V10-F22 配合 V10-F17 CHK 跳过):
  ```csharp
  [Theory]
  [InlineData("1234567890", true)]   // 长度+字符集通过
  [InlineData("ABCDEFGHIJ", true)]   // 长度+字符集通过
  [InlineData("123456789", false)]   // 长度不足
  [InlineData("12345678901", false)] // 长度超长
  [InlineData("123456789!", false)]  // 非法字符
  public void Mr1Validator_IsValid(string mr1, bool expected)
  {
      Assert.Equal(expected, Mr1Validator.IsValid(mr1));
  }
  ```
**验证**:
- `dotnet test` 5 个用例全部通过
- 真实数据导入时 Mr1Validator 不拒绝合法记录(CHK 跳过)

---

## v10 任务依赖链

```
Pre-Task-V10-1 (核实 Product.OemBrand) ──→ Task V10-1.5 (ProductIndexDoc 扩展)
                                         └→ Task V10-1.7 (EtlEndpoints 认证)
Pre-Task-V10-2 (核实 mr_1 字段) ────────→ Task V10-1.1 (UpgradeMr1IndexToUnique)
                                         └→ Task V10-2.3 (并入 V10-1.1)
Pre-Task-V10-3 (核实 ListAllAsync) ─────→ Task V10-1.8 (新增方法)

Task V10-1.1 (UpgradeMr1IndexToUnique) ──→ 独立
Task V10-1.2 (CrossReferenceConfig) ─────→ 独立
Task V10-1.3 (TRUNCATE 修改) ───────────→ 独立
Task V10-1.4 (死信表描述修正) ──────────→ 独立
Task V10-1.5 (ProductIndexDoc 扩展) ────→ Task V10-2.1 (全量重建端点)
                                        └→ Task V10-2.4 (BuildProductIndexDocAsync)
Task V10-1.6 (Mr1Validator CHK 跳过) ───→ Task V10-4.2 (单元测试)
Task V10-1.7 (EtlEndpoints 认证) ───────→ 独立
Task V10-1.8 (ListAllAsync 新增) ────────→ 独立

Task V10-2.1 (全量重建端点) ─────────────→ 独立
Task V10-2.2 (EscapeFilter) ─────────────→ 独立
Task V10-2.3 (mr_1 列名) ────────────────→ 并入 Task V10-1.1
Task V10-2.4 (BuildProductIndexDocAsync) → 独立
Task V10-2.5 (V9-R3 撤销) ──────────────→ 独立

Task V10-3.1 (CursorHmac V2) ────────────→ 独立
Task V10-3.2 (isSafeRedirect protocol) ──→ 独立
Task V10-3.3 (ErrorBoundary tags.source) → 独立
Task V10-3.4 (F7-3 不适用) ─────────────→ 独立
Task V10-3.5 (F7-12 http.ts 硬跳转) ─────→ 独立

Task V10-4.1 (F7-4 删除 SQL) ────────────→ 独立
Task V10-4.2 (Mr1Validator 单元测试) ───→ 依赖 Task V10-1.6
```

**总计**: 23 个 v10 补丁任务(3 前置 + 8 数据关联 + 5 检索逻辑 + 5 前后端联动 + 2 其他修正)

---

# v11 补丁任务清单(第十轮深度审查衍生 17 项漏洞修正)

> **修订背景**: v10 自称"0 项凭空假设",但第十轮三维度并行深度审查发现 v10 仍存在 10 项高危凭空假设 + 7 项中低危问题(共 17 项)。
> **修订原则**: 方法存在性 + API 签名双重核实机制 — 所有方法引用必须确认方法存在且签名匹配。
> **修订目标**: 实现 v10 自称但未达成的"真正 0 项凭空假设"。

## v11 前置任务(Pre-Task-V11-1 ~ Pre-Task-V11-6,6 个,全部已完成)

### Pre-Task-V11-1: 核实 BuildProductIndexDocAsync 方法是否真的不存在 ✅
- **核实方式**: Grep `BuildProductIndexDocAsync` 全项目
- **核实结论**: 全项目无匹配,EtlImportService.cs L1158-1166 是内联 lambda
- **影响**: Task V11-2.2 改为"新建方法 BuildProductIndexDocs"
- **状态**: ✅ 已完成

### Pre-Task-V11-2: 核实 LocalStorage 类是否真的不存在 ✅
- **核实方式**: Glob `backend/src/SakuraFilter.Infrastructure/Storage/LocalStorage.cs` + Grep `class LocalStorage`
- **核实结论**: 文件不存在,Storage 目录仅有 MinioStorage + AliyunOssStorage
- **影响**: Task V11-1.4 删除 v10 子任务 1.8.4
- **状态**: ✅ 已完成

### Pre-Task-V11-3: 核实 ProductIndexDoc.Id 类型是否为 long ✅
- **核实方式**: Read ISearchProvider.cs L33 + Product.cs L10
- **核实结论**: `long Id`(两处一致)
- **影响**: Task V11-1.2 撤销 v10 的 int 改动,保持 long
- **状态**: ✅ 已完成

### Pre-Task-V11-4: 核实 SyncSearchIndexAsync 访问修饰符是否为 private ✅
- **核实方式**: Read EtlImportService.cs L1127
- **核实结论**: `private async Task SyncSearchIndexAsync(...)` — 显式 private
- **影响**: Task V11-2.1 新增 public 包装方法 ReindexAllAsync
- **状态**: ✅ 已完成

### Pre-Task-V11-5: 核实 router.isReady() 返回类型是否为 Promise<void> ✅
- **核实方式**: Read router/index.ts L223 + package.json L29 + Vue Router 4 官方 API
- **核实结论**: Vue Router 4.5.0,isReady() 返回 Promise<void>(非同步布尔)
- **影响**: Task V11-3.3 改为 await 模式,路由 name 用 'Login'
- **状态**: ✅ 已完成

### Pre-Task-V11-6: 核实 OemBrand 业务规则(需业务方确认) ⏳
- **核实方式**: 待业务方确认 Product.Oem2 vs CrossReferences.OemBrand 的业务语义
- **临时方案**: 用 Product.Oem2 作为 OemBrand 来源(单值,无歧义)
- **影响**: Task V11-2.2 OemBrand 来源改为 Product.Oem2
- **状态**: ⏳ 待业务方确认(临时用 Product.Oem2)

---

## v11 数据关联任务(V11-1.1 ~ V11-1.4,4 个)

### Task V11-1.1: 修正 Task V10-1.2 — WithMany() 改为带参数(V11-F8)

**问题**: v10 Task V10-1.2 用 `WithMany()`(无参数),会破坏导航属性配置。
**事实**: ProductDbContextModelSnapshot.cs L1707-1715 是 `.WithMany("CrossReferences")`(带参数)。
**修复**:
- 文件: `backend/src/SakuraFilter.Infrastructure/Data/Configurations/CrossReferenceConfiguration.cs`(若存在) 或 OnModelCreating
- 修改: `WithMany()` → `WithMany("CrossReferences")` 或 `WithMany(p => p.CrossReferences)`
- **首选**: `WithMany(p => p.CrossReferences)`(强类型,重构友好)
- **次选**: 保持 `.WithMany("CrossReferences")`(与 ModelSnapshot 一致)

**子任务**:
1.1.1 Read CrossReferenceConfiguration.cs 确认 WithMany 当前写法
1.1.2 修改为带参数形式
1.1.3 `dotnet build` 验证编译通过
1.1.4 创建新 migration `dotnet ef migrations add FixCrossReferenceNavProperty`
1.1.5 比对新 migration 与 ModelSnapshot 是否一致(应无 schema 差异,仅 metadata 修正)

**验证**:
- `dotnet ef migrations script --idempotent` 输出无 DDL 变更(纯 metadata 修正)
- `dotnet build` 编译通过
- 单元测试: Product.C_crossReferences.Add(...) 能正确反查 Product

---

### Task V11-1.2: 修正 Task V10-1.5 — ProductIndexDoc.Id 保持 long(V11-F3)

**问题**: v10 Task V10-1.5 将 Id 类型改为 int,与 ISearchProvider.cs L33 和 Product.cs L10 的 long 不匹配。
**事实**:
- ISearchProvider.cs L33: `public record ProductIndexDoc(long Id, ...)`
- Product.cs L10: `public long Id`
- EtlImportService.cs L1158-1166 内联 lambda: `new ProductIndexDoc(p.Id, ...)`

**修复**:
- 文件: `backend/src/SakuraFilter.Search/ISearchProvider.cs`
- 修改: 撤销 v10 的 `int Id` 改动,保持 `long Id`
- 同步: 检查所有引用 ProductIndexDoc.Id 的代码,确认无 int 强制转换

**子任务**:
1.2.1 Grep `ProductIndexDoc` 全项目,列出所有引用位置
1.2.2 撤销 v10 在 ISearchProvider.cs L33 的 int 改动,恢复 long
1.2.3 检查 Meilisearch 主键配置(`.PrimaryKey("id")`),确认 id 字段类型一致(long 序列化为字符串存储)
1.2.4 检查 IndexReplayWorker.cs L97 `JsonSerializer.Deserialize<ProductIndexDoc>` 是否兼容 long
1.2.5 `dotnet build` 验证编译通过

**验证**:
- ISearchProvider.cs L33 显示 `public record ProductIndexDoc(long Id, ...)`
- `dotnet build` 编译通过
- 现有 ETL 流程不报 InvalidCastException

---

### Task V11-1.3: 修正 Task V10-1.7 — DevTokenAuthMiddleware 设置 ClaimsPrincipal(V11-F9)

**问题**: v10 Task V10-1.7 加 `RequireAuthorization("Admin")` 会破坏 X-Admin-Token 访问,因为 DevTokenAuthMiddleware 验证 token 后未设置 ClaimsPrincipal。
**事实**:
- DevTokenAuthMiddleware.cs L142-172: 验证 X-Admin-Token 后直接 `await _next(ctx)` 放行,**未设置 ClaimsPrincipal**
- ServiceCollectionExtensions.cs L178: `options.AddPolicy("Admin", p => p.RequireRole("admin"))`
- 策略名是 "Admin"(非 "AdminPolicy")

**修复**(两步,缺一不可):
1. **先**: DevTokenAuthMiddleware 验证成功后设置 ClaimsPrincipal
   - 文件: `backend/src/SakuraFilter.Api/Services/DevTokenAuthMiddleware.cs`
   - 修改: L172 `await _next(ctx)` 前,设置 `ctx.User = new ClaimsPrincipal(claimsIdentity)`
   - claimsIdentity 包含 RoleClaim: "admin"(与 AdminPolicy RequireRole("admin") 匹配)
2. **后**: AdminEtlEndpoints 加 `RequireAuthorization("Admin")`(沿用 v10 Task V10-1.7,但前提是步骤 1 已完成)

**子任务**:
1.3.1 修改 DevTokenAuthMiddleware.cs L142-172,验证成功后设置 ClaimsPrincipal
   ```csharp
   var claims = new[] {
       new Claim(ClaimTypes.Name, "dev-token-admin"),
       new Claim(ClaimTypes.Role, "admin")
   };
   var identity = new ClaimsIdentity(claims, "DevToken");
   ctx.User = new ClaimsPrincipal(identity);
   await _next(ctx);
   ```
1.3.2 修改 AdminEtlEndpoints.cs L21(若 L21 是 endpoint 注册位置),加 `.RequireAuthorization("Admin")`
1.3.3 `dotnet build` 验证编译通过
1.3.4 集成测试: 用 X-Admin-Token 访问 /api/etl/trigger-all,应通过 AdminPolicy 校验
1.3.5 集成测试: 无 token 访问 /api/etl/trigger-all,应返回 401

**验证**:
- DevTokenAuthMiddleware 验证成功后 ctx.User.HasClaim(ClaimTypes.Role, "admin") == true
- X-Admin-Token 通道继续可用(向后兼容)
- 普通用户 Cookie 通道未受影响(并行支持两种认证)

---

### Task V11-1.4: 修正 Task V10-1.8 — 删除子任务 1.8.4 LocalStorage(V11-F2)

**问题**: v10 Task V10-1.8 子任务 1.8.4 要求"LocalStorage 实现 ListAllAsync",但 LocalStorage 类全项目不存在。
**事实**:
- Glob `backend/src/SakuraFilter.Infrastructure/Storage/LocalStorage.cs` 无匹配
- Grep `class LocalStorage` 全项目无匹配
- Storage 目录仅 MinioStorage.cs + AliyunOssStorage.cs

**修复**:
- 文件: 无(直接删除 v10 子任务 1.8.4)
- 修改: Task V10-1.8 子任务从 4 个缩减为 3 个(1.8.1 MinioStorage + 1.8.2 AliyunOssStorage + 1.8.3 接口扩展)
- 影响: 不再要求 LocalStorage 实现 ListAllAsync(因为类不存在)

**子任务**(修订后的 v11 版本):
1.8.1 MinioStorage 实现 ListAllAsync(沿用 v10)
1.8.2 AliyunOssStorage 实现 ListAllAsync(沿用 v10)
1.8.3 IObjectStorage 接口扩展 ListAllAsync 签名(沿用 v10)
~~1.8.4 LocalStorage 实现 ListAllAsync~~(删除,因为 LocalStorage 类不存在)

**验证**:
- Grep `class LocalStorage` 全项目仍无匹配(确认未误创建)
- IObjectStorage 接口 ListAllAsync 签名存在
- MinioStorage 和 AliyunOssStorage 的 ListAllAsync 实现存在

---

## v11 检索逻辑任务(V11-2.1 ~ V11-2.4,4 个)

### Task V11-2.1: 修正 Task V10-2.1 — 全量重建端点综合修正(V11-F4/F7/F13/F15)

**问题**: v10 Task V10-2.1 存在 4 个衍生问题:
1. V11-F4: SyncSearchIndexAsync 是 private,L1158-1166 内联,外部无法调用
2. V11-F7: ResilientSearchProvider 无运行时强制切换 API,无法"切到 PG 兜底"
3. V11-F13: AdminSearchEndpoints.cs 文件全项目不存在
4. V11-F15: IndexReplayWorker 旧 payload 反序列化时新字段缺失,未处理

**修复**(4 子任务,缺一不可):

**子任务 2.1.1: 新增 public ReindexAllAsync 包装方法**(V11-F4)
- 文件: `backend/src/SakuraFilter.Etl/EtlImportService.cs`
- 修改: L1127 附近新增 public 包装方法
  ```csharp
  /// <summary>
  /// 全量重建搜索索引(public 包装,供 AdminSearchEndpoints 调用)
  /// WHY public: SyncSearchIndexAsync 是 private,外部端点无法直接调用
  /// </summary>
  public async Task ReindexAllAsync(DateTime? sinceDate, CancellationToken ct)
  {
      var importStartedAt = sinceDate ?? DateTime.UtcNow;
      await SyncSearchIndexAsync(importStartedAt, ct);  // 委托给 private 原方法
  }
  ```

**子任务 2.1.2: 新建 AdminSearchEndpoints.cs 文件**(V11-F13)
- 文件: `backend/src/SakuraFilter.Api/Endpoints/AdminSearchEndpoints.cs`(新建)
- 内容: 注册 POST /api/admin/search/reindex 端点
  ```csharp
  public static class AdminSearchEndpoints
  {
      public static void MapAdminSearchEndpoints(this IEndpointRouteBuilder app)
      {
          var group = app.MapGroup("/api/admin/search")
              .RequireAuthorization("Admin");  // 依赖 Task V11-1.3 已设置 ClaimsPrincipal

          group.MapPost("/reindex", async (
              EtlImportService etlService,
              ResilientSearchProvider searchProvider,
              CancellationToken ct) =>
          {
              // V11-F7: 切到 PG 兜底前先标记主索引不可用
              searchProvider.SetPrimaryAvailable(false);

              try
              {
                  // V11-F15: 全量重建前清空旧 payload,避免 IndexReplayWorker 反序列化失败
                  // WHY TRUNCATE: 旧 payload 可能缺少新字段,反序列化时报错或得到 null
                  //               全量重建会重新生成所有 payload,旧数据无保留价值
                  // 注意: 仅在显式触发全量重建时才 TRUNCATE,普通 ETL 增量导入不动
                  await etlService.TruncateSearchIndexPendingAsync(ct);

                  // 委托给 EtlImportService.ReindexAllAsync(public 包装)
                  await etlService.ReindexAllAsync(sinceDate: null, ct);
                  return Results.Ok(new { message = "全量重建完成" });
              }
              finally
              {
                  // V11-F7: 重建完成后恢复主索引可用
                  searchProvider.SetPrimaryAvailable(true);
              }
          });
      }
  }
  ```

**子任务 2.1.3: ResilientSearchProvider 新增 SetPrimaryAvailable 方法**(V11-F7)
- 文件: `backend/src/SakuraFilter.Search/ResilientSearchProvider.cs`
- 修改: L21 附近新增 public 方法
  ```csharp
  // V11-F7: 运行时强制切换主索引可用性(供 AdminSearchEndpoints 全量重建时调用)
  // WHY 新增: Initialize(bool) 仅启动时初始化,运行时无切换 API
  public void SetPrimaryAvailable(bool available)
  {
      _primaryAvailable = available;
      _logger?.LogWarning("ResilientSearchProvider 主索引可用性切换: {Available}(原因: 全量重建触发)", available);
  }
  ```

**子任务 2.1.4: EtlImportService 新增 TruncateSearchIndexPendingAsync 方法**(V11-F15)
- 文件: `backend/src/SakuraFilter.Etl/EtlImportService.cs`
- 修改: 新增 public 方法
  ```csharp
  /// <summary>
  /// 清空 search_index_pending 表(V11-F15)
  /// WHY: 全量重建前清空旧 payload,避免 IndexReplayWorker 反序列化旧 payload 时新字段缺失
  /// 注意: 仅在显式触发全量重建时调用,普通 ETL 增量导入不动
  /// </summary>
  public async Task TruncateSearchIndexPendingAsync(CancellationToken ct)
  {
      using var scope = _sp.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
      await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE search_index_pending", ct);
      _logger?.LogInformation("search_index_pending 表已清空(全量重建前)");
  }
  ```

**子任务 2.1.5: 注册 AdminSearchEndpoints**
- 文件: `backend/src/SakuraFilter.Api/Extensions/EndpointRouteBuilderExtensions.cs`
- 修改: 在 ETL endpoints 注册之后,加 `app.MapAdminSearchEndpoints();`

**验证**:
- `dotnet build` 编译通过
- 集成测试: POST /api/admin/search/reindex 无 token 返回 401
- 集成测试: POST /api/admin/search/reindex 用 X-Admin-Token 返回 200
- 单元测试: ReindexAllAsync 委托到 SyncSearchIndexAsync(importStartedAt, ct)
- 单元测试: SetPrimaryAvailable(false) 后 SearchAsync 走 PG 兜底
- 单元测试: TruncateSearchIndexPendingAsync 清空后表 row count == 0

---

### Task V11-2.2: 修正 Task V10-2.4 — BuildProductIndexDocs 综合修正(V11-F1/F10/F11/F12/F14)

**问题**: v10 Task V10-2.4 存在 5 个衍生问题:
1. V11-F1: BuildProductIndexDocAsync 方法全项目不存在(L1158-1166 是内联 lambda)
2. V11-F10: 投影返回匿名类型,但签名要求 IEnumerable<Product>,类型不匹配
3. V11-F11: .ToList() 对 1M 行数据内存爆炸
4. V11-F12: FirstOrDefault 无序,业务语义不确定
5. V11-F14: Async 后缀违反 .NET 约定(方法不返回 Task)

**修复**(5 子任务,缺一不可):

**子任务 2.2.1: 新建 BuildProductIndexDocs 方法**(V11-F1, V11-F14)
- 文件: `backend/src/SakuraFilter.Etl/EtlImportService.cs`
- 修改: 从 L1158-1166 内联 lambda 抽取为独立方法
  ```csharp
  /// <summary>
  /// 从 Product 实体构建 ProductIndexDoc(V11-F1: 从内联 lambda 抽取)
  /// WHY 命名 BuildProductIndexDocs(非 Async):
  ///   1. 方法不返回 Task,同步方法不能用 Async 后缀(.NET 约定)
  ///   2. 复数形式表示处理批量
  /// WHY 不内联: 1) AdminSearchEndpoints 全量重建需要复用 2) 单元测试需要直接调用
  /// </summary>
  private static ProductIndexDoc BuildProductIndexDocs(Product p)
  {
      return new ProductIndexDoc(
          Id: p.Id,                                    // V11-F3: long 类型(非 int)
          Mr1: p.Mr1,
          Oem2: p.Oem2,
          // V11-F12: OemBrand 来源改为 Product.Oem2 临时方案
          // WHY: Product 类无 OemBrand 字段,CrossReferences.OemBrand 是集合,FirstOrDefault 无序
          //      业务语义上 Product.Oem2 是单值,无歧义;待 Pre-Task-V11-6 业务方确认后调整
          OemBrand: p.Oem2,                            // 临时方案,待业务方确认
          // ... 其他字段保持与 v10 内联 lambda 一致
          UpdatedAt: new DateTimeOffset(
              DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc)
          ).ToUnixTimeSeconds()                        // Day 9.9 修复保留
      );
  }
  ```
- 同步: L1158-1166 内联 lambda 改为调用 `BuildProductIndexDocs(p)`

**子任务 2.2.2: 改用 .Include 替代投影**(V11-F10)
- 文件: `backend/src/SakuraFilter.Etl/EtlImportService.cs`(以及 Task V11-2.1 中 AdminSearchEndpoints 全量重建查询)
- 修改: 全量重建查询用 `.Include(p => p.CrossReferences)` 加载导航属性,而非投影
  ```csharp
  // V11-F10: 改用 .Include 替代投影,保证返回类型与签名一致(IEnumerable<Product>)
  // WHY: 投影返回匿名类型,但签名要求 IEnumerable<Product>,类型不匹配
  var products = await db.Products
      .Include(p => p.CrossReferences)
      .Where(p => p.UpdatedAt >= sinceDate)
      .ToListAsync(ct);  // 返回 List<Product>,与签名 IEnumerable<Product> 兼容

  // 然后用 BuildProductIndexDocs 转换
  var docs = products.Select(BuildProductIndexDocs).ToList();
  ```

**子任务 2.2.3: FirstOrDefault 不 ToList**(V11-F11)
- 文件: `backend/src/SakuraFilter.Etl/EtlImportService.cs`
- 修改: 任何 `.ToList().FirstOrDefault()` 模式改为 `.FirstOrDefault()` 直接调用
- WHY: `.ToList()` 对 1M 行数据内存爆炸(把整个表加载到内存),`.FirstOrDefault()` 直接翻译为 SQL `LIMIT 1`

**子任务 2.2.4: OemBrand 来源改为 Product.Oem2**(V11-F12)
- 文件: `backend/src/SakuraFilter.Etl/EtlImportService.cs`(子任务 2.2.1 已包含)
- 修改: OemBrand 字段从 `p.CrossReferences.FirstOrDefault()?.OemBrand` 改为 `p.Oem2`
- 临时方案: 待 Pre-Task-V11-6 业务方确认后调整

**子任务 2.2.5: 单元测试 BuildProductIndexDocs**
- 文件: `backend/tests/SakuraFilter.Etl.Tests/EtlImportServiceTests.cs`(若存在,否则新建)
- 测试用例:
  ```csharp
  [Fact]
  public void BuildProductIndexDocs_ReturnsCorrectDoc()
  {
      var product = new Product
      {
          Id = 123L,                                    // long
          Mr1 = "ABC1234567",
          Oem2 = "Toyota",
          UpdatedAt = DateTime.UtcNow
      };

      var doc = EtlImportService.BuildProductIndexDocs(product);

      Assert.Equal(123L, doc.Id);                       // V11-F3: long 类型
      Assert.Equal("ABC1234567", doc.Mr1);
      Assert.Equal("Toyota", doc.Oem2);
      Assert.Equal("Toyota", doc.OemBrand);             // V11-F12: Product.Oem2 临时方案
  }
  ```

**验证**:
- `dotnet build` 编译通过
- `dotnet test` 单元测试通过
- Grep `BuildProductIndexDocAsync` 全项目无匹配(确认未误用旧名)
- Grep `BuildProductIndexDocs` 全项目有 2 处匹配(方法定义 + 单元测试)
- ETL 增量导入流程不报错(内联 lambda 改为方法调用后行为一致)

---

### Task V11-2.3: 修正 Task V10-2.5 — VerifyAndExtractV2 恢复 V1 兜底(V11-F6)

**问题**: v10 Task V10-2.5 撤销 V9-R3 时把 v9 的 V1 兜底逻辑一起改丢,导致 V1 兼容期 cursor 无法验签。
**事实**:
- CursorHmac.cs L89: `public (string updatedAtIso, long id) VerifyAndExtract(string cursor)`
- AdminProductService.cs L400-401: 历史页 cursor 用 Ticks(`Sign(changedAt.Ticks.ToString(), id)`)
- AdminProductService.cs L866-868: 主列表 cursor 用 ISO8601(V1 格式,无 V2: 前缀)

**修复**: 恢复"V2 优先 V1 兜底"设计

**子任务**:
2.3.1 CursorHmac.cs 新增 VerifyAndExtractV2 方法
   ```csharp
   /// <summary>
   /// V2 验签(V2: 前缀),失败时 V1 兜底(V11-F6)
   /// WHY V1 兜底: 历史页 cursor 用 Ticks(V1 格式),V2 切换期需兼容
   /// </summary>
   public (string updatedAtIso, long id, int version) VerifyAndExtractV2(string cursor)
   {
       // 1. 先尝试 V2 验签
       if (cursor.StartsWith("V2:", StringComparison.Ordinal))
       {
           var (iso, id) = VerifyAndExtract(cursor.Substring(3));
           return (iso, id, 2);
       }

       // 2. V2 失败,V1 兜底(原 VerifyAndExtract 逻辑)
       var (v1Iso, v1Id) = VerifyAndExtract(cursor);
       return (v1Iso, v1Id, 1);
   }
   ```
2.3.2 AdminProductService 主列表 cursor 加 V2: 前缀(L866-868 修改)
   ```csharp
   // V2 cursor: "V2:" + ISO8601 + ":" + id + ":" + sig
   var v2Cursor = "V2:" + Sign(updatedAtIso, id);
   ```
2.3.3 AdminProductService 历史页 cursor 保持 V1 格式(L400-401 不变)
   - WHY: 历史页是已有功能,cursor 格式不动,避免破坏已有书签
2.3.4 主列表分页查询时用 VerifyAndExtractV2,自动兼容 V1/V2
2.3.5 单元测试: V2 cursor 验签成功,返回 version=2
2.3.6 单元测试: V1 cursor(无前缀)验签成功,返回 version=1
2.3.7 单元测试: 篡改 cursor(改 Ticks)抛出 UnauthorizedAccessException

**验证**:
- `dotnet build` 编译通过
- V1 cursor(已有书签)继续可用(向后兼容)
- V2 cursor(新格式)验签成功
- 篡改 cursor 抛出异常

---

### Task V11-2.4: 修正 Task V10-2.4 — IndexReplayWorker 旧 payload 兼容性(V11-F15)

**问题**: v10 Task V10-2.4 未处理 IndexReplayWorker 旧 payload 反序列化时新字段缺失。
**事实**:
- IndexReplayWorker.cs L97: `JsonSerializer.Deserialize<ProductIndexDoc>(p.Payload)`
- 旧 payload 可能缺少新字段(如 OemBrand),反序列化时得到 null 或报错

**修复**: 双重保险

**子任务**:
2.4.1 IndexReplayWorker.cs L97 反序列化时容错处理
   ```csharp
   // V11-F15: 旧 payload 可能缺少新字段,反序列化时容错
   // WHY 双重保险:
   //   1. Task V11-2.1 全量重建前 TRUNCATE search_index_pending(清空旧 payload)
   //   2. IndexReplayWorker 反序列化时容错(防止 TRUNCATE 与 Replay 并发的边界情况)
   ProductIndexDoc? doc;
   try
   {
       doc = JsonSerializer.Deserialize<ProductIndexDoc>(p.Payload);
   }
   catch (JsonException ex)
   {
       _logger?.LogWarning(ex, "旧 payload 反序列化失败,跳过(id={Id})", p.Id);
       continue;  // 跳过损坏的 payload,不阻塞队列
   }

   if (doc is null || doc.OemBrand is null)
   {
       // V11-F12: OemBrand 缺失时用 Mr1 兜底(临时方案,待业务方确认)
       doc = doc with { OemBrand = doc.Mr1 ?? "unknown" };
   }
   ```
2.4.2 单元测试: 旧 payload(无 OemBrand)反序列化不报错,OemBrand 兜底为 Mr1
2.4.3 单元测试: 损坏 payload 抛 JsonException 时跳过,不阻塞队列

**验证**:
- `dotnet build` 编译通过
- 旧 payload(无 OemBrand)反序列化不报错
- 损坏 payload 不阻塞队列(continue 跳过)

---

## v11 前后端联动任务(V11-3.1 ~ V11-3.3,3 个)

### Task V11-3.1: 修正 Task V10-3.1 — 历史页 V1 路径描述精确化(V11-F16)

**问题**: v10 Task V10-3.1 描述"历史页与 V2 天然兼容"有歧义,实际历史页用 V1(Ticks)格式,与 V2(ISO8601 + V2: 前缀)不兼容。
**事实**:
- AdminProductService.cs L400-401: 历史页 cursor 用 Ticks(`Sign(changedAt.Ticks.ToString(), id)`)
- Task V11-2.3: 历史页保持 V1 格式(不动),主列表用 V2

**修复**: 精确化文档描述(无代码改动)

**子任务**:
3.1.1 spec.md 第 12.2 节 V11-F16 描述修正(已完成)
3.1.2 在 CursorHmac.cs VerifyAndExtractV2 方法注释中明确:
   - V2: 主列表(新格式,ISO8601 + V2: 前缀)
   - V1: 历史页(已有格式,Ticks,无前缀)
   - 兼容期: VerifyAndExtractV2 自动识别 V2/V1,统一返回
3.1.3 在 AdminProductService.cs L400-401 加注释:
   ```csharp
   // V1 cursor(历史页): Ticks 格式,与主列表 V2 不兼容
   // WHY 保持 V1: 历史页是已有功能,避免破坏已有书签
   // 兼容: VerifyAndExtractV2 自动识别 V1/V2
   var cursor = Sign(changedAt.Ticks.ToString(), id);
   ```

**验证**:
- 代码注释明确区分 V1/V2 路径
- 历史页 cursor 格式不变(向后兼容)
- 主列表 cursor 用 V2 格式

---

### Task V11-3.2: 修正 Task V10-3.2 — isSafeRedirect 补充合法绝对路径测试(V11-F17)

**问题**: v10 Task V10-3.2 isSafeRedirect 测试用例未覆盖合法绝对路径(https://example.com)。
**事实**: 合法绝对路径在生产环境可能合法(如外部 SSO 回跳),测试用例需覆盖。

**修复**: 补充测试用例

**子任务**:
3.2.1 Read isSafeRedirect 当前实现,确认白名单逻辑
3.2.2 补充测试用例:
   ```typescript
   describe('isSafeRedirect', () => {
     // 已有用例(相对路径)...
     it('允许合法绝对路径(白名单内)', () => {
       expect(isSafeRedirect('https://sakurafilter.example.com/callback')).toBe(true)
     })
     it('拒绝非法绝对路径(白名单外)', () => {
       expect(isSafeRedirect('https://evil.com/callback')).toBe(false)
     })
     it('拒绝 javascript: 协议', () => {
       expect(isSafeRedirect('javascript:alert(1)')).toBe(false)
     })
     it('拒绝 data: 协议', () => {
       expect(isSafeRedirect('data:text/html,<script>alert(1)</script>')).toBe(false)
     })
   })
   ```
3.2.3 `npm run test` 验证全部通过

**验证**:
- 合法绝对路径(白名单内)返回 true
- 非法绝对路径(白名单外)返回 false
- javascript: / data: 协议返回 false
- `npm run test` 全部通过

---

### Task V11-3.3: 修正 Task V10-3.5 — router.isReady() await 模式(V11-F5)

**问题**: v10 Task V10-3.5 `if (router.isReady())` 把 Promise<void> 当同步布尔值,永远为 truthy。
**事实**:
- frontend/package.json L29: `"vue-router": "^4.5.0"`
- frontend/src/router/index.ts L223: `const router = createRouter({ history: createWebHistory(), routes })`
- Vue Router 4 官方 API: `isReady(): Promise<void>`,返回 Promise(非布尔)
- frontend/src/router/index.ts L52-55: `{ path: '/login', name: 'Login', ... }`(name 是 'Login',非 'login' 小写)

**修复**: 改为 await 模式 + name 'Login'

**子任务**:
3.3.1 Grep `router.isReady` 全前端,列出所有引用位置
3.3.2 修改为 await 模式:
   ```typescript
   // V11-F5: router.isReady() 返回 Promise<void>,不能用 if 判断
   // WHY: v10 写法 `if (router.isReady())` 永远为 truthy(Promise 对象本身是 truthy)
   router.isReady().then(() => {
     // 路由就绪后的逻辑
   }).catch((err) => {
     console.error('路由初始化失败', err)
   })

   // 或在 async 上下文中:
   // await router.isReady()
   ```
3.3.3 修改 router.push 调用,name 用 'Login'(非 'login'):
   ```typescript
   // V11-F5: name 是 'Login'(首字母大写,与 router/index.ts L52 一致)
   router.push({ name: 'Login' })  // 而非 { name: 'login' }
   ```
3.3.4 检查所有 `name: 'login'` 引用,统一改为 `name: 'Login'`
3.3.5 `npm run build` 验证编译通过
3.3.6 手动测试: 未登录访问 /admin/* 跳转到 /login

**验证**:
- `npm run build` 编译通过
- Grep `name: 'login'` 全前端无匹配(全部改为 'Login')
- Grep `if (router.isReady())` 全前端无匹配(全部改为 await/then 模式)
- 手动测试: 未登录访问 /admin/* 跳转到 /login 正常

---

## v11 任务依赖链

```
Pre-Task-V11-1 (BuildProductIndexDocAsync 不存在) ✅ ──→ Task V11-2.2 (新建方法)
Pre-Task-V11-2 (LocalStorage 不存在) ✅ ─────────────→ Task V11-1.4 (删除子任务 1.8.4)
Pre-Task-V11-3 (Id 类型 long) ✅ ─────────────────────→ Task V11-1.2 (保持 long)
Pre-Task-V11-4 (SyncSearchIndexAsync private) ✅ ─────→ Task V11-2.1 (新增 public 包装)
Pre-Task-V11-5 (isReady Promise) ✅ ──────────────────→ Task V11-3.3 (await 模式)
Pre-Task-V11-6 (OemBrand 业务规则) ⏳ ────────────────→ Task V11-2.2 (临时用 Product.Oem2)

Task V11-1.1 (WithMany 带参数) ──────────→ 独立
Task V11-1.2 (Id 保持 long) ─────────────→ 独立
Task V11-1.3 (DevTokenAuthMiddleware) ───→ Task V11-2.1 (RequireAuthorization 依赖 ClaimsPrincipal)
Task V11-1.4 (删除 LocalStorage 子任务) ─→ 独立

Task V11-2.1 (全量重建端点综合修正) ─────→ Task V11-2.2 (BuildProductIndexDocs 被调用)
                                         └→ Task V11-2.4 (TRUNCATE search_index_pending 配套)
Task V11-2.2 (BuildProductIndexDocs) ────→ 独立
Task V11-2.3 (VerifyAndExtractV2 V1 兜底) → 独立
Task V11-2.4 (IndexReplayWorker 兼容) ───→ 配合 Task V11-2.1

Task V11-3.1 (历史页 V1 描述) ───────────→ 配合 Task V11-2.3
Task V11-3.2 (isSafeRedirect 测试) ──────→ 独立
Task V11-3.3 (router.isReady await) ─────→ 独立
```

**总计**: 17 个 v11 补丁任务(6 前置已完成 + 4 数据关联 + 4 检索逻辑 + 3 前后端联动)
**新增代码文件**: 1 个(AdminSearchEndpoints.cs)
**修改代码文件**: 6 个(EtlImportService.cs / ResilientSearchProvider.cs / DevTokenAuthMiddleware.cs / ISearchProvider.cs / CursorHmac.cs / IndexReplayWorker.cs)
**修改前端文件**: 2 个(http.ts 或 router 相关 / isSafeRedirect 测试文件)
**新增 migration**: 1 个(FixCrossReferenceNavProperty,纯 metadata 修正,无 DDL)

