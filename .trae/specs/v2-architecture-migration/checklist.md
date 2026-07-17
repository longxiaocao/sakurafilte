# Checklist — V2 架构迁移与 5 项客户需求落地验证

> 对照 spec.md 的 ADDED/MODIFIED Requirements 与 tasks.md 的任务逐项验证。
- [ ] 表示待验证;[x] 表示通过。

---

## Phase 0: 架构前置改造

### MR.1 主键化
- [ ] `products.mr_1` 字段有 UNIQUE 部分索引 `idx_products_mr_1_unique`(WHERE mr_1 IS NOT NULL)
- [ ] `products.mr_1` 字段有 CHECK 约束 `chk_mr_1_format`(正则 `^[A-Za-z0-9]{1,10}$`)
- [ ] products 表新增 8 个尺寸原始字符串列(`d1_mm_raw` / `d2_mm_raw` / `h1_mm_raw` / `d3_mm_raw` / `d4_mm_raw` / `h2_mm_raw` / `h3_mm_raw` / `h4_mm_raw`)
- [ ] `cross_references` 表新增 `sort_order int DEFAULT 0` 字段
- [ ] `cross_references` 表新增 `machine_type varchar(50) DEFAULT 'others'` 字段
- [ ] `cross_references` 表新增 `is_published boolean DEFAULT true` 字段
- [ ] `cross_references` 表有索引 `idx_xrefs_brand_oem3_sort`(oem_brand, sort_order, oem_no_3)
- [ ] `cross_references` 表有唯一约束 `uq_xrefs_brand_oem3`(oem_brand, oem_no_3)
- [ ] `product_images` 表新增 `oem_no_3 varchar(200)` 字段
- [ ] `product_images` 表新增 `image_role varchar(20) DEFAULT 'detail'` 字段
- [ ] `product_images` 表有唯一索引 `uq_product_images_primary`(oem_no_3 WHERE role='primary')
- [ ] `product_images` 表有唯一索引 `uq_product_images_detail_slot`(product_id, slot WHERE role='detail')
- [ ] `machine_applications` 表新增 `machine_category varchar(50) DEFAULT 'others'` 字段
- [ ] `machine_applications` 表有索引 `idx_machine_apps_category`
- [ ] `partition6_placeholder` 空表存在(仅 id + created_at)
- [ ] `system_settings` 表有 7 项新配置(image.primary_naming_field / image.detail_naming_field / search.aggregate_highlight_pre_tag / search.aggregate_highlight_post_tag / search.aggregate_typo_tolerance / seo.url_legacy_redirect_enabled / seo.sitemap_shard_size)

### 实体与配置
- [ ] `Product.cs` 加 8 个尺寸原始字符串属性
- [ ] `CrossReference` 类加 `SortOrder` / `MachineType` / `IsPublished` 属性
- [ ] `ProductImage` 类加 `OemNo3` / `ImageRole` 属性
- [ ] `MachineApplication` 类加 `MachineCategory` 属性
- [ ] `ProductDbContext.OnModelCreating` 声明 MR.1 索引与 CHECK 约束
- [ ] `dotnet build` 通过,ModelSnapshot 与迁移一致

### MR.1 校验逻辑
- [ ] `AdminProductService.ValidateForm` 加 MR.1 格式校验(正则 `^[A-Za-z0-9]{1,10}$`)
- [ ] `AdminProductService.CreateAsync` 加 MR.1 唯一性校验
- [ ] `AdminProductService.UpdateAsync` 409 错误码新增 `MR1_ALREADY_EXISTS`
- [ ] 单元测试 `Mr1_ValidateFormat_Valid` 通过
- [ ] 单元测试 `Mr1_ValidateFormat_TooLong` 通过(11 位抛错)
- [ ] 单元测试 `Mr1_ValidateFormat_InvalidChar` 通过(含 `-` 抛错)
- [ ] 单元测试 `Mr1_Create_Duplicate` 通过

