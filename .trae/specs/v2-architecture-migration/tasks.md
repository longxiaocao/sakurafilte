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
  - [ ] 4.9.1: `npm install dompurify @types/dompurify`
  - [ ] 4.9.2: `frontend/src/utils/html-sanitizer.ts`:
    ```ts
    import DOMPurify from 'dompurify'
    const config: DOMPurify.Config = {
      ALLOWED_TAGS: ['mark'],
      ALLOWED_ATTR: [],
      FORBID_SCRIPT: true,
    }
    export function sanitizeHtml(dirty: string): string {
      return DOMPurify.sanitize(dirty, config) as string
    }
    ```
  - [ ] 4.9.3: ESLint 规则禁止直接 `v-html`,必须经 `sanitizeHtml`
  - **验证**: 单元测试 `sanitizeHtml('<script>alert(1)</script>')` 返回空字符串
  - **依赖**: Task 1.3

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