### Meilisearch 索引重构
- [ ] Meilisearch 索引主键改为 `mr_1`
- [ ] `BuildMr1DocumentAsync` 方法存在且聚合 products + cross_references + machine_applications + product_images
- [ ] 嵌套文档结构含 `oem_list` 数组(每项含 oem_brand / oem_no_3 / sort_order / machine_type / is_published)
- [ ] 嵌套文档结构含 `machine_list` 数组(每项含 machine_brand / machine_model / machine_category)
- [ ] 嵌套文档结构含 `image_primary_key` + `image_detail_keys`
- [ ] `searchableAttributes` 配置覆盖 product_name_1/2、oem_2、oem_list.oem_brand、oem_list.oem_no_3、machine_list.*
- [ ] `filterableAttributes` 配置覆盖 type、is_discontinued、oem_list.is_published、machine_list.machine_category、d1_mm/d2_mm/h1_mm
- [ ] `highlightPreTag` / `highlightPostTag` 配置为 `<mark>` / `</mark>`
- [ ] `typoTolerance` 配置为 2
- [ ] `separatorTokens` 配置含空格、连字符、斜杠、逗号、点
- [ ] `PostgresSearchProvider` 适配 MR.1 主键查询
- [ ] `ResilientSearchProvider.IndexAsync` 改为 MR.1 文档级别(任一 OEM 3 变更触发整 MR.1 重建)

---

## Phase 1: 需求 1 + 需求 5

### 需求 1: MR.1 长度 10 位
- [ ] `AdminProductFormView.vue` MR.1 输入框有 `maxlength="10"`
- [ ] `AdminProductFormView.vue` MR.1 输入框有 `pattern="[A-Za-z0-9]{1,10}"`
- [ ] el-form-item rules 含 MR.1 格式校验
- [ ] 提示文案"1-10 位字母+数字"显示
- [ ] 前端单元测试 `ProductForm_Mr1_Validation` 通过

### 需求 5: 聚合搜索 + 高亮
- [ ] `POST /api/public/search/aggregate` 端点存在
- [ ] 请求 DTO `AggregateSearchRequest` 含 q / page / pageSize / tolerance / includeDiscontinued
- [ ] 响应 DTO `AggregateSearchResponse` 含 total / hits / processingTimeMs
- [ ] 每个 hit 含 `_formatted` 字段(高亮)
- [ ] 每个 hit 含 `_rankingScore` 字段
- [ ] `MeiliSearchProvider.SearchAsync` 启用 `AttributesToHighlight`
- [ ] `MeiliSearchProvider.SearchAsync` 启用 `ShowRankingScore`
- [ ] `MeiliSearchProvider.SearchAsync` 配置 `HighlightPreTag="<mark>"`
- [ ] `PostgresSearchProvider` 有聚合搜索兜底实现
- [ ] `AppHeader.vue` 有全局单框搜索框
- [ ] `AggregateSearchView.vue` 存在
- [ ] 搜索结果用 `v-html` 渲染 `_formatted`(XSS 白名单只允许 `<mark>`)
- [ ] 500ms 防抖实现
- [ ] AbortController 取消前序请求实现
- [ ] `router/index.ts` 有 `/search/aggregate?q=` 路由
- [ ] 8 字段高级筛选改为折叠展开组件
- [ ] 单元测试 `Search_Aggregate_Highlight` 通过
- [ ] 单元测试 `Search_Aggregate_TypoTolerance` 通过("BOSHC" 命中 "BOSCH")
- [ ] 单元测试 `Search_Fallback_Pg` 通过
- [ ] 前端单元测试 `Search_Aggregate_HighlightRender` 通过
- [ ] 前端单元测试 `Search_Aggregate_Debounce` 通过

---

## Phase 2: 需求 2(OEM 3 优先展示)

### 后端端点
- [ ] `GET /api/admin/xrefs/reorder/brands` 端点存在,返回 Brand 列表(brand / sortOrder / oem3Count)
- [ ] `GET /api/admin/xrefs/reorder?oemBrand=BOSCH` 端点存在,返回 OEM 3 列表
- [ ] `POST /api/admin/xrefs/reorder` 端点存在,批量更新 sort_order
- [ ] 单个 OEM 3 sort_order 更新用乐观锁(xmin)
- [ ] 批量更新用事务
- [ ] 单元测试 `Oem3_Reorder_BrandGrouping` 通过

### 前端页面
- [ ] `AdminXrefReorderView.vue` 文件存在
- [ ] 页面布局: 左侧 Brand 列表 + 右侧 OEM 3 拖拽排序
- [ ] 使用 `vuedraggable` 库
- [ ] 拖拽完成自动调 API 保存
- [ ] `router/index.ts` 有 `/admin/xrefs/reorder` 路由,`requireAuth: true`
- [ ] AppHeader 后台菜单有"OEM 排序管理"入口
- [ ] `api/index.ts` 有 `adminXrefApi.listBrands` / `listByBrand` / `reorder`
- [ ] 前端单元测试 `XrefReorder_DragDrop` 通过

### 前台排序逻辑
- [ ] `BuildMr1DocumentAsync` 中 `oem_list` 按 `oem_brand.sort_order → oem_no_3.sort_order` 排序后入索引
- [ ] `PublicSearchController` 搜索结果 `oemList` 已排序
- [ ] `PublicProductController` 有 `/api/public/products/{mr1}/sibling-oem3` 端点
- [ ] 端点返回排序后列表
- [ ] 前端使用后端返回的已排序 `oemList`
- [ ] 单元测试 `Oem3_Search_OrderByBrandThenOem3` 通过

---

## Phase 3: 需求 4(图片命名可配置 + 分层)

### BuildKey 改造
- [ ] `BuildKeyAsync` 方法存在(异步)
- [ ] 读 system_settings 配置决定命名字段
- [ ] 主图 key 格式 `products/primary/{namingValue}/{namingValue}-1.{ext}`
- [ ] 详情图 key 格式 `products/detail/{namingValue}/{namingValue}-{slot}.{ext}`
- [ ] 配置缓存(`IMemoryCache` 5 分钟)
- [ ] 单元测试 `Image_BuildKey_Primary_OemNo3` 通过
- [ ] 单元测试 `Image_BuildKey_Detail_Mr1` 通过
- [ ] 单元测试 `Image_BuildKey_ConfigSwitch` 通过

### 上传端点分层
- [ ] `POST /api/admin/products/{mr1}/images/primary?oemNo3=...` 端点存在
- [ ] `POST /api/admin/products/{mr1}/images/detail?slot=2` 端点存在
- [ ] 旧端点 `POST /api/admin/products/{id}/images/{slot}` 已删除
- [ ] 主图冲突返回 `IMAGE_PRIMARY_DUPLICATE`
- [ ] 详情图 slot 冲突返回 `IMAGE_DETAIL_SLOT_DUPLICATE`
- [ ] 单元测试 `Image_Upload_Primary_Duplicate` 通过
- [ ] 单元测试 `Image_Upload_Detail_Slot_Duplicate` 通过

### 前端 UI
- [ ] `AdminProductFormView.vue` 分区 4 拆为主图区 + 详情图区
- [ ] 主图区有 OEM 3 下拉选择
- [ ] 主图区只能上传 1 张
- [ ] 详情图区可上传 slot 2-6
- [ ] `api/index.ts` 有 `imageApi.uploadPrimary(mr1, oemNo3, file)`
- [ ] `api/index.ts` 有 `imageApi.uploadDetail(mr1, slot, file)`
- [ ] `api/types.ts` 的 `ProductImage` 类型含 `oemNo3` / `imageRole` 字段
- [ ] 前端单元测试 `ProductForm_ImageUpload_Primary` 通过
- [ ] 前端单元测试 `ProductForm_ImageUpload_Detail` 通过

---

## Phase 4: 需求 3(SEO URL + SSR + sitemap)

### Razor SSR 详情页
- [ ] `SakuraFilter.Api/Pages/Products/Detail.cshtml.cs` 存在
- [ ] `OnGetAsync(pn1, pn2, brand, oem3)` 方法存在
- [ ] `Detail.cshtml` 服务端渲染 `<h1>` 含 OEM 3 + Product Name
- [ ] `Detail.cshtml` 服务端渲染参数表格(HTML `<table>`)
- [ ] `Detail.cshtml` 服务端渲染适配机型列表(HTML `<ul>`)
- [ ] `Detail.cshtml` 服务端渲染同 MR.1 其他 OEM 3 推荐(HTML `<ul>`)
- [ ] `Detail.cshtml` 含 canonical link
- [ ] `Detail.cshtml` 含 OG meta tags
- [ ] `Detail.cshtml` 含 Vue 局部 hydration 挂载点(`<div id="vue-gallery" data-mr1="..." data-oem3="...">`)
- [ ] `product-detail-hydration.js` 存在,挂载图片画廊/对比按钮/询盘表单
- [ ] `Program.cs` 加 `services.AddRazorPages()` + `app.MapRazorPages()`
- [ ] 单元测试 `Razor_DetailPage_Renders` 校验 HTML 含 `<h1>` + canonical
- [ ] E2E `Public_ProductDetail_SeoMeta` 通过

### 旧 URL 重定向
- [ ] 旧路由 `/product/{oem}` 返回 301
- [ ] 重定向逻辑查 DB 拿 pn1/pn2/brand/oem3 拼 SEO URL
- [ ] OEM 找不到返回 404
- [ ] `seo.url_legacy_redirect_enabled` 配置开关生效
- [ ] E2E `Public_ProductDetail_LegacyRedirect` 通过

### sitemap.xml
- [ ] `GET /sitemap.xml` 端点存在,返回 sitemapindex
- [ ] `GET /sitemaps/products-{shard}.xml` 端点存在
- [ ] 每分片 ≤ 50000 URL
- [ ] 按 mr_1 hash 分片
- [ ] 查询 `cross_references WHERE is_published = true AND is_discontinued = false`
- [ ] 内存缓存(IMemoryCache 1 小时)
- [ ] `seo.sitemap_shard_size` 配置生效
- [ ] 单元测试 `Sitemap_Index_Renders` 通过
- [ ] 单元测试 `Sitemap_Shard_UrlCount` 通过

### 前端路由适配
- [ ] `router/index.ts` 移除 `/product/:oem` 路由
- [ ] 公开搜索结果列表链接改用 SEO URL `/products/{pn1}/{pn2}/{brand}/{oem3}`
- [ ] `PublicCompareView.vue` 产品链接同步改造
- [ ] `PublicProductView.vue` 删除纯 SPA 渲染逻辑
- [ ] E2E `Public_ProductDetail_SeoUrl` 通过

---

## Phase 5: ETL 适配 + 模拟数据 + 测试

### ETL 适配
- [ ] `ImportProductsAsync` 解析 `mr_1` 字段
- [ ] `ImportProductsAsync` 校验 mr_1 格式(1-10 位字母+数字)
- [ ] `ImportProductsAsync` 冲突按 mode 处理(full-load 覆盖 / insert-only 跳过 / upsert 更新)
- [ ] `oem_no_normalized` 从 `mr_1` 派生
- [ ] `ImportXrefsAsync` 解析 `sort_order` 字段
- [ ] `ImportXrefsAsync` 解析 `machine_type` 字段
- [ ] `ImportXrefsAsync` 解析 `is_published` 字段
- [ ] xrefs 关联 `mr_1` 而非 `oem_no_2`
- [ ] 找不着 mr_1 时 `IncrSkippedMissingMr1`
- [ ] `ImportAppsAsync` 解析 `machine_category` 字段
- [ ] COPY 列定义含所有 V2 字段
- [ ] E2E ETL 导入 100 MR.1 / 300 OEM 3 / 500 机型,状态 completed

### 模拟数据
- [ ] `spike-test/_gen_v2_mock_data.py` 脚本存在
- [ ] 生成 `mock_products_v2.jsonl`(100 行)
- [ ] 生成 `mock_xrefs_v2.jsonl`(300 行)
- [ ] 生成 `mock_apps_v2.jsonl`(500 行)
- [ ] 图片占位: 300 张主图上传到 MinIO(key `products/primary/{oem3}/{oem3}-1.png`)
- [ ] 图片占位: 200 张详情图上传到 MinIO(key `products/detail/{mr1}/{mr1}-{slot}.png`)
- [ ] 数据关系: MR.1 一对多 OEM 3
- [ ] 数据关系: OEM 3 一对一主图
- [ ] 数据关系: MR.1 一对多详情图
- [ ] 数据关系: machine_category 覆盖 agriculture/commercial/construction/industrial/others 5 类
- [ ] 脚本执行后 Meilisearch 索引 100 文档
- [ ] 搜索 "BOSCH" 返回预期结果

### 测试套件
- [ ] 后端单元测试全绿(`dotnet test`)
- [ ] 前端单元测试全绿(`npm run test:unit`)
- [ ] 契约测试全绿(`npm run test:contract`)
- [ ] E2E 测试全绿(`npm run test:e2e`)
- [ ] 视觉回归基线重置完成(`public-product-seo` / `public-aggregate-search` / `admin-xref-reorder`)
- [ ] `_test_regression.py --scan` 新增 V2 修复点扫描通过

---

## 客户 4 项确认点落地验证

- [ ] **确认点 1**(需求 2 排序规则): 前台排序按"先 Brand 字典 sort_order,再 OEM 3 sort_order"——`idx_xrefs_brand_oem3_sort` 索引 + `BuildMr1DocumentAsync` 中 oem_list 排序逻辑落地
- [ ] **确认点 2**(需求 3 URL 用 OEM 3): SEO URL 格式 `/products/:pn1/:pn2/:brand/:oem3` 用 OEM 3,不是 OEM 2——`Detail.cshtml.cs` 路由参数 + DB 查询 by oem_no_3
- [ ] **确认点 3**(需求 4 旧数据删除): 一次性脚本 `018_v2_legacy_data_cleanup.sql` TRUNCATE 所有业务表——脚本存在且执行成功
- [ ] **确认点 4**(需求 5 弱中文分词): Meilisearch `separatorTokens` 配置 + PG trgm 兜底,外贸场景中文搜索弱支持——配置生效,文档说明

---

## V2.docx 3 项已确认方案落地验证

- [ ] **方案 B Machine Type 双轨**: `cross_references.machine_type` + `machine_applications.machine_category` 双字段存在;前端分类树读 `machine_applications.machine_category`
- [ ] **方案 A 图片分层**: Image1 主图(`image_role='primary'` + `oem_no_3` 关联) + Image2-6 详情图(`image_role='detail'` + `product_id` 关联 MR.1);`uq_product_images_primary` + `uq_product_images_detail_slot` 唯一约束存在
- [ ] **分区 6 预留空表**: `partition6_placeholder` 表存在(仅 id + created_at),不进 EF Core 业务查询,不展示前端,不进 Meilisearch 索引

---

## 4 轮自查修复点验证

### 第 1 轮:数据关联
- [ ] MR.1 一对多 OEM 3 关联无冲突(`cross_references.product_id` → `products.id`)
- [ ] 同 MR.1 共享尺寸/参数/详情图逻辑无冲突
- [ ] OEM Brand 与 Machine Brand 字段隔离,无混淆
- [ ] 上下架双重校验(MR.1 is_published + OEM 3 is_published)无遗漏
- [ ] 尺寸原始字符串 + 纯数值双存储闭环
- [ ] Meilisearch MR.1 嵌套文档无跨文档查询

### 第 2 轮:业务边界
- [ ] 多 OEM 3 共用一套参数场景无异常
- [ ] 同一机型绑定多个 MR.1 场景无异常
- [ ] 尺寸带特殊符号(如"80.5±0.1")正确解析存储
- [ ] Excel 批量导入重复 MR.1/OEM 3 正确去重或更新
- [ ] 未设置 Machine Type 的 OEM 3 默认 'others' 不报错
- [ ] 无主图的 OEM 3 详情页显示 logo 占位
- [ ] 参数缺失字段详情页显示"—"
- [ ] 多条件叠加筛选正确拼接
- [ ] 全局搜索模糊拼写容错生效

### 第 3 轮:前后端联动
- [ ] 后台 6 大管理模块与 7 张表读写同步无遗漏
- [ ] 数据变更同步 Meilisearch 索引(MR.1 文档级别重建)
- [ ] 前端双维度筛选(左侧机型分类 + 顶部产品类型)交叉过滤正确
- [ ] 详情页同 MR.1 其他 OEM 3 推荐排序正确
- [ ] 主图/详情图展示优先级正确(主图位置显示当前 OEM 3 主图,详情图轮播显示 MR.1 共享图)

### 第 4 轮:已确认需求
- [ ] Machine Type 双轨全程落地,无私自修改
- [ ] 图片方案 A 全程落地,无私自修改
- [ ] 分区 6 预留空表仅后端占位,不参与查询/不展示前端
