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

---

## 47 项漏洞修复验证清单(v2 修订)

> 对照 spec.md 末尾"漏洞修复清单(47 项 → 修复方案映射)",逐项验证修复落地。
> 每个漏洞含: 漏洞描述、修复方案、验证手段、验证状态。

### 一、数据结构与表设计漏洞(20 项)

#### 漏洞 1: product_images (product_id, slot) UNIQUE 约束冲突 [高]
- [ ] 旧约束 `ix_product_images_product_id_slot_unique` 已 DROP
- [ ] 新增 `uq_product_images_primary` 部分唯一索引 `ON product_images (oem_no_3) WHERE image_role = 'primary' AND oem_no_3 IS NOT NULL`
- [ ] 新增 `uq_product_images_detail_slot` 部分唯一索引 `ON product_images (product_id, slot) WHERE image_role = 'detail'`
- [ ] 验证手段: `psql \d product_images` 显示索引列表,旧约束消失,两个新索引存在
- [ ] 单元测试 `Image_Upload_Primary_Duplicate` 通过(同 OEM 3 第二张主图抛 `IMAGE_PRIMARY_DUPLICATE`)
- [ ] 单元测试 `Image_Upload_Detail_Slot_Duplicate` 通过(同 MR.1 slot 重复抛 `IMAGE_DETAIL_SLOT_DUPLICATE`)

#### 漏洞 2: oem_no_normalized UNIQUE 语义矛盾 [高]
- [ ] `products.oem_no_normalized` 旧 UNIQUE 约束 `ix_products_oem_no_normalized_unique` 已 DROP
- [ ] 改为普通索引 `idx_products_oem_no_normalized` (WHERE NOT NULL)
- [ ] `ALTER COLUMN oem_no_normalized DROP NOT NULL` 执行成功(允许 NULL)
- [ ] 验证手段: `psql \d products` 显示 oem_no_nullable + 普通索引
- [ ] 单元测试 `Product_Create_NullOemNoNormalized` 通过(NULL 不抛错)

#### 漏洞 3: cross_references 缺 oem_2 字段 [高]
- [ ] `cross_references` 表新增 `oem_2 varchar(100)` 列
- [ ] ETL `ImportXrefsAsync` 解析 `oem_2` 字段
- [ ] `CrossReference` 实体类加 `Oem2` 属性
- [ ] 验证手段: `psql \d cross_references` 显示 oem_2 列
- [ ] 单元测试 `Xref_Import_Oem2` 通过(oem_2 字段正确入库)

#### 漏洞 4: system_settings INSERT 缺 updated_at [高]
- [ ] 10 项新配置的 INSERT 语句显式带 `updated_at = now()`
- [ ] 验证手段: `SELECT key, updated_at FROM system_settings WHERE key LIKE 'image.%' OR key LIKE 'search.%' OR key LIKE 'seo.%'` 全部 updated_at 非空

#### 漏洞 5: 所有 INSERT 缺 ON CONFLICT [高]
- [ ] system_settings INSERT 末尾加 `ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, description = EXCLUDED.description, updated_at = now()`
- [ ] 脚本可重跑不报错(幂等)
- [ ] 验证手段: 连续执行 2 次 INSERT 脚本,无错误,数据一致

#### 漏洞 6: oem_no_3 nullable 绕过 UNIQUE [高]
- [ ] `ALTER TABLE cross_references ALTER COLUMN oem_no_3 SET NOT NULL` 执行成功
- [ ] `ALTER TABLE cross_references ALTER COLUMN oem_brand SET NOT NULL` 执行成功
- [ ] 验证手段: `psql \d cross_references` 显示两列 not_null=true
- [ ] 单元测试 `Xref_Create_NullOemNo3` 通过(抛 `OEM3_REQUIRED`)
- [ ] 单元测试 `Xref_Create_NullOemBrand` 通过(抛 `OEM_BRAND_REQUIRED`)

#### 漏洞 7: mr_1=NULL 与 oem_no_normalized NOT NULL 冲突 [高]
- [ ] `oem_no_normalized DROP NOT NULL` 与 `mr_1` 部分唯一索引共存(WHERE mr_1 IS NOT NULL)
- [ ] mr_1 为 NULL 时不进唯一索引,允许多行 NULL
- [ ] V2 新数据业务层强制 mr_1 必填(`MR1_REQUIRED` 错误码)
- [ ] 验证手段: 单元测试 `Product_Create_Mr1Required` 通过(V2 数据不填 mr_1 抛错)

#### 漏洞 8: partition6_placeholder 未在 EF Core 注册 [中]
- [ ] 新增 `SakuraFilter.Core/Entities/Partition6Placeholder.cs` 实体类(仅 Id + CreatedAt)
- [ ] `ProductDbContext.OnModelCreating` 显式 `modelBuilder.Entity<Partition6Placeholder>().ToTable("partition6_placeholder").HasKey(e => e.Id)`
- [ ] 不暴露 DbSet(防止业务查询误用)
- [ ] 验证手段: `dotnet build` 通过,ModelSnapshot 含 partition6_placeholder 表

#### 漏洞 9: 字段长度未明确 [中]
- [ ] `mr_1` varchar(10) (CHECK 约束 `^[A-Za-z0-9]{1,10}$`)
- [ ] `oem_no_3` varchar(200)
- [ ] `oem_brand` varchar(100)
- [ ] `oem_2` varchar(100)
- [ ] `machine_type` varchar(50)
- [ ] `machine_category` varchar(50)
- [ ] `image_role` varchar(20)
- [ ] EF Core 配置类 `HasMaxLength()` 全部对齐
- [ ] 验证手段: `psql \d+ cross_references` + `\d+ products` + `\d+ product_images` 字段长度一致

#### 漏洞 10: machine_type 枚举未加 CHECK [中]
- [ ] `cross_references` 加 `chk_xref_machine_type` CHECK 约束(枚举: agriculture/commercial/construction/industrial/others)
- [ ] `machine_applications` 加 `chk_machine_apps_category` CHECK 约束(同枚举)
- [ ] 验证手段: `psql \d+ cross_references` + `\d+ machine_applications` 显示 CHECK 约束
- [ ] 单元测试 `Xref_Create_InvalidMachineType` 通过(非法枚举值抛 `MACHINE_TYPE_INVALID`)

#### 漏洞 11: image_role 未加 CHECK [中]
- [ ] `product_images` 加 `chk_image_role` CHECK 约束(image_role IN ('primary', 'detail'))
- [ ] `product_images` 加 `chk_image_role_slot` CHECK 约束((primary AND slot=1) OR (detail AND slot BETWEEN 2 AND 6))
- [ ] 验证手段: `psql \d+ product_images` 显示两个 CHECK 约束
- [ ] 单元测试 `Image_Upload_InvalidRole` 通过(非法 role 抛错)
- [ ] 单元测试 `Image_Upload_PrimarySlotNot1` 通过(primary + slot=2 抛 `IMAGE_ROLE_SLOT_MISMATCH`)
- [ ] 单元测试 `Image_Upload_DetailSlotInvalid` 通过(detail + slot=7 抛 `IMAGE_DETAIL_SLOT_INVALID`)

#### 漏洞 12: 外键级联策略未明确 [中]
- [ ] `product_images.product_id` 外键声明 `ON DELETE CASCADE`
- [ ] EF Core 配置 `.OnDelete(DeleteBehavior.Cascade)`
- [ ] 验证手段: `psql \d+ product_images` 外键显示 ON DELETE CASCADE
- [ ] 单元测试 `Product_Delete_CascadeImages` 通过(删 MR.1 自动删关联图片行)

#### 漏洞 13: cross_references 无 xmin 并发丢更新 [中]
- [ ] `CrossReference` 实体加 `RowVersion` 属性(byte[])
- [ ] EF Core 配置 `e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken()`(映射到 xmin)
- [ ] OEM 排序管理端点 POST body 含 `rowVersion` 字段
- [ ] 冲突返回 409 `XREF_CONFLICT`
- [ ] 验证手段: 单元测试 `Oem3_Reorder_ConcurrencyConflict` 通过(并发更新抛 DbUpdateConcurrencyException)

#### 漏洞 14: mr_1 与 oem_no_normalized 派生关系未明 [中]
- [ ] ETL `ImportProductsAsync` 中 `oem_no_normalized = mr_1`(临时派生,过渡兼容)
- [ ] 注释明确"V2 过渡期,oem_no_normalized 仅为兼容旧查询,新代码不应使用"
- [ ] 验证手段: ETL 导入后 `SELECT mr_1, oem_no_normalized FROM products WHERE mr_1 IS NOT NULL` 两列值一致

#### 漏洞 15: 索引选择性分析缺失 [中]
- [ ] spec.md "索引设计汇总"表补全每条索引的选择性备注(高/中/低)
- [ ] 高选择性索引(cardinality 接近行数)优先保留
- [ ] 低选择性索引(如 is_published 布尔)改为部分索引(WHERE 条件)
- [ ] 验证手段: spec.md 索引汇总表完整

#### 漏洞 16: cross_references.product_id NOT NULL 未明 [中]
- [ ] `ALTER TABLE cross_references ALTER COLUMN product_id SET NOT NULL` 执行成功
- [ ] 验证手段: `psql \d cross_references` 显示 product_id not_null=true
- [ ] 单元测试 `Xref_Create_NullProduct` 通过(无关联产品抛错)

#### 漏洞 17: products.is_published 与 xref.is_published 区分 [中]
- [ ] spec.md 检索逻辑章节明确: `products.is_published` 为文档级(整 MR.1 下架),`cross_references.is_published` 为 OEM 3 级(单个 OEM 3 下架)
- [ ] Meilisearch 过滤语义: `oem_list.is_published = true AND is_published = true`(双重过滤)
- [ ] 验证手段: spec.md 文档说明清晰
- [ ] 单元测试 `Search_Filter_ProductLevelUnpublished` 通过(MR.1 下架不出现在结果)
- [ ] 单元测试 `Search_Filter_Oem3LevelUnpublished` 通过(单个 OEM 3 下架,该 OEM 3 不出现但同 MR.1 其他 OEM 3 出现)

#### 漏洞 18: product_images.slot 值范围未加 CHECK [中]
- [ ] `chk_image_role_slot` CHECK 约束明确 slot=1(primary)/slot BETWEEN 2 AND 6(detail)
- [ ] 验证手段: 见漏洞 11 验证项

#### 漏洞 19: numeric 字段精度未明 [中]
- [ ] products 表 8 个尺寸字段 `d1_mm/d2_mm/h1_mm/d3_mm/d4_mm/h2_mm/h3_mm/h4_mm` 全部 `numeric(10,2)`
- [ ] `ALTER COLUMN d1_mm TYPE numeric(10,2)` 等 8 个 ALTER 执行成功
- [ ] 验证手段: `psql \d products` 显示 8 个字段类型 numeric(10,2)

#### 漏洞 20: brand_sort_order 查询路径未走单一索引 [中]
- [ ] Meilisearch 文档结构含 `brand_sort_order_min` 字段(预计算,文档级)
- [ ] `BuildMr1DocumentAsync` 中 `brand_sort_order_min = oem_list.Min(o => o.brand_sort_order)`
- [ ] `sortableAttributes` 含 `brand_sort_order_min`
- [ ] 验证手段: Meilisearch `/indexes/products/settings/sortable-attributes` 含 brand_sort_order_min
- [ ] 单元测试 `Search_Sort_ByBrandSortOrderMin` 通过(按 brand_sort_order_min 排序正确)

### 二、检索逻辑与索引漏洞(12 项)

#### 漏洞 1: 聚合搜索响应结构与 MR.1 文档主键矛盾 [高]
- [ ] `AggregateSearchResponse.hits[]` 每项为 1 个 MR.1 文档
- [ ] 每个 hit 含 `oemList` 数组(已按 Brand sort_order → OEM 3 sort_order 排序)
- [ ] 前端展开显示同 MR.1 下所有 OEM 3
- [ ] 验证手段: 单元测试 `Search_Aggregate_DocumentLevel` 通过(同 MR.1 多 OEM 3 只返回 1 个 hit)

#### 漏洞 2: PG 兜底 JOIN 膨胀 [高]
- [ ] `PostgresSearchProvider` 改用 LATERAL JOIN + JSON 聚合
- [ ] SQL 模板: `SELECT p.*, lat_oem.oem_list, lat_machine.machine_list FROM products p LEFT JOIN LATERAL (...) lat_oem ON true LEFT JOIN LATERAL (...) lat_machine ON true WHERE ...`
- [ ] 加 DISTINCT 防重复
- [ ] 验证手段: 单元测试 `Search_Fallback_Pg_NoCartesian` 通过(同 MR.1 3 OEM 3 + 5 机型不膨胀)

#### 漏洞 3: filterableAttributes 严重漏配 [高]
- [ ] Meilisearch `filterableAttributes` 含: type / is_discontinued / is_published / oem_list.is_published / oem_list.oem_brand / oem_list.oem_no_3 / oem_list.oem_2 / oem_list.machine_type / machine_list.machine_brand / machine_list.machine_category / d1_mm / d2_mm / h1_mm
- [ ] 验证手段: `GET /indexes/products/settings/filterable-attributes` 返回列表完整

#### 漏洞 4: _formatted 高亮 XSS [高]
- [ ] 后端 `MeiliSearchProvider.SearchAsync` 返回前对 `_formatted` 做 HTML escape(转义 `<>&"'`)
- [ ] 转义后还原 `<mark>` + `</mark>` 标签
- [ ] 前端 `html-sanitizer.ts` 封装 DOMPurify,白名单只允许 `<mark>`
- [ ] 前端 `v-html` 渲染前必须经过 sanitizer
- [ ] 验证手段: 单元测试 `Search_Aggregate_XssDefense` 通过(注入 `<script>` 被 escape)
- [ ] 前端单元测试 `Search_Aggregate_XssDefense` 通过(DOMPurify 过滤)

#### 漏洞 5: 嵌套数组 filter 语义不明 [高]
- [ ] spec.md 明确"嵌套数组 filter 为 OR 语义(至少一个元素满足)"
- [ ] 文档级冗余字段 `brand_sort_order_min` 支持文档级过滤
- [ ] `oem_list.is_published = true` 表示"至少一个 OEM 3 上架"
- [ ] 验证手段: spec.md 文档说明清晰
- [ ] 单元测试 `Search_Filter_NestedOrSemantics` 通过(任一 OEM 3 满足条件即返回)

#### 漏洞 6: 排序规则缺索引支撑 [中]
- [ ] `brand_sort_order_min` 冗余字段 + `sortableAttributes` 配置
- [ ] `oem_list.sort_order` MIN 语义明确(取最小值作为文档级排序键)
- [ ] 验证手段: 见漏洞 20

#### 漏洞 7: cursor 分页偏移 [中]
- [ ] `CursorHmac.Sign` 改签名为 `Sign(string updatedAtIso, string mr1)`(支持 string MR.1)
- [ ] cursor payload 改为 `{updatedAt, mr1}` JSON
- [ ] 验证手段: 单元测试 `Cursor_NextPage_ByMr1` 通过(按 MR.1 cursor 翻页)

#### 漏洞 8: 停止词配置缺失 [中]
- [ ] Meilisearch `stopWords` 配置: ["the", "a", "an", "of", "for", "and", "or", "to", "in", "on"]
- [ ] 验证手段: `GET /indexes/products/settings/stop-words` 返回列表

#### 漏洞 9: typo 容错 minWordSizeForTypos 未配 [中]
- [ ] `typoTolerance.minWordSizeForTypos.oneTypo = 4` / `twoTypos = 8`
- [ ] system_settings 配置项 `search.aggregate_min_word_size_for_typos=4`
- [ ] 验证手段: `GET /indexes/products/settings/typo-tolerance` 返回配置
- [ ] 单元测试 `Search_Aggregate_TypoTolerance` 通过("BOSHC" 命中 "BOSCH")

#### 漏洞 10: 嵌套字段排序语义未明 [中]
- [ ] spec.md 明确: 嵌套字段排序取 MIN 语义
- [ ] `brand_sort_order_min` 为文档级,直接 sort
- [ ] `oem_list.sort_order` 取 MIN 作为文档级排序键
- [ ] 验证手段: spec.md 文档说明清晰

#### 漏洞 11: PG ILIKE 转义未说 [中]
- [ ] `PostgresSearchProvider` 复用 `LikeEscapeExtensions.EscapeLikePattern`
- [ ] 使用 3 参 ILike (`EF.Functions.ILike(pattern, query, escapeChar)`)
- [ ] 验证手段: 单元测试 `Search_Fallback_Pg_LikeEscape` 通过(含 `%` `_` 的搜索词不触发全表扫描)

#### 漏洞 12: 分页深度限制缺失 [中]
- [ ] `AggregateSearchRequest.page` 加校验 `page > 100` 抛 `SEARCH_PAGE_TOO_DEEP`
- [ ] max_page_depth=100 配置在 system_settings
- [ ] 验证手段: 单元测试 `Search_Aggregate_PageTooDeep` 通过(page=101 抛错)

### 三、前后端联动链路漏洞(15 项)

#### 漏洞 1: nginx.conf 路由未配置 [高]
- [ ] `docker/nginx.conf` 新增 location:
  - `location ~ ^/products/` → proxy_pass http://backend:8080
  - `location ~ ^/product/` → proxy_pass http://backend:8080 (旧 URL 301)
  - `location ~ ^/(sitemap\.xml|sitemaps/)` → proxy_pass http://backend:8080
  - `location = /robots.txt` → proxy_pass http://backend:8080
- [ ] 验证手段: `curl -I http://localhost/products/oil-filter/spin-on/bosch/F000000001` 返回 200 + Content-Type: text/html(非 SPA index.html)
- [ ] 验证手段: `curl -I http://localhost/sitemap.xml` 返回 200 + Content-Type: application/xml

#### 漏洞 2: router 移除路由与 SPA 跳转冲突 [高]
- [ ] spec.md "SEO 与部署方案/SPA 跳转改造"列全项目清单: `router.push('/product/...')` → `window.location.href = '/products/...'`
- [ ] 涉及文件: PublicSearchView.vue / PublicCompareView.vue / PublicProductView.vue / AppHeader.vue 搜索跳转
- [ ] 验证手段: 全项目 grep `router.push.*product/` 无遗留(仅保留 SPA 内部跳转)

#### 漏洞 3: Vue 3 无原生局部 hydration [高]
- [ ] spec.md 明确: 不用 hydration,改用 client mount 模式
- [ ] `product-detail-client.js` 使用 `createApp(GalleryApp).mount('#vue-gallery')`
- [ ] SSR 阶段 div 内可留空(SEO 内容在外层 HTML)
- [ ] 验证手段: 浏览器禁用 JS,详情页 SEO 内容(h1/表格/列表)仍可见
- [ ] 验证手段: 单元测试 `Razor_DetailPage_NoVueDependency` 通过

#### 漏洞 4: ProblemDetailsFactory 错误码命名不一致 [高]
- [ ] 新增 V2 错误码全部大写下划线格式(无 ERR_ 前缀)
- [ ] V2 错误码清单: MR1_ALREADY_EXISTS / MR1_FORMAT_INVALID / MR1_REQUIRED / OEM3_ALREADY_EXISTS / OEM3_REQUIRED / OEM_BRAND_REQUIRED / MACHINE_TYPE_INVALID / XREF_CONFLICT / IMAGE_PRIMARY_DUPLICATE / IMAGE_DETAIL_SLOT_DUPLICATE / IMAGE_ROLE_SLOT_MISMATCH / IMAGE_DETAIL_SLOT_INVALID / SEARCH_PAGE_TOO_DEEP
- [ ] 旧 `ERR_*` 错误码保留映射(向后兼容)
- [ ] `appsettings.json` 加 `ErrorCodes:LegacyPrefix: "ERR_"` 配置
- [ ] 验证手段: 单元测试 `ProblemDetails_ErrorCode_LegacyCompat` 通过(旧 ERR_CONFLICT 仍识别为新 XREF_CONFLICT)

#### 漏洞 5: AdminProductImageService 签名不兼容 [高]
- [ ] `UploadAsync` 新签名: `(string mr1, string imageRole, string? oemNo3, short slot, Stream stream, string contentType, CancellationToken ct)`
- [ ] 旧签名 `(long productId, short slot, ...)` 删除
- [ ] 调用方 `AdminProductEndpoints.cs` 适配
- [ ] 验证手段: `dotnet build` 通过,无旧签名调用残留

#### 漏洞 6: CursorHmac 签名改造 [中]
- [ ] `CursorHmac.Sign` 改为 `Sign(string updatedAtIso, string mr1)`(string 类型 MR.1)
- [ ] `CursorHmac.Verify` 对应改造
- [ ] 调用方 `PublicSearchController` 适配
- [ ] 验证手段: 单元测试 `CursorHmac_SignVerify_Mr1String` 通过

#### 漏洞 7: RateLimit "public" 策略缺失 [中]
- [ ] `Program.cs` `AddRateLimiter` 加 "public" 策略(120/min,基于 RemoteIpAddress)
- [ ] 公开搜索端点用 `[EnableRateLimiting("public")]` 标注
- [ ] 验证手段: 压测 121 次请求,第 121 次返回 429

#### 漏洞 8: ExemptPaths 死配置 /api/products [中]
- [ ] `appsettings.json` `ExemptPaths` 数组移除 `/api/products`(死配置)
- [ ] 验证手段: appsettings.json 文件检查,无 `/api/products` 条目

#### 漏洞 9: IndexReplayWorker 批次大小/并发未明 [中]
- [ ] spec.md 明确: 复用现有 BatchSize=500
- [ ] 加 `SemaphoreSlim(1)` 并发限制(单实例同时只跑 1 个批次)
- [ ] 验证手段: spec.md 文档说明清晰
- [ ] 单元测试 `IndexReplay_BatchSize500_Concurrency1` 通过

#### 漏洞 10: sitemap 内存缓存键未明 [中]
- [ ] sitemap 索引缓存键: `sitemap:index`
- [ ] sitemap 分片缓存键: `sitemap:shard:{shard}`
- [ ] 缓存 TTL 1 小时
- [ ] 验证手段: 单元测试 `Sitemap_CacheKey` 通过

#### 漏洞 11: Vue 局部 hydration 时序问题 [中]
- [ ] `product-detail-client.js` defer 加载
- [ ] 脚本位置在 `</body>` 前
- [ ] 挂载失败不影响 SEO 内容(渐进增强)
- [ ] 验证手段: 模拟 JS 加载失败,SEO 内容仍可见
- [ ] 验证手段: 单元测试 `VueMount_DeferLoad` 通过

#### 漏洞 12: 前端图片懒加载 [低]
- [ ] `<img>` 标签加 `loading="lazy"` 属性
- [ ] 关键主图(首屏)不加 lazy
- [ ] 验证手段: 前端代码检查

#### 漏洞 13: 路由懒加载 [低]
- [ ] router 动态 import() 懒加载所有路由
- [ ] 验证手段: `npm run build` 后 chunk 分割正常

#### 漏洞 14: SEO meta tags 服务端注入 [中]
- [ ] `Detail.cshtml` 用 `@model` 渲染 `og:title` / `og:description` / `canonical` / `og:image`
- [ ] 验证手段: `curl http://localhost/products/...` HTML 含 og:title 等标签
- [ ] E2E `Public_ProductDetail_SeoMeta` 通过

#### 漏洞 15: 404 页面 SEO URL 不存在时 [中]
- [ ] Razor 404 页含站内搜索入口
- [ ] 404 页 HTTP 状态码正确返回 404(不是 200)
- [ ] 验证手段: `curl -I http://localhost/products/oil-filter/spin-on/bosch/NOT_EXIST` 返回 404
- [ ] E2E `Public_ProductDetail_404` 通过

---

## 第二轮深度审查验证点(已完成 → 修复方案见 v3 修订)

> 第二轮三维度并行审查已执行,发现 64 个衍生漏洞(去重后 62 个),其中高危 19、中危 38、低危 5
> v3 修订已系统性修复全部衍生漏洞,验证点见下方"62 项衍生漏洞修复验证清单"

### 数据关联维度第二轮审查(已检出 22 项 → 全部修复)
- [x] product_images 旧约束 DROP 后,历史数据是否有依赖该约束的业务代码(残留 SELECT) → **D2/D4** 修复:清理 ProductDbContext Fluent API
- [x] oem_no_normalized DROP NOT NULL 后,是否有 NOT NULL 校验残留代码 → **D2** 修复:移除 `.IsRequired()`
- [x] cross_references 加 oem_2 后,是否有 OEM 2 在 products 表的残留读写 → **D8** 修复:保存 xrefs 后反向更新 products.oem_2
- [x] system_settings ON CONFLICT 改造后,旧 INSERT 单条逻辑是否兼容 → **D17** 修复:pg_advisory_lock(20260717)
- [x] partition6_placeholder 注册后,是否误进入 Meilisearch 索引构建逻辑 → **D14** 修复:仅 EF Core 迁移创建,SQL 脚本不 CREATE TABLE
- [x] mr_1 部分唯一索引(WHERE mr_1 IS NOT NULL)与 NULL 多行共存的边界 → **D20** 修复:Fluent API HasFilter
- [x] cross_references.xmin 乐观锁在 ETL 全量导入场景下的行为(大批量更新冲突) → 已明确 ETL 用 ON CONFLICT 走 upsert 路径,不走乐观锁

### 检索逻辑维度第二轮审查(已检出 22 项 → 全部修复)
- [x] Meilisearch 嵌套文档 oem_list 排序后,搜索结果展示顺序是否一致 → **S17** 修复:新增文档级 oem_list_sort_order_min 字段
- [x] LATERAL JOIN 兜底在 1M 数据量下的查询计划(EXPLAIN ANALYZE) → **S2** 修复:LATERAL 内 LIMIT 50 + CTE 预计算
- [x] filterableAttributes 补全后,Meilisearch 索引大小是否膨胀超阈值 → **S15** 修复:6-10GB 估算 + max-indexing-memory 8192
- [x] _formatted HTML escape 后,中文高亮是否仍正常(mark 标签位置正确) → **S1** 修复:占位符替换法
- [x] brand_sort_order_min 冗余字段在 OEM 3 上下架切换时是否及时更新 → **S8** 修复:XrefOemBrandService.UpdateAsync 触发重建 + 5s 去重
- [x] cursor 分页 page > 100 抛错后,前端是否有友好提示 → **S13** 修复:cursor 加 24h TTL + 版本前缀 + 错误码
- [x] typoTolerance minWordSizeForTypos=4 配置后,3 字短词(如"BMW")搜索是否失效 → **S4** 修复:改为 oneTypo:3 / twoTypos:5

### 前后端联动维度第二轮审查(已检出 20 项 → 全部修复)
- [x] nginx 路由 /products/ 与 SPA /products/:pn1/:pn2/:brand/:oem3 路由是否冲突 → **F1/F12** 修复:CommonEndpoints 移除根路由 + 路由注册顺序
- [x] Vue client mount 在 SSR HTML 含 `<mark>` 高亮场景下的渲染(画廊组件是否冲突) → **F2** 修复:挂载点分离
- [x] ProblemDetailsFactory 旧 ERR_* 映射的覆盖范围(是否有遗漏错误码) → **F3** 修复:错误码迁移矩阵 + http.ts 双格式兼容
- [x] AdminProductImageService 签名改造后,旧单测是否全部更新 → **F4** 修复:调用点改造清单 + BuildKey 旧重载标 Obsolete
- [x] CursorHmac string MR.1 改造后,旧 cursor 客户端兼容性 → **F5/S13** 修复:版本前缀 v2: + 7 天过渡期
- [x] RateLimit "public" 策略对 SEO 爬虫(Googlebot)是否误伤 → **F6** 修复:Googlebot 白名单 + sitemap 单独策略
- [x] sitemap 缓存失效后,首次请求的响应时间(缓存击穿防护) → **F7** 修复:SemaphoreSlim(1,1) + double-check
- [x] product-detail-client.js defer 加载顺序(依赖 Vue 全局变量时) → **F8** 修复:manualChunks + modulepreload + try-catch

---

## 62 项衍生漏洞修复验证清单(v3 修订)

> 对照 spec.md 末尾"第二轮深度审查衍生漏洞修复清单(v3 修订)",逐项验证修复落地。
> 每个漏洞含: 漏洞编号、修复方案、验证手段、验证状态。

### 一、数据关联维度衍生漏洞(22 项)

#### D1 [高]: spec DROP INDEX 语句索引名与实际数据库不一致
- [ ] `ProductDbContext.cs:86` 的 `IsUnique()` 已移除,改为 `HasFilter("oem_no_normalized IS NOT NULL")`
- [ ] `ProductDbContext.cs:153` 的 `IsUnique()` 已移除,改为两个部分唯一索引 + `HasFilter`
- [ ] 迁移脚本 DROP 语句修正:`DROP INDEX IF EXISTS ix_products_oem_no_normalized`(无 `_unique` 后缀)
- [ ] 迁移脚本 DROP 语句修正:`DROP INDEX IF EXISTS uq_products_oem_normalized`(008 脚本创建)
- [ ] 迁移脚本 DROP 语句修正:`DROP INDEX IF EXISTS ix_product_images_product_id_slot`(无后缀)
- [ ] 验证手段: `dotnet ef migrations script --idempotent` 输出无 `AlterColumn(... nullable: false)` 或旧 `CreateIndex(... unique: true)` 重建
- [ ] 关联任务: Task 0.2.8.1/0.2.8.2/0.2.8.3

#### D2 [高]: ProductDbContext 残留 .IsRequired() / .IsUnique() 与 spec 改造冲突
- [ ] `ProductDbContext.cs:62` 的 `.IsRequired()` 已移除(oem_no_normalized 允许 NULL)
- [ ] `ProductDbContext.cs:86` 的 `IsUnique()` 改为 `HasFilter`
- [ ] `ProductDbContext.cs:153` 的 `IsUnique()` 改为两个部分唯一索引
- [ ] 验证手段: `git diff ProductDbContext.cs` 显示三处配置变更
- [ ] 关联任务: Task 0.2.8.1/0.2.8.2/0.2.8.3

#### D3 [中]: spec 误用 byte[]/ulong? 描述 RowVersion,实际应为 uint
- [ ] spec.md L521 已改为 `e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken()`
- [ ] spec.md 明确"复用现有 RowVersion uint 映射 xmin;禁用 byte[]/ulong?,Npgsql 抛 InvalidCastException"
- [ ] 验证手段: spec.md grep `byte\[\]` 无 RowVersion 相关匹配

#### D4 [中]: product_images 外键名与 spec DROP CONSTRAINT 不匹配
- [ ] spec DROP 语句修正:`DROP CONSTRAINT IF EXISTS fk_product_images_products_product_id`(实际名)
- [ ] spec 明确"现有外键已是 CASCADE,spec 改为'验证现有外键策略,无需 DROP/ADD'"
- [ ] 验证手段: `psql \d product_images` 显示 `ON DELETE CASCADE`

#### D5 [高]: ETL ON CONFLICT (oem_no_normalized) 在 V2 后报 42P10
- [ ] `EtlImportService.cs:976` 改为 `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL DO NOTHING`
- [ ] `EtlImportService.cs:993` 改为 `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL DO UPDATE SET ...`
- [ ] 验证手段: 单元测试 `Etl_Upsert_Mr1_OnConflict` 通过
- [ ] 关联任务: Task 5.1.8.1/5.1.8.2

#### D6 [高]: ETL ON CONFLICT (product_id, oem_brand, oem_no_3) 与 V2 新唯一索引不匹配
- [ ] `EtlImportService.cs:1470` 改为 `ON CONFLICT (oem_brand, oem_no_3) WHERE is_discontinued = false DO NOTHING`
- [ ] `EtlImportService.cs:1478` 改为 `ON CONFLICT (oem_brand, oem_no_3) WHERE is_discontinued = false DO UPDATE SET ...`
- [ ] 验证手段: 单元测试 `Etl_Upsert_Xref_OnConflict` 通过
- [ ] 关联任务: Task 5.1.8.3

#### D7 [中]: AdminProductService.CreateAsync 未实现 MR.1 必填/唯一校验
- [ ] `AdminProductService.ValidateForm` 加 MR.1 格式校验(正则 `^[A-Za-z0-9]{1,10}$`)
- [ ] `AdminProductService.ValidateForm` 加 MR.1 必填校验
- [ ] `AdminProductService.CreateAsync` 加 MR.1 唯一性校验
- [ ] 验证手段: 单元测试 `Mr1_Create_Duplicate` + `Mr1_ValidateFormat_*` 通过
- [ ] 关联任务: Task 0.3.1/0.3.2/0.3.3

#### D8 [中]: AdminProductService Oem2 处理与"代表值"语义不一致
- [ ] `AdminProductService.CreateAsync/UpdateAsync` 保存 xrefs 后反向更新 `products.oem_2` 为第一个 xref.oem_2
- [ ] 验证手段: 集成测试 `AdminProduct_UpdateXref_Oem2Backfill` 通过
- [ ] 关联任务: Task 5.1.9

#### D9 [中]: oem_no_normalized 派生关系大小写冲突
- [ ] spec 明确"V2 中 oem_no_normalized 不再保证唯一性,派生规则改为 `oem_no_normalized = mr_1`(保留原大小写)"
- [ ] 验证手段: spec grep `oem_no_normalized = ` 显示派生规则

#### D10 [中]: numeric(10,2) ALTER TYPE 截断旧数据
- [ ] spec 明确执行顺序:`先 TRUNCATE 旧数据 → 再 ALTER 字段类型`
- [ ] ALTER TYPE 语句补 `USING d1_mm::numeric(10,2)` 子句
- [ ] 验证手段: 迁移脚本检查 TRUNCATE 在 ALTER TYPE 之前

#### D11 [低]: idx_xrefs_brand_oem3_sort 与现有 ix_cross_references_oem_brand_oem_no_3 索引重叠
- [ ] spec 明确"DROP 旧索引 `ix_cross_references_oem_brand_oem_no_3`,新增 `idx_xrefs_brand_oem3_sort`(选择性更优)"
- [ ] 验证手段: `psql \d cross_references` 显示旧索引消失,新索引存在

#### D12 [中]: cross_references.oem_no_3 长度与 EF Core 配置不一致
- [ ] `ProductDbContext.cs:114` 改为 `e.Property(x => x.OemNo3).HasMaxLength(200)`
- [ ] 验证手段: ModelSnapshot 显示 `HasMaxLength(200)`
- [ ] 关联任务: Task 0.2.8.4

#### D13 [中]: system_settings updated_at EF Core 配置层缺默认值
- [ ] `ProductDbContext.cs:164` 后补 `e.Property(s => s.UpdatedAt).HasDefaultValueSql("now()")`
- [ ] 验证手段: ModelSnapshot 显示 `HasDefaultValueSql("now()")`
- [ ] 关联任务: Task 0.2.8.5

#### D14 [中]: partition6_placeholder 双重创建(SQL + EF Core 迁移)冲突
- [ ] spec L592-595 的 `CREATE TABLE IF NOT EXISTS partition6_placeholder` SQL 移除
- [ ] 仅通过 EF Core 迁移创建
- [ ] EF Core 配置 `e.ToTable("partition6_placeholder").HasKey(x => x.Id)` + `e.Property(x => x.CreatedAt).HasDefaultValueSql("now()")`
- [ ] 验证手段: 迁移脚本无重复 CREATE TABLE
- [ ] 关联任务: Task 0.2.9

#### D15 [中]: product_images.slot CHECK 与 image_role DEFAULT 'detail' 对 slot=1 旧数据冲突
- [ ] spec 明确执行顺序:`先 TRUNCATE product_images → 再 ADD COLUMN image_role → 再 ADD CONSTRAINT chk_image_role_slot`
- [ ] 迁移脚本 TRUNCATE 在 ADD COLUMN 之前
- [ ] 验证手段: 迁移脚本检查顺序

#### D16 [中]: 图片命名配置切换旧图不迁移导致前端显示断裂
- [ ] `product_images` 表新增 `naming_field varchar(20)` 字段(记录 'oem_no_3' 或 'mr_1')
- [ ] `BuildKeyAsync` 写入时记录 naming_field
- [ ] 前端查询 DB 拿 key,不根据配置动态生成
- [ ] 验证手段: 集成测试:切换配置后,旧 OEM 3 详情页图片仍可显示
- [ ] 关联任务: Task 3.2.9

#### D17 [低]: system_settings ON CONFLICT 多实例并发丢更新(性能问题)
- [ ] spec 部署文档明确:"V2 迁移脚本仅单实例执行,通过 `pg_advisory_lock(20260717)` 防止并发"
- [ ] 迁移脚本头部加 `SELECT pg_advisory_lock(20260717);` 末尾加 `SELECT pg_advisory_unlock(20260717);`
- [ ] 验证手段: 脚本 grep `pg_advisory_lock` 命中

#### D18 [低]: cross_references.product_id 外键策略未明
- [ ] spec 明确:"cross_references.product_id → products.id ON DELETE CASCADE(已存在,无需修改)"
- [ ] 验证手段: `psql \d cross_references` 显示 `ON DELETE CASCADE`

#### D19 [中]: oem_no_normalized DROP NOT NULL 后 ETL 旧代码路径派生关系未明
- [ ] spec 明确:"V2 新数据(含 mr_1):oem_no_normalized = mr_1;旧数据(无 mr_1):保留源值,mr_1=NULL;V2 迁移后旧数据全部 TRUNCATE"
- [ ] ETL 派生逻辑:`oem_no_normalized = mr_1 ?? oem_2`(降级路径)
- [ ] 验证手段: 单元测试 `Etl_DeriveOemNoNormalized` 通过

#### D20 [中]: EF Core [Index] 特性不支持 WHERE 条件的部分索引
- [ ] Fluent API 配置:`e.HasIndex(p => p.Mr1).IsUnique().HasFilter("mr_1 IS NOT NULL").HasDatabaseName("idx_products_mr_1_unique")`
- [ ] Product.cs 移除可能的 `[Index]` 特性(若有)
- [ ] 验证手段: ModelSnapshot 显示 HasFilter
- [ ] 关联任务: Task 0.2.8.6

#### D21 [中]: xref_oem_brand 字典与 cross_references.oem_brand 外键缺失
- [ ] spec 明确:"cross_references.oem_brand 不加外键,仅为字符串引用"
- [ ] 前端 typeahead 过滤 `deleted_at IS NULL`
- [ ] 验证手段: `psql \d cross_references` 显示 oem_brand 无外键约束

#### D22 [中]: ETL COPY 列清单可能误包含 xmin 系统列
- [ ] `EtlImportService.cs` 中 COPY products_stage 和 cross_references_stage 列清单明确排除 xmin
- [ ] 验证手段: ETL 全量导入 100 万行,无 xmin 相关报错
- [ ] 关联任务: Task 5.1.7

### 二、检索逻辑维度衍生漏洞(22 项)

#### S1 [高]: _formatted HTML escape + <mark> Replace 还原存在二次 XSS 漏洞
- [ ] `MeiliSearchProvider.SearchAsync` 返回前处理占位符替换:
  - `<mark>` → `\u0001MARK_OPEN\u0001`
  - `</mark>` → `\u0001MARK_CLOSE\u0001`
  - HtmlEncode 后还原占位符
- [ ] 验证手段: 单元测试 `Search_Aggregate_XssDefense_LiteralMarkTag` 通过(录入产品名 `<mark>test</mark>` 渲染为纯文本)
- [ ] 关联任务: Task 1.2.13

#### S2 [高]: LATERAL JOIN 兜底 SQL 在 1M 数据量下性能严重退化
- [ ] LATERAL 内部加 `LIMIT 50`(单 MR.1 最多 50 OEM 3)
- [ ] 移除 `json_agg(DISTINCT ...)` 的 DISTINCT
- [ ] ORDER BY 改 CTE 预计算 brand_sort_order_min
- [ ] 验证手段: `EXPLAIN ANALYZE` 单条查询 < 100ms(1M 数据量)
- [ ] 关联任务: Task 1.2.10/1.2.11

#### S3 [中]: oem_list.is_published 嵌套过滤 OR 语义与前端展示不一致
- [ ] spec 明确:"后端响应层过滤:`includeDiscontinued=false` 时响应中 oemList 仅含 isPublished=true 的项"
- [ ] `MeiliSearchProvider.SearchAsync` 返回前对 oemList 过滤
- [ ] 验证手段: 单元测试 `Search_Aggregate_OemList_FilterUnpublished` 通过
- [ ] 关联任务: Task 1.2.15

#### S4 [高]: typoTolerance minWordSizeForTypos=4 致 3 字品牌缩写无法容错
- [ ] `typoTolerance.minWordSizeForTypos` 改为 `{oneTypo: 3, twoTypos: 5}`
- [ ] system_settings 拆分为 `search.typo_min_word_size_one_typo=3` + `search.typo_min_word_size_two_typos=5`
- [ ] 验证手段: 单元测试 `Search_Aggregate_TypoTolerance_3LetterBrand`("BNW" 命中 "BMW")通过
- [ ] 关联任务: Task 1.2.12.1

#### S5 [高]: separatorTokens 含 - 致 OEM 号 F-000000001 被错误分割
- [ ] `separatorTokens` 改为 `[" ", "/", ",", "."]`(移除 `-`)
- [ ] 新增 `nonSeparatorTokens: ["-"]`
- [ ] 验证手段: 单元测试 `Search_Aggregate_OemNo3_Hyphen_Precise`(`F-000000001` 精确命中)通过
- [ ] 关联任务: Task 1.2.12.2/1.2.12.3

#### S6 [中]: stopWords 含 of/for/and 致型号 OF-100 误删词
- [ ] `stopWords` 改为 `["the", "a", "an"]`(移除 of/for/and)
- [ ] 验证手段: 单元测试 `Search_Aggregate_StopWords_OfInModel`(`OF-100` 不误命中 `D100`)通过
- [ ] 关联任务: Task 1.2.12.4

#### S7 [高]: Meilisearch 索引主键改 mr_1 后无停机迁移策略
- [ ] 创建新索引 `products_v2`(主键 `mr_1`),配置 filterableAttributes
- [ ] 后台批量写入 V2 文档(不影响现有 `products` 索引)
- [ ] 切换 `MeiliSearchOptions.IndexName = "products_v2"`(热切换)
- [ ] 验证搜索结果一致性后,删除旧索引 `products`
- [ ] (可选)重命名 `products_v2` → `products`
- [ ] `ProductIndexDoc` record 重写为 `Mr1IndexDoc` 嵌套结构
- [ ] `DeleteAsync(IEnumerable<long>)` 改为 `DeleteAsync(IEnumerable<string> mr1s)`
- [ ] 验证手段: 集成测试 `Meili_IndexMigration_ZeroDowntime` 通过
- [ ] 关联任务: Task 0.4.12/0.4.13/0.4.14

#### S8 [中]: brand_sort_order_min 更新时机与 Brand 字典 sort_order 变更联动缺失
- [ ] `XrefOemBrandService.UpdateAsync` 触发后台任务批量重建相关 MR.1 文档
- [ ] `ResilientSearchProvider.IndexAsync` 加 5 秒内同 MR.1 去重(`ConcurrentDictionary<string, DateTime>`)
- [ ] 验证手段: 单元测试 `XrefBrand_Update_TriggersReindex` 通过

#### S9 [中]: PG 兜底未返回 _formatted 与 _rankingScore
- [ ] `PostgresSearchProvider` 实现 `BuildFormatted(string? source, string query)` 方法(Regex.Replace + `<mark>` 包裹)
- [ ] `_rankingScore` 固定 0.5
- [ ] 前端 v-html 兜底回退显示原始字段
- [ ] 验证手段: 单元测试 `Search_Fallback_Pg_FormattedHighlight` 通过
- [ ] 关联任务: Task 1.2.8

#### S10 [中]: PG 兜底查询字段不完整
- [ ] PG WHERE 补全:product_name_1/2 + oem_2 + EXISTS cross_references(oem_brand/oem_no_3/oem_2) + EXISTS machine_applications(machine_brand/model)
- [ ] 验证手段: 对比测试 `Search_Meili_vs_Pg_Recall` 召回数差异 < 5%
- [ ] 关联任务: Task 1.2.9

#### S11 [中]: PG 兜底排序逻辑与 Meilisearch 不一致
- [ ] PG ORDER BY 三层:`brand_sort_order_min ASC → oem_list_sort_order_min ASC → updated_at DESC`
- [ ] 验证手段: 对比测试 `Search_Meili_vs_Pg_SortOrder` 前 20 条结果顺序一致
- [ ] 关联任务: Task 1.2.10

#### S12 [中]: LikeEscapeExtensions 跨项目引用矛盾
- [ ] 移动 `LikeEscapeExtensions` 到 `SakuraFilter.Core/Extensions/`(让 Api 与 Search 都能引用)
- [ ] 验证手段: `dotnet build` SakuraFilter.Search 项目成功引用

#### S13 [高]: cursor 分页无过期时间,可绕过分页深度限制
- [ ] `CursorHmac.Sign` 加 24h TTL:`v2:{expUnixTs}|{updatedAtIso}|{mr1B64Url}|{sig16}`
- [ ] `VerifyAndExtract` 加版本前缀检查 + TTL 校验
- [ ] 新增错误码 `CURSOR_INVALID`(400) / `CURSOR_EXPIRED`(400)
- [ ] 过渡期 7 天支持旧 cursor(无 v2: 前缀)
- [ ] cursor 模式禁用 page 参数
- [ ] 验证手段: 单元测试 `Cursor_Expired_24h` + `Cursor_LegacyCompat_7days` 通过
- [ ] 关联任务: Task 4.6.4/4.6.5

#### S14 [中]: 嵌套数组多字段组合 filter 语义未明确
- [ ] spec 明确:"单字段 OR 语义;多字段 AND 组合同元素 AND 语义(存在一个元素同时满足所有条件)"
- [ ] 单元测试 `Search_Filter_NestedMultiField_SameElement`:构造 MR.1 下有 BOSCH(下架) + MANN(上架),筛选 `oem_brand=BOSCH AND is_published=true` 不命中
- [ ] 关联任务: Task 1.2.14

#### S15 [中]: Meilisearch 1M 嵌套文档索引大小估算缺失
- [ ] spec 补充索引大小估算(6-10 GB)
- [ ] Meilisearch 配置 `--max-indexing-memory 8192`
- [ ] 监控告警超 8GB 触发
- [ ] P2 优化考虑分索引策略
- [ ] 验证手段: 部署文档 grep `max-indexing-memory`

#### S16 [低]: spec PG SQL 误用 value 列名(实际是 brand)
- [ ] spec 修正:`WHERE b.brand = ANY(@oemBrands)`
- [ ] 验证手段: spec grep `value ` 无 brand 相关匹配

#### S17 [中]: Meilisearch 嵌套数组排序实际是 first element 非 MIN
- [ ] spec 明确:"Meilisearch 对数组字段排序取第一个元素;BuildMr1DocumentAsync 中 oem_list 数组必须先按 sort_order 升序排序后入索引"
- [ ] 新增文档级 `oem_list_sort_order_min` 字段(推荐方案)
- [ ] 验证手段: 单元测试 `Meili_OemListSortOrderMin_FirstElementEqualsMin` 通过

#### S18 [高]: MeiliSearchProvider.IndexAsync 仍用 primaryKey: "id"
- [ ] 改为 `primaryKey: "mr_1"`
- [ ] 验证手段: 代码 grep `primaryKey` 命中 `"mr_1"`
- [ ] 关联任务: Task 0.4.14.2

#### S19 [高]: MeiliSearchProvider.DeleteAsync 签名仍是 IEnumerable<long>
- [ ] `ISearchProvider.cs:26` 接口签名改为 `Task DeleteAsync(IEnumerable<string> mr1s, CancellationToken ct = default)`
- [ ] `MeiliSearchProvider.cs:132-139` 改为接收 `IEnumerable<string> mr1s`
- [ ] `ResilientSearchProvider.cs:173-178` 双写删除同步改造
- [ ] `AdminProductService.DeleteAsync` 调用方改为 `_search.DeleteAsync(new[] { product.Mr1 })`
- [ ] 验证手段: 单元测试 `Meili_Delete_ByMr1` 通过
- [ ] 关联任务: Task 0.4.12

#### S20 [低]: system_settings typoTolerance 配置项不完整
- [ ] 拆分为 3 项:`search.typo_tolerance_enabled='true'` / `search.typo_min_word_size_one_typo='3'` / `search.typo_min_word_size_two_typos='5'`
- [ ] 验证手段: `SELECT key FROM system_settings WHERE key LIKE 'search.typo%'` 返回 3 行

#### S21 [低]: Meilisearch searchableAttributes 嵌套字段路径配置实际行为未验证
- [ ] spec 补充:"部署后用 `/indexes/products/search` API 验证搜索 BOSCH 时 _formatted 只在 oem_brand 字段高亮"
- [ ] 若行为不符,改为扁平字段策略(oem_brands_str = `BOSCH|MANN|NTN`)
- [ ] 验证手段: 集成测试 `Meili_NestedSearchableAttributes_Verify` 通过

#### S22 [中]: PG 兜底 IncludeDiscontinued 与 is_published 双层过滤未对齐
- [ ] PG WHERE 补充:`EXISTS (SELECT 1 FROM cross_references x WHERE x.product_id = p.id AND x.is_published = true AND x.is_discontinued = false)`
- [ ] 验证手段: 单元测试 `Search_Fallback_Pg_IncludeDiscontinued_Filter` 通过
- [ ] 关联任务: Task 1.2.9.2

### 三、前后端联动维度衍生漏洞(20 项)

#### F1 [高]: nginx / SPA fallback 与后端 MapGet("/") 路由冲突
- [ ] `CommonEndpoints.cs:18` 的 `MapGet("/")` 改为 `MapGet("/api/info")`
- [ ] nginx `location = /` 显式 `try_files $uri /index.html =404`,不回源后端
- [ ] 验证手段: `curl -I http://localhost/` 返回 `Content-Type: text/html`(非 JSON)
- [ ] 关联任务: Task 0.7.5

#### F2 [高]: Vue client mount 清空 SSR 内容,与"渐进增强"承诺矛盾
- [ ] SSR 内容放在 `<div id="seo-content">`
- [ ] Vue 挂载点独立:`<div id="gallery-app"></div>` + `<div id="compare-app"></div>` + `<div id="inquiry-app"></div>`
- [ ] spec 明确:"Vue 挂载点必须独立于 SSR 内容容器,严禁复用同一 div"
- [ ] 验证手段: 浏览器禁用 JS → SSR 内容可见;启用 JS → Vue 画廊加载,SSR 内容不被清空
- [ ] 关联任务: Task 4.1.8

#### F3 [高]: ProblemDetailsFactory 旧 ERR_* 映射缺失,前端拦截器未兼容新格式
- [ ] spec 补充"错误码迁移矩阵"表格(旧码 → 新码)
- [ ] `ProblemDetailsFactory.cs` 添加新错误码常量,保留 ERR_* 别名
- [ ] 前端 `http.ts` 拦截器扩展为 `ERROR_CODE_MAP` 双格式兼容
- [ ] i18n 文案表补充 13 个新错误码翻译
- [ ] 验证手段: 前端单元测试 `HttpInterceptor_LegacyCompat` + `I18n_NewErrorCodes` 通过
- [ ] 关联任务: Task 0.5.5/0.5.6

#### F4 [中]: AdminProductImageService.UploadAsync 签名改造后调用点遗漏
- [ ] spec 补充"调用点改造清单":`AdminProductEndpoints.cs:186/198` 端点签名 + 调用同步改造
- [ ] `BuildKey` 保留旧重载标记 `[Obsolete]` 用于历史数据迁移
- [ ] 验证手段: `git grep "UploadAsync"` 无遗漏调用点

#### F5 [高]: CursorHmac 旧 cursor 客户端无过渡期设计
- [ ] cursor 添加版本前缀 `v2:{base64(payload)}`
- [ ] `VerifyAndExtract` 根据前缀路由到旧/新解析逻辑
- [ ] 过渡期 7 天
- [ ] 前端拦截器处理 `CURSOR_EXPIRED` 错误码自动重置到第 1 页
- [ ] 验证手段: 单元测试 `Cursor_LegacyCompat_7days` 通过
- [ ] 关联任务: Task 4.6.4.3

#### F6 [中]: RateLimit "public" 策略未实施,Googlebot 抓取可能误伤
- [ ] `ServiceCollectionExtensions.cs` 添加 "public" 策略(120/min per IP)
- [ ] 新增 "sitemap" 策略(600/min per IP)
- [ ] nginx 层 Googlebot User-Agent 白名单
- [ ] 验证手段: 压测 Googlebot 600 req/min 不触发 503
- [ ] 关联任务: Task 0.6.3

#### F7 [中]: sitemap 缓存击穿,无 SemaphoreSlim 防护
- [ ] sitemap 服务加 `SemaphoreSlim(1, 1)` + double-check
- [ ] 多实例用 PG NOTIFY/LISTEN 协调
- [ ] 验证手段: 压测 100 并发请求 sitemap,缓存重建只触发 1 次

#### F8 [中]: product-detail-client.js defer 加载顺序,Vue chunk 未保证
- [ ] `vite.config.ts` 配置 `manualChunks` 将 Vue 强制打入 `product-detail-client.js`
- [ ] 或使用 `<link rel="modulepreload">` 预加载
- [ ] 脚本加 try-catch 降级
- [ ] 验证手段: 模拟 Vue chunk 加载失败,SSR 内容仍可见,控制台有降级日志
- [ ] 关联任务: Task 4.1.9

#### F9 [中]: SPA 跳转改造清单遗漏 SearchView.vue 与 DemoView.vue
- [ ] spec 补充改造清单:`SearchView.vue:121/207` + `DemoView.vue:200`
- [ ] 抽取公共工具函数 `buildProductUrl(product)` 统一生成 SEO URL
- [ ] 全局 grep `router.push.*product/` 无遗漏
- [ ] 验证手段: 全项目 grep `router.push.*product/` 无遗留
- [ ] 关联任务: Task 4.5.3/4.5.4

#### F10 [中]: 前端 ProductImageInfo 类型缺 oemNo3/imageRole 字段
- [ ] `frontend/src/api/types.ts` 更新 `ProductImageInfo`:加 `oemNo3?: string` + `imageRole?: string` + `namingField?: string`
- [ ] 过渡期字段可选
- [ ] 画廊组件兼容两种格式:`const role = img.imageRole ?? (img.slot === 1 ? 'primary' : 'detail')`
- [ ] 验证手段: `npm run typecheck` 通过
- [ ] 关联任务: Task 4.8

#### F11 [中]: 图片端点 [Authorize] 缺失,依赖 DevTokenAuthMiddleware 前缀匹配
- [ ] `AdminProductEndpoints.cs` 图片端点加 `.RequireAuthorization("AdminPolicy")`
- [ ] spec 明确:"所有 /api/admin/* 端点必须同时满足:(a) 路由前缀匹配 DevTokenAuthMiddleware;(b) 添加 [Authorize] 或 .RequireAuthorization()"
- [ ] 验证手段: `curl -X POST /api/admin/products/MR1/images/primary` 无 token 返回 401

#### F12 [高]: Razor MapRazorPages 与 CommonEndpoints.MapGet("/") 路由优先级冲突
- [ ] `EndpointRouteBuilderExtensions.cs` 按顺序注册:`MapRazorPages()` → `MapControllers()` → 其他端点
- [ ] `Detail.cshtml.cs` 显式 `@page "/products/{pn1}/{pn2}/{brand}/{oem3}"`
- [ ] 验证手段: 路由测试 `/products/...` 命中 Razor Pages,非 controller
- [ ] 关联任务: Task 0.7.6

#### F13 [中]: HMAC payload 分隔符 | 与 MR.1 字符集冲突
- [ ] MR.1 CHECK 约束 `^[A-Za-z0-9]{1,10}$` 已禁止 `|`
- [ ] `CursorHmac.Sign` 对 mr1 做 Base64Url 编码后再拼接
- [ ] spec 明确:"HMAC payload 中所有字符串字段必须 Base64Url 编码"
- [ ] 验证手段: 单元测试 `Cursor_Mr1WithSpecialChar_Base64Url` 通过

#### F14 [高]: DOMPurify 依赖未安装,html-sanitizer.ts 未创建
- [ ] `frontend/package.json` 添加 `dompurify @types/dompurify`
- [ ] 创建 `frontend/src/utils/html-sanitizer.ts`(白名单 `ALLOWED_TAGS: ['mark']` + `ALLOWED_ATTR: []`)
- [ ] ESLint 规则禁止直接 v-html,必须经 sanitizeHtml
- [ ] 验证手段: 单元测试 `sanitizeHtml('<script>alert(1)</script>')` 返回空字符串
- [ ] 关联任务: Task 4.9

#### F15 [中]: E2E 基线与视觉测试用例未更新 SEO URL
- [ ] 更新 `public-product.spec.ts` / `smoke.spec.ts` / `public-search-flow.spec.ts` 访问 SEO URL
- [ ] 创建新基线 `public-product-seo.spec.ts` / `public-product-mobile.spec.ts`
- [ ] 删除旧视觉基线截图重新生成
- [ ] 验证手段: `npm run test:e2e` + `npm run test:visual` 全绿
- [ ] 关联任务: Task 4.10

#### F16 [中]: Vue 子组件 GalleryApp/CompareApp/InquiryApp 未创建,props 接口未定义
- [ ] 创建 `frontend/src/components/GalleryApp.vue` props 接口:`{ images, oemNo3, mr1 }`
- [ ] 创建 `CompareApp.vue` props 接口:`{ mr1, oemNo3, productName1 }`
- [ ] 创建 `InquiryApp.vue` props 接口:`{ mr1, oemNo3, brand }`
- [ ] `Detail.cshtml` 添加独立挂载点 + `window.__PRODUCT__` 数据
- [ ] 验证手段: 前端单元测试 `VueMount_GalleryApp` 通过
- [ ] 关联任务: Task 4.5.1/4.5.2

#### F17 [中]: AppHeader 聚合搜索跳转 SSR 详情页丢失 SPA 上下文
- [ ] `AppHeader.vue` 改用 `window.location.href`(整页跳转)
- [ ] 对比列表状态持久化到 `sessionStorage`
- [ ] 或 router `beforeEnter` 守卫重定向
- [ ] 验证手段: E2E 测试:加入对比 → 跳转详情页 → 对比列表仍保留
- [ ] 关联任务: Task 4.5.5

#### F18 [高]: Meilisearch 主键仍为 id,文档结构未嵌套化(与 S7 重叠)
- [ ] 见 S7 双索引切换策略
- [ ] 关联任务: Task 0.4.13/0.4.14

#### F19 [中]: Detail.cshtml.cs 与 PublicProductController 查询逻辑重复
- [ ] 抽取 `IProductDetailService.GetByOem3Async(string oem3)` 公共服务
- [ ] PageModel 与 Controller 都调用该服务
- [ ] 旧 `/api/products/{oem}` 端点标记 `[Obsolete]` 过渡期后删除
- [ ] 验证手段: 代码审查 `Detail.cshtml.cs` 与 `PublicProductController` 无重复查询代码
- [ ] 关联任务: Task 4.7

#### F20 [高]: ETL 重导顺序错误,TRUNCATE → IndexAsync 用旧结构导致索引重建失败
- [ ] 创建 `018_v2_legacy_data_cleanup.sql` 双表灰度方案:
  - 阶段 1 创建 `products_v2` 表导入数据
  - 阶段 2 切换读流量(应用层双写)
  - 阶段 3 删除 `products` 重命名 `products_v2`
  - 阶段 4 重建 Meilisearch 索引(新结构)
  - 图片对象清理需应用层脚本(非 SQL)
- [ ] 验证手段: 灰度期间 `products_v2` 与 `products` 数据一致性校验;MinIO 无孤儿对象
- [ ] 关联任务: Task 4.1.10

---

## 30 个 v3 补丁任务验证清单

> 对照 tasks.md "v3 修订新增任务" 章节,逐项验证任务完成

### Phase 0 v3 补丁任务验证

- [ ] **Task 0.2.8**: ProductDbContext Fluent API 配置层清理
  - [ ] 0.2.8.1: 移除 `.IsRequired()`(oem_no_normalized 允许 NULL)
  - [ ] 0.2.8.2: `IsUnique()` 改为 `HasFilter`
  - [ ] 0.2.8.3: 两个部分唯一索引配置
  - [ ] 0.2.8.4: `HasMaxLength(200)`
  - [ ] 0.2.8.5: `HasDefaultValueSql("now()")`
  - [ ] 0.2.8.6: `idx_products_mr_1_unique` 部分唯一索引
  - [ ] 验证: `dotnet ef migrations script --idempotent` 输出无旧约束重建

- [ ] **Task 0.2.9**: partition6_placeholder 创建方式统一
  - [ ] spec SQL 移除 CREATE TABLE
  - [ ] 仅 EF Core 迁移创建
  - [ ] 验证: `psql \d partition6_placeholder` 表存在

- [ ] **Task 0.4.12**: ISearchProvider.DeleteAsync 签名改造
  - [ ] 接口签名改 `IEnumerable<string> mr1s`
  - [ ] MeiliSearchProvider/ResilientSearchProvider 同步改造
  - [ ] AdminProductService 调用方改造
  - [ ] 验证: 单元测试 `Meili_Delete_ByMr1` 通过

- [ ] **Task 0.4.13**: Meilisearch 双索引灰度迁移脚本
  - [ ] 创建 `products_v2` 索引
  - [ ] 批量写入 V2 文档
  - [ ] 热切换 IndexName
  - [ ] 删除旧索引
  - [ ] 验证: 集成测试 `Meili_IndexMigration_ZeroDowntime` 通过

- [ ] **Task 0.4.14**: Mr1IndexDoc record 重写为嵌套结构
  - [ ] `ProductIndexDoc` 重写为 `Mr1IndexDoc`
  - [ ] `OemListItem` / `MachineListItem` record 定义
  - [ ] `IndexAsync` 改为 `primaryKey: "mr_1"`
  - [ ] 验证: 单元测试 `Meili_BuildMr1Document_FlatToNested` 通过

- [ ] **Task 0.5.5**: 前端 http.ts 拦截器双格式错误码兼容
  - [ ] `ERROR_CODE_MAP` 定义
  - [ ] 拦截器 normalized 错误码路由
  - [ ] 验证: 前端单元测试 `HttpInterceptor_LegacyCompat` 通过

- [ ] **Task 0.5.6**: i18n 文案表补充新错误码翻译
  - [ ] 13 个新错误码中英文翻译
  - [ ] 验证: 前端单元测试 `I18n_NewErrorCodes` 通过

- [ ] **Task 0.6.3**: nginx Googlebot 白名单 + sitemap 单独 RateLimit
  - [ ] Googlebot User-Agent 白名单
  - [ ] `/sitemap.xml` 单独 RateLimit(600/min)
  - [ ] Googlebot 绕过 "public" RateLimit
  - [ ] 验证: 压测 Googlebot 600 req/min 不触发 503

- [ ] **Task 0.7.5**: CommonEndpoints 移除根路由
  - [ ] `MapGet("/")` 改为 `MapGet("/api/info")`
  - [ ] nginx `location = /` 显式 `try_files`
  - [ ] 验证: `curl -I http://localhost/` 返回 HTML

- [ ] **Task 0.7.6**: 路由注册顺序
  - [ ] MapRazorPages → MapControllers → 其他
  - [ ] `Detail.cshtml.cs` `@page` 指令
  - [ ] 验证: 路由测试 `/products/...` 命中 Razor Pages

### Phase 1 v3 补丁任务验证

- [ ] **Task 1.2.8**: PostgresSearchProvider 手动 _formatted 高亮 + _rankingScore
  - [ ] `BuildFormatted` 方法实现
  - [ ] `_rankingScore` 固定 0.5
  - [ ] 前端 v-html 兜底回退
  - [ ] 验证: 单元测试 `Search_Fallback_Pg_FormattedHighlight` 通过

- [ ] **Task 1.2.9**: PG WHERE 补全 6 字段 + EXISTS 子查询
  - [ ] WHERE 子句补全
  - [ ] `includeDiscontinued=false` 时 EXISTS 子查询
  - [ ] 验证: 对比测试 `Search_Meili_vs_Pg_Recall` 召回数差异 < 5%

- [ ] **Task 1.2.10**: PG ORDER BY 三层对齐 Meilisearch + CTE 预计算
  - [ ] CTE 预计算 `brand_sort_order_min` + `oem_list_sort_order_min`
  - [ ] ORDER BY 三层
  - [ ] 验证: 对比测试 `Search_Meili_vs_Pg_SortOrder` 前 20 条结果顺序一致

- [ ] **Task 1.2.11**: PG LATERAL 内 LIMIT 50 + 移除 DISTINCT
  - [ ] LATERAL 内 `LIMIT 50`
  - [ ] 移除 `json_agg(DISTINCT ...)`
  - [ ] 验证: `EXPLAIN ANALYZE` 单条查询 < 100ms(1M 数据量)

- [ ] **Task 1.2.12**: Meilisearch typoTolerance/separatorTokens/stopWords 配置调整
  - [ ] `minWordSizeForTypos` 改为 `{oneTypo: 3, twoTypos: 5}`
  - [ ] `separatorTokens` 改为 `[" ", "/", ",", "."]`
  - [ ] `nonSeparatorTokens: ["-"]`
  - [ ] `stopWords` 改为 `["the", "a", "an"]`
  - [ ] 验证: 单元测试 `Search_Aggregate_TypoTolerance_3LetterBrand` + `Search_Aggregate_OemNo3_Hyphen_Precise` + `Search_Aggregate_StopWords_OfInModel` 通过

- [ ] **Task 1.2.13**: _formatted XSS 占位符替换法
  - [ ] 占位符替换逻辑实现
  - [ ] 验证: 单元测试 `Search_Aggregate_XssDefense_LiteralMarkTag` 通过

- [ ] **Task 1.2.14**: 嵌套字段 filter 多字段组合语义明确
  - [ ] spec 语义明确
  - [ ] 单元测试 `Search_Filter_NestedMultiField_SameElement` 通过

- [ ] **Task 1.2.15**: oemList 响应层过滤 isPublished
  - [ ] `MeiliSearchProvider.SearchAsync` 返回前过滤 oemList
  - [ ] 验证: 单元测试 `Search_Aggregate_OemList_FilterUnpublished` 通过

### Phase 3 v3 补丁任务验证

- [ ] **Task 3.2.9**: product_images 新增 naming_field 字段
  - [ ] 表新增 `naming_field varchar(20)` 字段
  - [ ] EF Core 配置 + 迁移
  - [ ] `BuildKeyAsync` 写入时记录 naming_field
  - [ ] 前端查询 DB 拿 key
  - [ ] 验证: 集成测试:切换配置后,旧 OEM 3 详情页图片仍可显示

### Phase 4 v3 补丁任务验证

- [ ] **Task 4.1.8**: Detail.cshtml 挂载点分离
  - [ ] SSR 内容放在 `<div id="seo-content">`
  - [ ] Vue 挂载点独立
  - [ ] 验证: 浏览器禁用 JS → SSR 内容可见;启用 JS → Vue 画廊加载,SSR 内容不被清空

- [ ] **Task 4.1.9**: product-detail-client.js try-catch 降级 + modulepreload
  - [ ] `vite.config.ts` 配置 `manualChunks`
  - [ ] `<link rel="modulepreload">` 预加载
  - [ ] 脚本加 try-catch
  - [ ] 验证: 模拟 Vue chunk 加载失败,SSR 内容仍可见

- [ ] **Task 4.1.10**: 018_v2_legacy_data_cleanup.sql 双表灰度方案
  - [ ] 阶段 1 创建 `products_v2` 表
  - [ ] 阶段 2 ETL 导入新数据
  - [ ] 阶段 3 切换读流量
  - [ ] 阶段 4 删除旧表 + 重命名
  - [ ] 阶段 5 重建 Meilisearch 索引
  - [ ] 图片对象清理应用层脚本
  - [ ] 验证: 灰度期间数据一致性;MinIO 无孤儿对象

- [ ] **Task 4.5.1**: 创建 GalleryApp.vue + props 接口
  - [ ] `frontend/src/components/GalleryApp.vue` 创建
  - [ ] props 接口 `{ images, oemNo3, mr1 }`
  - [ ] 验证: 前端单元测试 `VueMount_GalleryApp` 通过

- [ ] **Task 4.5.2**: 创建 CompareApp.vue + InquiryApp.vue
  - [ ] `CompareApp.vue` props 接口
  - [ ] `InquiryApp.vue` props 接口
  - [ ] 验证: 前端单元测试 `VueMount_CompareInquiry` 通过

- [ ] **Task 4.5.3**: 抽取 buildProductUrl 工具函数
  - [ ] `frontend/src/utils/product-url.ts` 创建
  - [ ] slugify 函数实现
  - [ ] 验证: 单元测试 `BuildProductUrl_Slugify` 通过

- [ ] **Task 4.5.4**: 全局替换 router.push('/product/...') 为 window.location.href
  - [ ] 全项目 grep `router.push.*product/` 无遗留
  - [ ] 涉及文件全部改造:SearchView.vue / DemoView.vue / PublicSearchView.vue / PublicCompareView.vue / PublicProductView.vue / AppHeader.vue
  - [ ] 验证: 全项目 grep 无遗留

- [ ] **Task 4.5.5**: 对比列表状态 sessionStorage 持久化
  - [ ] 对比列表 store 写入 sessionStorage
  - [ ] 详情页 mount 时恢复
  - [ ] 验证: E2E 测试:加入对比 → 跳转详情页 → 对比列表仍保留

- [ ] **Task 4.6.4**: CursorHmac 加版本前缀 + 24h TTL
  - [ ] `Sign` 方法改造
  - [ ] `VerifyAndExtract` 加版本前缀检查 + TTL 校验
  - [ ] 过渡期 7 天支持旧 cursor
  - [ ] 验证: 单元测试 `Cursor_Expired_24h` + `Cursor_LegacyCompat_7days` 通过

- [ ] **Task 4.6.5**: 新增错误码 CURSOR_INVALID / CURSOR_EXPIRED
  - [ ] ProblemDetailsFactory 加错误码
  - [ ] 前端拦截器处理 `CURSOR_EXPIRED` 自动重置到第 1 页
  - [ ] 验证: 单元测试 `Cursor_ErrorCodes` 通过

- [ ] **Task 4.7**: 抽取 IProductDetailService 公共服务
  - [ ] `IProductDetailService.cs` 接口定义
  - [ ] `ProductDetailService.GetByOem3Async` 实现
  - [ ] `Detail.cshtml.cs` PageModel 注入
  - [ ] `PublicProductController` 注入
  - [ ] 旧 `/api/products/{oem}` 端点标记 `[Obsolete]`
  - [ ] 验证: 代码审查无重复查询代码

- [ ] **Task 4.8**: 前端 types.ts ProductImageInfo 字段同步
  - [ ] `ProductImageInfo` 加 `oemNo3?: string` + `imageRole?: string` + `namingField?: string`
  - [ ] 画廊组件兼容两种格式
  - [ ] 验证: `npm run typecheck` 通过

- [ ] **Task 4.9**: 创建 html-sanitizer.ts + 安装 dompurify 依赖
  - [ ] `npm install dompurify @types/dompurify`
  - [ ] `frontend/src/utils/html-sanitizer.ts` 创建
  - [ ] ESLint 规则禁止直接 v-html
  - [ ] 验证: 单元测试 `sanitizeHtml('<script>alert(1)</script>')` 返回空字符串

- [ ] **Task 4.10**: 更新 E2E 测试 URL + 创建 SEO 基线
  - [ ] 更新 `public-product.spec.ts` / `smoke.spec.ts` / `public-search-flow.spec.ts` 访问 SEO URL
  - [ ] 创建新基线 `public-product-seo.spec.ts` + `public-product-mobile.spec.ts`
  - [ ] 删除旧视觉基线截图重新生成
  - [ ] 验证: `npm run test:e2e` + `npm run test:visual` 全绿

### Phase 5 v3 补丁任务验证

- [ ] **Task 5.1.7**: ETL COPY 列定义排除 xmin 系统列
  - [ ] COPY products_stage 和 cross_references_stage 列清单排除 xmin
  - [ ] 验证: ETL 全量导入 100 万行,无 xmin 相关报错

- [ ] **Task 5.1.8**: ETL ON CONFLICT 改造
  - [ ] `EtlImportService.cs:976` 改为 `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL DO NOTHING`
  - [ ] `EtlImportService.cs:993` 改为 `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL DO UPDATE SET ...`
  - [ ] `EtlImportService.cs:1470` 改为 `ON CONFLICT (oem_brand, oem_no_3) WHERE is_discontinued = false DO NOTHING`
  - [ ] `EtlImportService.cs:1478` 改为 `ON CONFLICT (oem_brand, oem_no_3) WHERE is_discontinued = false DO UPDATE SET ...`
  - [ ] 验证: 单元测试 `Etl_Upsert_Mr1_OnConflict` + `Etl_Upsert_Xref_OnConflict` 通过

- [ ] **Task 5.1.9**: AdminProductService 保存 xrefs 后反向更新 products.oem_2
  - [ ] `CreateAsync/UpdateAsync` 保存 xrefs 后反向更新 `products.oem_2` 为第一个 xref.oem_2
  - [ ] 验证: 集成测试 `AdminProduct_UpdateXref_Oem2Backfill` 通过

---

## 第三轮深度审查验证点(待启动)

> 启动 3 个并行子代理(数据/检索/联动)对 v3 修复后再次深度审查
> 验证目标: v3 修复是否产生衍生问题、是否有遗漏场景
> 循环终止条件: 连续一轮审查无任何新漏洞检出

### 第三轮审查重点(v3 修复方案的衍生风险)

#### 数据关联维度第三轮审查
- [ ] 占位符替换法 `\u0001MARK_OPEN\u0001` 是否会被用户录入的字面量绕过(用户录入了 `\u0001MARK_OPEN\u0001` 本身)
- [ ] product_images 新增 naming_field 字段后,旧产品(无 naming_field)的前端展示兜底
- [ ] partition6_placeholder 仅 EF Core 迁移创建后,ModelSnapshot 是否会因显式 ToTable 误进查询
- [ ] ETL ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL 的 NULL mr_1 数据行为(应进入 skipped 计数)
- [ ] AdminProductService 反向更新 products.oem_2 与 ETL 全量导入的写顺序冲突
- [ ] cross_references.xmin 在 ETL ON CONFLICT DO UPDATE 路径下的行为(PG 是否刷新 xmin)
- [ ] oem_no_normalized = mr_1 派生规则在大小写敏感排序(collation)下的边界

#### 检索逻辑维度第三轮审查
- [ ] 占位符替换法在多字段高亮(_formatted.productName1 + _formatted.oemList[].oemBrand)下是否一致
- [ ] CTE 预计算 brand_sort_order_min 在 OEM 3 全部下架(MIN 返回 NULL)时 ORDER BY 行为
- [ ] Meilisearch 双索引灰度切换瞬间,IndexAsync 写入的目标索引是否一致(避免写入旧索引)
- [ ] oem_list_sort_order_min 冗余字段在 sort_order 全部为 0 时排序稳定性
- [ ] PG 兜底 BuildFormatted 的 Regex.Escape 对正则元字符(如 `*+?()[]`)的处理
- [ ] cursor 版本前缀 v2: 与未来 v3: 的兼容性设计(是否需要 v 字段而非前缀)
- [ ] nonSeparatorTokens: ["-"] 与 separatorTokens 同时配置时的优先级
- [ ] searchableAttributes 嵌套字段路径若行为不符,扁平字段策略(oem_brands_str)的 fallback 落地方案

#### 前后端联动维度第三轮审查
- [ ] Vue 挂载点分离后,画廊组件的图片懒加载与 SSR `<img>` 是否重复请求
- [ ] product-detail-client.js manualChunks 后,首屏 JS 体积是否超阈值(影响 SEO LCP)
- [ ] buildProductUrl 的 slugify 对中文/特殊字符的处理(外贸场景多语言)
- [ ] sessionStorage 持久化对比列表的容量上限(>10 项时的降级策略)
- [ ] CursorHmac 过渡期 7 天后,旧 cursor 客户端的强制升级提示
- [ ] ProblemDetailsFactory 双格式错误码兼容的移除时机(技术债清理)
- [ ] nginx Googlebot 白名单的 User-Agent 伪造风险(是否需要反查 IP)
- [ ] 018_v2_legacy_data_cleanup.sql 双表灰度期间,应用层双写的失败补偿机制

### 持续迭代验证点(每轮审查后追加)

> 每完成一轮审查 + 修复后,在此追加下一轮验证点
> 循环终止条件: 连续一轮审查无任何新漏洞检出

> 第三轮深度审查已完成,发现 70 个衍生漏洞,v4 修订已系统性修复
> 验证清单见下方"v4 修订 70 项衍生漏洞修复验证清单"
> 第四轮深度审查验证点见下方"第四轮深度审查验证点(v4 修复衍生风险)"章节

---

## v4 修订 70 项衍生漏洞修复验证清单

> 对照 spec.md 末尾"第三轮深度审查衍生漏洞修复清单(v4 修订)"第一/二/三节,逐项验证修复落地
> 高危 29 / 中危 41 / 低危 12,合计 82 项,去重后 70 项
> 每个漏洞含: 漏洞描述、修复方案、验证手段、验证状态

### 一、数据关联维度衍生漏洞修复验证(31 项 → 高危 11 + 中危 16 + 低危 4)

#### D3-1: AdminProductService 仍用 NormalizeOem 派生 oem_no_normalized [高]
- [ ] `AdminProductService.cs:43` 移除 `NormalizeOem(form.Oem2)` 派生
- [ ] `AdminProductService.cs:1038-1044` 删除 `NormalizeOem` 方法
- [ ] oem_no_normalized 派生改为 `Mr1` 原值
- [ ] 验证: 单元测试 `AdminProduct_Create_OemNoNormalizedEqualsMr1` 通过

#### D3-2: CrossReference 实体缺失 V2 字段 [高]
- [ ] `Product.cs:122-131` CrossReference 加 `SortOrder`/`MachineType`/`IsPublished`/`Oem2`/`RowVersion`(uint)5 个属性
- [ ] `ProductDbContext.cs:108-117` 加 IsRowVersion + UNIQUE 部分索引 `uq_xrefs_brand_oem3` + sort_order 索引
- [ ] `AdminProductService.cs:100-108/247-254` 写 CrossReference 时补全 4 个 V2 字段
- [ ] 验证: 单元测试 `Xref_Create_AllV2Fields_Persisted` 通过

#### D3-3: ValidateForm 无 MR.1 校验 [高]
- [ ] `AdminProductService.cs:1008-1036` 加 MR.1 必填校验 + 格式校验 `^[A-Za-z0-9]{1,10}$`
- [ ] 长度上限改为 10(原 7)
- [ ] 控制字符过滤
- [ ] 验证: 单元测试 `Mr1_ValidateFormat_10Char` + `Mr1_ValidateFormat_ControlChar` 通过

#### D3-4: UpdateAsync 未同步 OemNoNormalized [高]
- [ ] `AdminProductService.cs:184-185` UpdateAsync 同步更新 `OemNoNormalized = Mr1`
- [ ] 验证: 单元测试 `AdminProduct_Update_OemNoNormalizedSynced` 通过

#### D3-5: 唯一性检查用旧字段 [高]
- [ ] `AdminProductService.cs:57-59` 唯一性检查改用 `Mr1`(原 `OemNoNormalized`)
- [ ] 验证: 单元测试 `Mr1_Create_Duplicate_DetectByMr1` 通过

#### D3-6: ETL COPY 不含 mr_1 [高]
- [ ] `EtlImportService.cs:832-879` products COPY 列清单加 `mr_1`
- [ ] JSONL 解析加 `mr_1` 字段(必填 + 格式校验)
- [ ] `EtlImportService.cs:1832-1845` products_stage 表定义加 `mr_1`/`oem_2`/`d4_mm`/`h4_mm`/`d*_raw`/`h*_raw` 字段
- [ ] 验证: 集成测试 ETL 导入 V2 mock 数据,products 表 mr_1 列有值

#### D3-7: ETL ON CONFLICT 用旧字段 [高]
- [ ] `EtlImportService.cs:945-992` INSERT INTO products 列清单加 `mr_1`
- [ ] `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL DO NOTHING/UPDATE`
- [ ] `ProductDbContext.cs:86` 移除 `OemNoNormalized` 的 `IsUnique()`
- [ ] 验证: 集成测试 ETL 导入重复 mr_1,无 42P10 错误

#### D3-8: LoadExistingOemMapAsync 查旧字段 [高]
- [ ] `EtlImportService.cs:1212-1218` `LoadExistingOemMapAsync` 改为查 `mr_1`
- [ ] JSONL 字段名 `product_oem` → `mr_1`
- [ ] 验证: 集成测试 ETL 二次导入,update 路径正确命中

#### D3-9: xrefs_stage 缺 4 列 [高]
- [ ] `EtlImportService.cs:1398-1480` xrefs_stage + COPY + INSERT 加 `sort_order`/`machine_type`/`is_published`/`oem_2` 字段
- [ ] 验证: 集成测试 ETL 导入 xrefs,4 个 V2 字段正确入库

#### D3-10: cascade 语义问题 [高]
- [ ] `EtlImportService.cs:935-937` cascade 语义重新定义: `cascade=false` 时显式 TRUNCATE products + product_images
- [ ] 验证: 集成测试 ETL cascade=false,旧数据被清空 + 新数据入库

#### D3-11: DROP TABLE products CASCADE [高]
- [ ] `018_v2_legacy_data_cleanup.sql` 阶段 4 改为: DROP CONSTRAINT → DROP TABLE → RENAME → ADD CONSTRAINT
- [ ] 验证: 执行脚本无 2BP01 错误,子表外键完整

#### D3-12: MR.1 控制字符注入 [中危]
- [ ] `AdminProductService.ValidateForm` 加控制字符过滤
- [ ] `EtlImportService.GetStringOrNull` 加控制字符过滤(允许 `\t` `\n` `\r`)
- [ ] 验证: 单元测试 `Mr1_ValidateFormat_ControlChar` 通过

#### D3-13: Meilisearch _formatted XSS(占位符法未解决 mark 字面量) [中危]
- [ ] Meilisearch 高亮标签专属化为 `\u0001MO\u0001`/`\u0001MC\u0001`
- [ ] 后端只还原专属标签,不还原用户字面量 `<mark>`
- [ ] 递归 `SanitizeFormatted(JToken)` 处理嵌套对象/数组
- [ ] 验证: 单元测试 `Search_Aggregate_XssDefense_LiteralMarkTag` 通过

#### D3-14: AdminProductService 未反向更新 products.oem_2 [中危]
- [ ] `AdminProductService.cs:114-115` CreateAsync 保存 xrefs 后反向更新 `products.oem_2`
- [ ] 按 sort_order 排序后取第一个 xref.oem_2,空列表置 NULL
- [ ] 验证: 单元测试 `AdminProduct_Create_Oem2Backfill` 通过

#### D3-15: UpdatedAt/CreatedAt C# 默认值导致 xmin 失效 [中危]
- [ ] `Product.cs:195/77` 移除 UpdatedAt/CreatedAt 的 C# 默认值 `DateTime.UtcNow`
- [ ] DbContext 加 `HasDefaultValueSql("now()")`
- [ ] 验证: 单元测试 `Product_Create_DefaultTimestamp` 通过(由 DB 设置)

#### D3-16: Mr1 普通索引未改 UNIQUE 部分索引 [中危]
- [ ] `ProductDbContext.cs:104` `e.HasIndex(p => p.Mr1)` 替换为 UNIQUE 部分索引 `idx_products_mr_1_unique`
- [ ] 验证: `psql \d products` 显示 `idx_products_mr_1_unique` UNIQUE 索引

#### D3-17: Partition6Placeholder 未在 ProductDbContext 注册 [中危]
- [ ] 新增 `Partition6Placeholder.cs` 实体
- [ ] `ProductDbContext.OnModelCreating` 注册 `ToTable("partition6_placeholder")`
- [ ] 验证: `dotnet ef migrations script` 输出含 `partition6_placeholder` 表

#### D3-18: ETL xrefs INSERT 23505 错误(下架后重新上架) [中危]
- [ ] `EtlImportService.cs:1457-1480` xrefs INSERT 前 DELETE 旧下架行
- [ ] 验证: 集成测试 ETL 二次导入下架 OEM 3,无 23505 错误

#### D3-19: products_stage 缺 V2 字段(d4_mm/h4_mm/d*_raw/h*_raw) [中危]
- [ ] `EtlImportService.cs:1832-1845` products_stage 表定义加全部 V2 字段
- [ ] 精度改 `NUMERIC(10,2)`(原 NUMERIC(8,2))
- [ ] 验证: 集成测试 ETL 导入 d4/h4 字段,正确入库

#### D3-20: RowVersion 类型 ulong? 与 xmin 不一致 [中危]
- [ ] spec L521 "ulong?" 改为 "uint"
- [ ] CrossReference xmin 配置统一为 uint
- [ ] 验证: `dotnet build` 无类型不匹配 warning

#### D3-21: UpdateAsync xref 全量替换绕过 xmin [中危]
- [ ] `AdminProductService.cs:243-254` UpdateAsync xref 改为增量更新(新增/更新/删除三类)
- [ ] 更新类触发 xmin 乐观锁
- [ ] 验证: 单元测试 `Xref_Update_XminConflict` 通过

#### D3-22: is_discontinued NOT NULL/DEFAULT 缺失 [中危]
- [ ] spec L489 后补 `ALTER TABLE cross_references ALTER COLUMN is_discontinued SET NOT NULL/DEFAULT false`
- [ ] spec L316-318 "主图被删除后再次上传"边界: 重新上架 UPDATE 旧下架行,不 INSERT 新行
- [ ] 验证: `psql \d cross_references` 显示 is_discontinued NOT NULL DEFAULT false

#### D3-23: CleanupOrphanImagesAsync 缺失 [中危]
- [ ] `CleanupOrphanImagesAsync` 应用层脚本实现
- [ ] TRUNCATE product_images 后扫描 OSS 清理孤儿文件
- [ ] 验证: 集成测试 TRUNCATE 后无孤儿文件

#### D3-24: F20 双表灰度 FK 失效 [中危]
- [ ] `018_v2_legacy_data_cleanup.sql` 阶段 4 分阶段 DROP/ADD CONSTRAINT
- [ ] 验证: 灰度期间 cross_references.product_id 始终有有效 FK

#### D3-25: NUMERIC(8,2) 精度不足 [中危]
- [ ] `EtlImportService.cs:1837-1838` 精度改 `NUMERIC(10,2)`
- [ ] 验证: 集成测试导入大尺寸(如 99999999.99)成功

#### D3-26: 产品变更历史 V2 字段缺失 [中危]
- [ ] 产品变更历史表(product_change_logs)加 V2 字段(mr_1/sort_order/machine_type/is_published/oem_2)
- [ ] 验证: 单元测试 `ProductChangeLog_V2Fields` 通过

#### D3-27: GetStringOrNull 未过滤控制字符 [中危]
- [ ] `EtlImportService.cs:1850-1851` `GetStringOrNull` 加控制字符过滤
- [ ] 允许 `\t` `\n` `\r`(Excel 多行文本)
- [ ] 验证: 单元测试 `Etl_GetStringOrNull_ControlChar` 通过

#### D3-28: ProductDbContext 旧 OemBrand+OemNo3 索引未替换 [低危]
- [ ] `ProductDbContext.cs:116` `e.HasIndex(x => new { x.OemBrand, x.OemNo3 })` 替换为 `idx_xrefs_brand_oem3_sort`
- [ ] 验证: `psql \d cross_references` 显示新索引

#### D3-29: DROP INDEX 索引名与实际数据库不一致 [低危]
- [ ] spec v3 D4 修复补充: DROP CONSTRAINT 前先查询实际外键名
- [ ] 验证: 执行迁移脚本无 "index does not exist" 错误

#### D3-30: naming_field 字段语义模糊 [低危]
- [ ] spec v3 D16 修复调整: `naming_field` 字段语义改为"命名快照值"(审计/追溯)
- [ ] 前端查 `image_key` 不动态生成
- [ ] 验证: 单元测试 `Image_NamingField_Snapshot` 通过

#### D3-31: EtlAlertService 排除条件 [低危]
- [ ] EtlAlertService 显式排除 cancelled 记录
- [ ] 验证: 单元测试 `EtlAlert_ExcludeCancelled` 通过

### 二、检索逻辑维度衍生漏洞修复验证(28 项 → 高危 10 + 中危 14 + 低危 4)

#### S3-1: 占位符法未解决 mark 字面量 XSS [高]
- [ ] Meilisearch 高亮标签专属化为 `\u0001MO\u0001`/`\u0001MC\u0001`
- [ ] 后端只还原专属标签
- [ ] 递归 `SanitizeFormatted`
- [ ] 验证: 单元测试 `Search_Aggregate_XssDefense_LiteralMarkTag` 通过

#### S3-2: CTE 未过滤 is_published [高]
- [ ] `BuildMr1DocumentAsync` 过滤 `is_published=true` 的 OEM 3
- [ ] 预计算 `OemListPublishedBrands`/`OemListPublishedNo3s` 字段
- [ ] 验证: 单元测试 `Meili_BuildMr1Document_FilterUnpublished` 通过

#### S3-3: PG 单一 ILIKE 与 Meilisearch 分词不一致 [高]
- [ ] PG 兜底分词 OR 拼接(`req.Q.Split` 拆 token + `EscapeLikePattern` + 参数化)
- [ ] 验证: 对比测试 `Search_Meili_vs_Pg_Recall` 召回数差异 < 5%

#### S3-4: lat_machine LATERAL 缺失 [高]
- [ ] PG 兜底 `lat_machine` LATERAL 子查询完整实现
- [ ] 过滤 `is_discontinued=false` + LIMIT 50
- [ ] 验证: `EXPLAIN ANALYZE` 单条查询 < 100ms

#### S3-5: PG 排序第 3 字段不一致 [高]
- [ ] PG 兜底 `ORDER BY` 第 3 字段改为相关性评分(`CASE WHEN ... THEN 100 ...`)
- [ ] keyset 分页
- [ ] 验证: 对比测试 `Search_Meili_vs_Pg_SortOrder` 前 20 条结果顺序一致

#### S3-6: 双索引阶段 3 矛盾 [高]
- [ ] Meilisearch 双索引灰度改为 5 阶段(双写 + 读切换 + 停双写)
- [ ] `IOptionsMonitor<MeiliSearchOptions>` 热切换
- [ ] `DeleteAsync` 双索引同步
- [ ] 验证: 集成测试 `Meili_DualIndex_GrayRelease` 通过

#### S3-7: 嵌套数组 AND filter 语义 [高]
- [ ] `Mr1IndexDoc` record 新增扁平化冗余字段(`OemListPublishedBrands`/`OemListPublishedNo3s`/`OemBrandsStr`/`OemNo3sStr`)
- [ ] `filterableAttributes` 补充扁平化字段
- [ ] 验证: 单元测试 `Search_Filter_NestedMultiField_FlattenFallback` 通过

#### S3-8: 软删除 brand 仍可搜索 [高]
- [ ] `BuildMr1DocumentAsync` 过滤软删除 brand(`b.deleted_at IS NULL`)
- [ ] 验证: 单元测试 `Meili_BuildMr1Document_FilterSoftDeletedBrand` 通过

#### S3-9: cursor 双 key 未合并 [高]
- [ ] `CursorHmac.cs` 验签顺序调整(先 HMAC 后 TTL)
- [ ] 统一 Base64Url 编码
- [ ] 旧 cursor 过渡期分支(`LEGACY_CUTOFF_TS`)
- [ ] 双 key 验签
- [ ] `pageNum` 字段
- [ ] 验证: 单元测试 `Cursor_DualKey_Rotation` 通过

#### S3-10: CTE 未过滤 deleted_at [高]
- [ ] PG 兜底 CTE 加 `deleted_at IS NULL` 过滤
- [ ] 验证: 单元测试 `Search_PG_CTE_FilterSoftDeleted` 通过

#### S3-11: Brand sort_order 变更后台重建缺失 [中危]
- [ ] `IHostedService` + `Channel<string>` 队列
- [ ] `IMemoryCache` 5 秒去重
- [ ] `search_index_pending` 表持久化兜底
- [ ] 验证: 单元测试 `BrandSortOrder_Change_ReindexDebounce` 通过

#### S3-12: Meilisearch filter 注入防御不足 [中危]
- [ ] Meilisearch filter 注入防御改为移除 `"` 和 `\` 策略
- [ ] 嵌套字段 filter 单元测试
- [ ] 验证: 单元测试 `Search_Filter_InjectionDefense` 通过

#### S3-13: cursor 过渡期未明确截止 [中危]
- [ ] `LEGACY_CUTOFF_TS` 常量(过渡期截止时间戳)
- [ ] 过期后旧 cursor 拒绝
- [ ] 验证: 单元测试 `Cursor_Legacy_AfterCutoff_Rejected` 通过

#### S3-14: cursor pageNum 字段缺失 [中危]
- [ ] `SignV2` 加 `pageNum` 字段
- [ ] `VerifyAndExtractV2` 校验 `pageNum > 1000` 拒绝
- [ ] 验证: 单元测试 `Cursor_PageNum_TooDeep` 通过

#### S3-15: PG keyset 分页缺失 [中危]
- [ ] PG 兜底 keyset 分页实现
- [ ] 验证: 单元测试 `Search_PG_Keyset_Pagination` 通过

#### S3-16: trgm GIN 索引未覆盖 5 字段 [中危]
- [ ] trgm GIN 索引补充 5 个(`idx_xrefs_oem_no_3_trgm` / `idx_xrefs_oem_brand_trgm` / `idx_products_pn1_trgm` / `idx_products_pn2_trgm` / `idx_products_oem_2_trgm`)
- [ ] `pg_trgm` extension 确认
- [ ] 验证: `psql \di` 显示 5 个新索引

#### S3-17: 双索引 DeleteAsync 仅删一个 [中危]
- [ ] `DeleteAsync` 双索引同步删除
- [ ] 验证: 单元测试 `Meili_DualIndex_DeleteSync` 通过

#### S3-18: IOptionsMonitor 热切换缺失 [中危]
- [ ] `MeiliSearchProvider` 注入 `IOptionsMonitor<MeiliSearchOptions>`
- [ ] `OnChange` 监听配置变化
- [ ] 验证: 集成测试 `Meili_OptionsMonitor_HotSwitch` 通过

#### S3-19: stopWords 配置 "a" 误伤 [中危]
- [ ] typoTolerance stopWords 移除 "a"
- [ ] 验证: 单元测试 `Search_StopWords_NoA` 通过

#### S3-20: nonSeparatorTokens ["-"] 副作用 [中危]
- [ ] separatorTokens 不加 `nonSeparatorTokens: ["-"]`
- [ ] 验证: 单元测试 `Search_OemNo3_Hyphen_Precise` 通过

#### S3-21: 嵌套字段 filter 性能问题 [中危]
- [ ] 扁平化冗余字段替代嵌套字段 filter
- [ ] `filterableAttributes` 优先用扁平字段
- [ ] 验证: 性能测试 1M 数据量 P95 < 200ms

#### S3-22: Brand sort_order 变更无兜底 [中危]
- [ ] `search_index_pending` 表持久化兜底
- [ ] 应用重启后从 pending 表恢复
- [ ] 验证: 集成测试 `BrandSortOrder_Change_AppRestart_Recover` 通过

#### S3-23: 嵌套字段 filter 单元测试缺失 [中危]
- [ ] 嵌套字段 filter 单元测试覆盖
- [ ] 验证: 单元测试 `Search_Filter_NestedMultiField` 通过

#### S3-24: cursor 验签顺序错误 [中危]
- [ ] `VerifyAndExtractV2` 先 HMAC 验签,后 TTL 检查
- [ ] 验证: 单元测试 `Cursor_VerifyOrder_HmacBeforeTtl` 通过

#### S3-25: typoTolerance 中文场景 [低危]
- [ ] typoTolerance 中文场景禁用(`disableOnWords: ["中文关键词"]`)
- [ ] 验证: 单元测试 `Search_TypoTolerance_ChineseDisabled` 通过

#### S3-26: separatorTokens 与 nonSeparatorTokens 冲突 [低危]
- [ ] 移除 `nonSeparatorTokens: ["-"]` 配置
- [ ] 验证: 单元测试 `Search_Separator_Conflict_Resolved` 通过

#### S3-27: searchableAttributes 嵌套路径不可用 [低危]
- [ ] 扁平字段 fallback 落地
- [ ] 验证: 单元测试 `Search_Searchable_FlatFallback` 通过

#### S3-28: _formatted 嵌套对象递归处理 [低危]
- [ ] `SanitizeFormatted` 递归处理 JToken
- [ ] 验证: 单元测试 `Search_Formatted_RecursiveSanitize` 通过

### 三、前后端联动维度衍生漏洞修复验证(23 项 → 高危 8 + 中危 11 + 低危 4)

#### F2-1: window.__PRODUCT__ XSS [高]
- [ ] `Detail.cshtml` 改用 JSON 数据岛 `<script type="application/json" id="product-data">@Json.Serialize(Model.Product)</script>`
- [ ] 前端 `JSON.parse(document.getElementById('product-data').textContent)`
- [ ] 验证: 单元测试 `Razor_JsonIsland_XssDefense` 通过

#### F2-2: cursor 验签顺序错误 [高]
- [ ] `CursorHmac.VerifyAndExtractV2` 先 HMAC 后 TTL
- [ ] 验证: 单元测试 `Cursor_VerifyOrder_HmacBeforeTtl` 通过

#### F2-3: 双表灰度 FK 失效 [高]
- [ ] `018_v2_legacy_data_cleanup.sql` 阶段 4 分阶段 DROP/ADD CONSTRAINT
- [ ] 验证: 集成测试 灰度期间 FK 完整

#### F2-4: DROP TABLE CASCADE [高]
- [ ] 严格顺序 DROP CONSTRAINT → DROP TABLE → RENAME → ADD CONSTRAINT
- [ ] 验证: 执行脚本无 CASCADE 删除子表

#### F2-5: ERROR_CODE_MAP 未落地 [高]
- [ ] `frontend/src/utils/http.ts` 拦截器改造:`ERROR_CODE_I18N` 字符串映射
- [ ] V2 新码 + 旧 ERR_ 别名
- [ ] `data.errorCode` 优先
- [ ] `CURSOR_EXPIRED`/`INVALID` 自动重置
- [ ] 验证: 前端单元测试 `HttpInterceptor_ErrorCodeMap` 通过

#### F2-6: 中文 slugify 为空 [高]
- [ ] `frontend/src/utils/build-product-url.ts` 实现 `buildProductUrl(p)` 工具函数
- [ ] 中文 slugify 兜底(`Uri.EscapeDataString`)
- [ ] 验证: 单元测试 `BuildProductUrl_ChineseSlugify` 通过

#### F2-7: SearchHit 缺 V2 字段 [高]
- [ ] `frontend/src/api/types.ts` 新增 `AggregateSearchHit`(含 mr1/productName1/oemList[]/machineList[]/_formatted/_rankingScore)
- [ ] `AggregateSearchResponse` 类型
- [ ] `SearchHit` 补 `mr1`/`productName1`/`oemList` 字段
- [ ] `frontend/src/api/index.ts` 新增 `searchApi.aggregate(req)`
- [ ] `SearchView.vue` 改用新 API + 新类型
- [ ] 验证: `npm run typecheck` 通过

#### F2-8: 无 ErrorBoundary [高]
- [ ] `product-detail-client.ts` 实现 `safeMount(id, Comp, props)` ErrorBoundary
- [ ] try-catch 降级 UI
- [ ] 验证: 前端单元测试 `VueMount_ErrorBoundary` 通过

#### F2-9: script defer 阻塞 [中危]
- [ ] `<script type="module">` 替换 `<script defer>`
- [ ] `frontend/vite.config.ts` 多入口 build + `manualChunks: { vue: ['vue', 'vue-router', 'pinia'] }`
- [ ] 验证: Lighthouse SEO LCP < 2.5s

#### F2-10: cursor 无 pageNum 字段 [中危]
- [ ] `SignV2` 加 `pageNum`
- [ ] `VerifyAndExtractV2` 校验
- [ ] 验证: 单元测试 `Cursor_PageNum` 通过

#### F2-11: F20 双表灰度约束切换顺序 [中危]
- [ ] `018_v2_legacy_data_cleanup.sql` 分阶段 DROP/ADD CONSTRAINT
- [ ] 验证: 集成测试 灰度切换无约束错误

#### F2-12: router.push('/product/...') 遗漏 [中危]
- [ ] 全局 grep 替换 `router.push('/product/...')` 4 处遗漏
- [ ] 涉及文件:`SearchView.vue:121,207` / `AppHeader.vue:202` / `PublicCompareView.vue:336` / `PublicProductView.vue:59`
- [ ] 验证: 全项目 grep `router.push.*product/` 无遗留

#### F2-13: 对比列表 sessionStorage 容量 [中危]
- [ ] `PublicCompareView.vue` 对比列表 sessionStorage 仅持久化 ID 数组
- [ ] `QuotaExceededError` 降级
- [ ] 验证: 前端单元测试 `Compare_SessionStorage_QuotaExceeded` 通过

#### F2-14: Googlebot UA 白名单伪造 [中危]
- [ ] `docker/nginx.conf` Googlebot UA 白名单限定 location
- [ ] admin 路径严格 RateLimit 无视 UA
- [ ] 验证: 压测伪造 Googlebot UA 访问 admin 路径,被 RateLimit 拦截

#### F2-15: 挂载点 SSR 兜底主图缺失 [中危]
- [ ] `Detail.cshtml` 挂载点内 SSR 兜底主图
- [ ] 验证: 浏览器禁用 JS,主图仍可见

#### F2-16: 404 页面无搜索入口 [中危]
- [ ] `Detail.cshtml.cs` OnGetAsync 404 渲染友好页 + 站内搜索入口
- [ ] 验证: E2E 测试访问不存在 OEM 3,404 页有搜索框

#### F2-17: cursor 旧格式过渡期不明确 [中危]
- [ ] `LEGACY_CUTOFF_TS` 明确过渡期截止
- [ ] 双 key 验签支持轮转
- [ ] 验证: 单元测试 `Cursor_Legacy_DualKey` 通过

#### F2-18: i18n 错误码翻译缺失 [中危]
- [ ] `frontend/src/i18n/locales/zh-CN.ts` + `en-US.ts` 新增 `common.error.*` 命名空间 13 个错误码翻译
- [ ] 验证: 前端单元测试 `I18n_NewErrorCodes` 通过

#### F2-19: 双表灰度写入策略无接口 [中危]
- [ ] `IProductWriteStrategy` / `IProductReadStrategy` 接口
- [ ] 阶段 3 双写策略表
- [ ] `AdminProductService` 注入 `IProductWriteStrategy`
- [ ] `EtlImportService` 同理
- [ ] 验证: 集成测试 `ProductWriteStrategy_DualWrite` 通过

#### F2-20: 前端单元测试覆盖缺失 [低危]
- [ ] `frontend/src/utils/__tests__/` 新增单元测试
- [ ] `html-sanitizer.test.ts` / `build-product-url.test.ts` / `GalleryApp.test.ts` / `error-code-map.test.ts`
- [ ] 验证: `npm run test:unit` 全绿

#### F2-21: CURSOR 自动重置逻辑缺失 [低危]
- [ ] http.ts 拦截器 `CURSOR_EXPIRED`/`INVALID` 自动重置到第 1 页
- [ ] 验证: 前端单元测试 `HttpInterceptor_CursorAutoReset` 通过

#### F2-22: build-product-url.ts 测试覆盖 [低危]
- [ ] `build-product-url.test.ts` 覆盖中文/特殊字符/空字段
- [ ] 验证: 单元测试 `BuildProductUrl_EdgeCases` 通过

#### F2-23: 双索引回滚预案缺失 [低危]
- [ ] Meilisearch 双索引切换回滚预案: 阶段 5a/5b/5c 拆分
- [ ] 旧索引保留 7 天
- [ ] 验证: 集成测试 `Meili_DualIndex_Rollback` 通过

#### F2-24: vue-gallery 命名不一致 [低危]
- [ ] spec L1128 `vue-gallery` 命名同步更新为 `gallery-app`/`compare-app`/`inquiry-app`
- [ ] `product-detail-client.js` 示例代码同步
- [ ] 验证: 全项目 grep `vue-gallery` 无遗留

---

## 48 个 v4 补丁任务验证清单

> 对照 tasks.md "v4 补丁任务清单" 章节,逐项验证任务完成

### Phase 0 v4 补丁任务验证(11 个)

- [ ] **Task 0.2.8**: CrossReference 实体加 5 个 V2 属性
  - [ ] `SortOrder`/`MachineType`/`IsPublished`/`Oem2`/`RowVersion`(uint)
  - [ ] 验证: `dotnet build` 通过

- [ ] **Task 0.2.9**: CrossReference 配置加 IsRowVersion + UNIQUE 部分索引
  - [ ] `IsRowVersion()` + `IsConcurrencyToken()`
  - [ ] `uq_xrefs_brand_oem3` 部分唯一索引
  - [ ] sort_order 索引
  - [ ] 验证: `psql \d cross_references` 显示索引

- [ ] **Task 0.2.10**: 移除 OemNoNormalized 的 IsUnique()
  - [ ] 改为部分普通索引
  - [ ] 验证: `psql \d products` 显示普通索引

- [ ] **Task 0.2.11**: Mr1 改为 UNIQUE 部分索引
  - [ ] `idx_products_mr_1_unique`
  - [ ] 验证: `psql \d products` 显示 UNIQUE 索引

- [ ] **Task 0.2.12**: 旧 OemBrand+OemNo3 索引替换
  - [ ] 替换为 `idx_xrefs_brand_oem3_sort`
  - [ ] 验证: `psql \d cross_references` 显示新索引

- [ ] **Task 0.2.13**: Partition6Placeholder 实体 + 注册
  - [ ] `Partition6Placeholder.cs` 创建
  - [ ] `ProductDbContext` 注册
  - [ ] 验证: `dotnet ef migrations script` 含表创建

- [ ] **Task 0.2.14**: UpdatedAt/CreatedAt 默认值改造
  - [ ] 移除 C# 默认值
  - [ ] `HasDefaultValueSql("now()")`
  - [ ] 验证: 单元测试 `Product_Create_DefaultTimestamp` 通过

- [ ] **Task 0.2.15**: is_discontinued NOT NULL/DEFAULT 补充
  - [ ] `ALTER COLUMN is_discontinued SET NOT NULL/DEFAULT false`
  - [ ] 验证: `psql \d cross_references` 显示约束

- [ ] **Task 0.2.16**: 主图重新上架边界
  - [ ] UPDATE 旧下架行,不 INSERT 新行
  - [ ] 验证: 单元测试 `Image_Primary_ReuploadAfterDelete` 通过

- [ ] **Task 0.2.17**: RowVersion 类型统一为 uint
  - [ ] spec "ulong?" 改为 "uint"
  - [ ] 验证: `dotnet build` 无 warning

- [ ] **Task 0.2.18**: DROP CONSTRAINT 前查询实际外键名
  - [ ] spec v3 D4 修复补充
  - [ ] 验证: 执行迁移脚本无错误

### Phase 0 v4 补丁任务验证(AdminProductService 6 个)

- [ ] **Task 0.3.10**: 移除 NormalizeOem 方法
  - [ ] `AdminProductService.cs:43/1038-1044` 移除
  - [ ] oem_no_normalized 派生改为 mr_1 原值
  - [ ] 验证: 单元测试 `AdminProduct_Create_OemNoNormalizedEqualsMr1` 通过

- [ ] **Task 0.3.11**: 写 CrossReference 补全 V2 字段
  - [ ] `AdminProductService.cs:100-108/247-254` 补全 sort_order/machine_type/is_published/oem_2
  - [ ] 验证: 单元测试 `Xref_Create_AllV2Fields_Persisted` 通过

- [ ] **Task 0.3.12**: ValidateForm 加 MR.1 校验
  - [ ] 必填/格式校验 `^[A-Za-z0-9]{1,10}$`
  - [ ] 长度上限 10
  - [ ] 控制字符过滤
  - [ ] 验证: 单元测试 `Mr1_ValidateFormat_*` 通过

- [ ] **Task 0.3.13**: UpdateAsync 同步 OemNoNormalized
  - [ ] `OemNoNormalized = Mr1`
  - [ ] 验证: 单元测试 `AdminProduct_Update_OemNoNormalizedSynced` 通过

- [ ] **Task 0.3.14**: 唯一性检查改用 Mr1
  - [ ] `AdminProductService.cs:57-59` 改造
  - [ ] 验证: 单元测试 `Mr1_Create_Duplicate_DetectByMr1` 通过

- [ ] **Task 0.3.15**: CreateAsync 反向更新 products.oem_2
  - [ ] 按 sort_order 排序后取第一个 xref.oem_2
  - [ ] 空列表置 NULL
  - [ ] 验证: 单元测试 `AdminProduct_Create_Oem2Backfill` 通过

### Phase 0 v4 补丁任务验证(Meilisearch 6 个)

- [ ] **Task 0.4.2a**: BuildMr1DocumentAsync 过滤软删除 brand
  - [ ] `b.deleted_at IS NULL` 过滤
  - [ ] 预计算 `OemListPublishedBrands`/`OemListPublishedNo3s`/`OemBrandsStr`/`OemNo3sStr`/`OemListSortOrderMin`
  - [ ] 验证: 单元测试 `Meili_BuildMr1Document_FilterSoftDeleted` 通过

- [ ] **Task 0.4.4a**: Meilisearch filter 注入防御
  - [ ] 移除 `"` 和 `\` 策略
  - [ ] 嵌套字段 filter 单元测试
  - [ ] 验证: 单元测试 `Search_Filter_InjectionDefense` 通过

- [ ] **Task 0.4.6a**: typoTolerance 配置调整
  - [ ] stopWords 移除 "a"
  - [ ] separatorTokens 不加 `nonSeparatorTokens: ["-"]`
  - [ ] 验证: 单元测试 `Search_StopWords_NoA` + `Search_OemNo3_Hyphen_Precise` 通过

- [ ] **Task 0.4.8a**: Meilisearch 高亮标签专属化
  - [ ] `\u0001MO\u0001`/`\u0001MC\u0001` 配置
  - [ ] 后端只还原专属标签
  - [ ] 递归 `SanitizeFormatted(JToken)`
  - [ ] 验证: 单元测试 `Search_Aggregate_XssDefense_LiteralMarkTag` 通过

- [ ] **Task 0.4.13a**: Meilisearch 双索引灰度 5 阶段
  - [ ] 5 阶段(双写 + 读切换 + 停双写)
  - [ ] `IOptionsMonitor<MeiliSearchOptions>` 热切换
  - [ ] `DeleteAsync` 双索引同步
  - [ ] 验证: 集成测试 `Meili_DualIndex_GrayRelease` 通过

- [ ] **Task 0.4.14a**: Mr1IndexDoc 扁平化冗余字段
  - [ ] `OemListPublishedBrands`/`OemListPublishedNo3s`/`OemBrandsStr`/`OemNo3sStr`
  - [ ] `filterableAttributes` 补充
  - [ ] 验证: 单元测试 `Meili_FlatFields_Filter` 通过

- [ ] **Task 0.4.15**: Brand sort_order 变更后台重建
  - [ ] `IHostedService` + `Channel<string>` 队列
  - [ ] `IMemoryCache` 5 秒去重
  - [ ] `search_index_pending` 表持久化
  - [ ] 验证: 集成测试 `BrandSortOrder_Change_ReindexDebounce` 通过

### Phase 0 v4 补丁任务验证(前端 2 个)

- [ ] **Task 0.5.5**: http.ts 拦截器改造
  - [ ] `ERROR_CODE_I18N` 字符串映射
  - [ ] V2 新码 + 旧 ERR_ 别名
  - [ ] `data.errorCode` 优先
  - [ ] `CURSOR_EXPIRED`/`INVALID` 自动重置
  - [ ] 验证: 前端单元测试 `HttpInterceptor_ErrorCodeMap` 通过

- [ ] **Task 0.5.6**: i18n 错误码翻译
  - [ ] 13 个错误码中英文翻译
  - [ ] 验证: 前端单元测试 `I18n_NewErrorCodes` 通过

### Phase 1 v4 补丁任务验证(4 个)

- [ ] **Task 1.2.9a**: PG 兜底分词 OR 拼接
  - [ ] `req.Q.Split` 拆 token + `EscapeLikePattern` + 参数化
  - [ ] 验证: 对比测试 `Search_Meili_vs_Pg_Recall` 召回差异 < 5%

- [ ] **Task 1.2.10a**: PG 排序第 3 字段 + keyset 分页
  - [ ] 第 3 字段改为相关性评分
  - [ ] keyset 分页实现
  - [ ] 验证: 对比测试 `Search_Meili_vs_Pg_SortOrder` 前 20 条一致

- [ ] **Task 1.2.11a**: PG lat_machine LATERAL 实现
  - [ ] LATERAL 子查询 + `is_discontinued=false` + LIMIT 50
  - [ ] 验证: `EXPLAIN ANALYZE` 单条 < 100ms

- [ ] **Task 1.2.12**: trgm GIN 索引补充 5 个
  - [ ] 5 个 trgm 索引创建
  - [ ] `pg_trgm` extension 确认
  - [ ] 验证: `psql \di` 显示新索引

### Phase 3 v4 补丁任务验证(2 个)

- [ ] **Task 3.2.10**: UpdateAsync xref 增量更新
  - [ ] 新增/更新/删除三类
  - [ ] 更新类触发 xmin 乐观锁
  - [ ] 验证: 单元测试 `Xref_Update_XminConflict` 通过

- [ ] **Task 3.2.11**: naming_field 语义改为快照值
  - [ ] spec v3 D16 修复调整
  - [ ] 前端查 `image_key` 不动态生成
  - [ ] 验证: 单元测试 `Image_NamingField_Snapshot` 通过

### Phase 4 v4 补丁任务验证(13 个)

- [ ] **Task 4.1.11**: Detail.cshtml JSON 数据岛
  - [ ] `<script type="application/json" id="product-data">` 替代 `window.__PRODUCT__`
  - [ ] 挂载点内 SSR 兜底主图
  - [ ] `<script type="module">` 替换 `<script defer>`
  - [ ] 验证: 单元测试 `Razor_JsonIsland_XssDefense` 通过

- [ ] **Task 4.1.12**: product-detail-client.ts ErrorBoundary
  - [ ] `safeMount(id, Comp, props)` 实现
  - [ ] try-catch 降级 UI
  - [ ] 验证: 前端单元测试 `VueMount_ErrorBoundary` 通过

- [ ] **Task 4.1.13**: vite.config.ts 多入口 build
  - [ ] `manualChunks: { vue: ['vue', 'vue-router', 'pinia'] }`
  - [ ] 验证: Lighthouse SEO LCP < 2.5s

- [ ] **Task 4.1.14**: 018_v2_legacy_data_cleanup.sql 外键安全切换
  - [ ] 分阶段 DROP/ADD CONSTRAINT
  - [ ] 验证: 执行脚本无 2BP01 错误

- [ ] **Task 4.1.15**: vue-gallery 命名同步
  - [ ] `gallery-app`/`compare-app`/`inquiry-app`
  - [ ] 验证: 全项目 grep `vue-gallery` 无遗留

- [ ] **Task 4.1.16**: Meilisearch 双索引回滚预案
  - [ ] 阶段 5a/5b/5c 拆分
  - [ ] 旧索引保留 7 天
  - [ ] 验证: 集成测试 `Meili_DualIndex_Rollback` 通过

- [ ] **Task 4.5.6**: CursorHmac 双签名重载
  - [ ] 验签顺序调整(先 HMAC 后 TTL)
  - [ ] 统一 Base64Url 编码
  - [ ] 旧 cursor 过渡期分支(`LEGACY_CUTOFF_TS`)
  - [ ] 双 key 验签
  - [ ] `pageNum` 字段
  - [ ] 验证: 单元测试 `Cursor_DualKey_Rotation` 通过

- [ ] **Task 4.5.7**: IProductWriteStrategy / IProductReadStrategy
  - [ ] 接口定义
  - [ ] 阶段 3 双写策略表
  - [ ] 验证: 集成测试 `ProductWriteStrategy_DualWrite` 通过

- [ ] **Task 4.5.8**: build-product-url.ts 工具函数
  - [ ] `buildProductUrl(p)` 实现
  - [ ] 中文 slugify 兜底(`Uri.EscapeDataString`)
  - [ ] 验证: 单元测试 `BuildProductUrl_ChineseSlugify` 通过

- [ ] **Task 4.5.9**: 全局替换 router.push('/product/...')
  - [ ] 4 处遗漏全部替换
  - [ ] 验证: 全项目 grep `router.push.*product/` 无遗留

- [ ] **Task 4.5.10**: PublicCompareView sessionStorage 改造
  - [ ] 仅持久化 ID 数组
  - [ ] `QuotaExceededError` 降级
  - [ ] 验证: 前端单元测试 `Compare_SessionStorage_QuotaExceeded` 通过

- [ ] **Task 4.6.6**: nginx Googlebot UA 白名单
  - [ ] 限定 location `^/(products|product|sitemap.xml|sitemaps|robots.txt)`
  - [ ] admin 路径严格 RateLimit 无视 UA
  - [ ] 验证: 压测伪造 Googlebot UA 访问 admin 被拦截

- [ ] **Task 4.6.7**: Detail.cshtml.cs 404 友好页
  - [ ] 404 渲染友好页 + 站内搜索入口
  - [ ] 验证: E2E 测试 404 页有搜索框

- [ ] **Task 4.6.8**: AdminProductService 注入 IProductWriteStrategy
  - [ ] CreateAsync/UpdateAsync 按 strategy 决定写入目标
  - [ ] ETL `EtlImportService` 同理
  - [ ] 验证: 集成测试 `ProductWriteStrategy_DualWrite` 通过

- [ ] **Task 4.8.1**: 前端 types.ts 新增 AggregateSearchHit
  - [ ] `AggregateSearchHit` + `AggregateSearchResponse` 类型
  - [ ] `SearchHit` 补 `mr1`/`productName1`/`oemList` 字段
  - [ ] 验证: `npm run typecheck` 通过

- [ ] **Task 4.8.2**: 前端 api/index.ts 新增 searchApi.aggregate
  - [ ] `searchApi.aggregate(req)` 对接 `POST /api/public/search/aggregate`
  - [ ] `SearchView.vue` 改用新 API
  - [ ] 验证: 前端单元测试 `SearchApi_Aggregate` 通过

- [ ] **Task 4.9.1**: 前端单元测试新增
  - [ ] `html-sanitizer.test.ts` / `build-product-url.test.ts` / `GalleryApp.test.ts` / `error-code-map.test.ts`
  - [ ] 验证: `npm run test:unit` 全绿

### Phase 5 v4 补丁任务验证(9 个)

- [ ] **Task 5.1.10**: ETL products_stage 加 V2 字段
  - [ ] `mr_1`/`oem_2`/`d4_mm`/`h4_mm`/`d*_raw`/`h*_raw` 字段
  - [ ] 精度改 `NUMERIC(10,2)`
  - [ ] 验证: 集成测试 ETL 导入 V2 字段正确入库

- [ ] **Task 5.1.11**: ETL products COPY + JSONL 加 mr_1
  - [ ] COPY 列清单加 `mr_1`
  - [ ] JSONL 解析加 `mr_1`(必填 + 格式校验)
  - [ ] 验证: 集成测试 ETL 导入 mr_1 列有值

- [ ] **Task 5.1.12**: ETL INSERT INTO products 加 mr_1
  - [ ] INSERT 列清单加 `mr_1`
  - [ ] `ON CONFLICT (mr_1) WHERE mr_1 IS NOT NULL`
  - [ ] 验证: 集成测试 ETL 重复 mr_1 无 42P10

- [ ] **Task 5.1.13**: ETL LoadExistingOemMapAsync 改查 mr_1
  - [ ] 改为查 `mr_1`
  - [ ] JSONL 字段名 `product_oem` → `mr_1`
  - [ ] 验证: 集成测试 ETL 二次导入 update 路径正确

- [ ] **Task 5.1.14**: ETL xrefs_stage + COPY + INSERT 加 V2 字段
  - [ ] `sort_order`/`machine_type`/`is_published`/`oem_2` 字段
  - [ ] 验证: 集成测试 ETL 导入 xrefs V2 字段正确入库

- [ ] **Task 5.1.15**: ETL cascade 语义重新定义
  - [ ] `cascade=false` 时显式 TRUNCATE products + product_images
  - [ ] 验证: 集成测试 ETL cascade=false 旧数据被清空

- [ ] **Task 5.1.16**: ETL xrefs INSERT 前 DELETE 旧下架行
  - [ ] 避免 23505 错误
  - [ ] 验证: 集成测试 ETL 二次导入下架 OEM 3 无错误

- [ ] **Task 5.1.17**: ETL GetStringOrNull 加控制字符过滤
  - [ ] 允许 `\t` `\n` `\r`
  - [ ] 验证: 单元测试 `Etl_GetStringOrNull_ControlChar` 通过

- [ ] **Task 5.1.18**: CleanupOrphanImagesAsync 应用层脚本
  - [ ] TRUNCATE product_images 后扫描 OSS 清理孤儿文件
  - [ ] 验证: 集成测试 TRUNCATE 后无孤儿文件

---

## 第四轮深度审查验证点(v4 修复衍生风险)

> 启动 3 个并行子代理(数据/检索/联动)对 v4 修复后再次深度审查
> 验证目标: v4 修复是否产生衍生问题、是否有遗漏场景
> 循环终止条件: 连续一轮审查无任何新漏洞检出

### 第四轮审查重点(v4 修复方案的衍生风险)

#### 数据关联维度第四轮审查
- [ ] v4 CursorHmac 双签名重载是否导致旧 cursor 客户端在 LEGACY_CUTOFF_TS 后被强制登出而无降级路径
- [ ] v4 F20 双表灰度 5 阶段顺序在阶段 3(读切换)时,若有并发写入是否导致数据不一致
- [ ] v4 Meilisearch 双索引 5 阶段切换时,阶段 2(双写)失败是否触发回滚,回滚后旧索引是否仍可用
- [ ] v4 BuildMr1DocumentAsync 过滤 `b.deleted_at IS NULL` 时,若 brand 软删除后立即恢复,Meilisearch 索引是否同步恢复
- [ ] v4 ETL xrefs INSERT 前 DELETE 旧下架行的策略在大批量场景下是否引发锁竞争
- [ ] v4 AdminProductService 反向更新 products.oem_2 与 ETL 全量导入的并发写冲突
- [ ] v4 AdminProductService.UpdateAsync xref 增量更新在 sort_order 全部为 0 时排序稳定性
- [ ] v4 Partition6Placeholder EF Core 注册后,是否被误加入 Meilisearch 索引或前端查询
- [ ] v4 naming_field 语义改为"命名快照值"后,旧数据的 naming_field 为 NULL,前端展示兜底
- [ ] v4 CleanupOrphanImagesAsync 在 MinIO→OSS 切换期间的双存储孤儿清理

#### 检索逻辑维度第四轮审查
- [ ] v4 Meilisearch 高亮标签 `\u0001MO\u0001`/`\u0001MC\u0001` 在 BMP 私用区是否与中文 CJK 兼容区冲突
- [ ] v4 递归 SanitizeFormatted 在深层嵌套 JSON(如 50 个 OEM 3 + 100 个机型)时的栈溢出风险
- [ ] v4 PG 兜底分词 OR 拼接在用户输入 100+ token 时的性能(参数化 IN 列表上限)
- [ ] v4 PG keyset 分页在排序字段(brand_sort_order_min)值大量重复时跳页或卡页
- [ ] v4 PG lat_machine LATERAL LIMIT 50 是否漏掉关键机型(如热门机型被截断)
- [ ] v4 Meilisearch 扁平化冗余字段 `OemListPublishedBrands` 在 filter 拼接时的语义(`IN` vs `AND`)
- [ ] v4 Brand sort_order 变更后台重建的 Channel 队列在应用崩溃时是否丢任务
- [ ] v4 search_index_pending 表持久化兜底在多实例部署时的分布式锁
- [ ] v4 IOptionsMonitor 热切换在配置文件热重载时的瞬态不一致
- [ ] v4 双索引 DeleteAsync 同步删除在阶段 2(双写)期间,删除失败是否导致旧索引残留

#### 前后端联动维度第四轮审查
- [ ] v4 JSON 数据岛在 Product.remark 含 `</script>` 字面量时是否仍能正确解析(`<script type="application/json">` 不解析实体)
- [ ] v4 safeMount ErrorBoundary 在 Vue chunk 加载失败时是否进入死循环(重试 → 失败 → 重试)
- [ ] v4 vite.config.ts manualChunks 后,vue chunk 缓存失效策略(版本号变更)
- [ ] v4 buildProductUrl 中文 slugify 兜底 `Uri.EscapeDataString` 在 RFC 3986 严格模式下是否被 nginx 误判为非法 URL
- [ ] v4 ERROR_CODE_I18N 字符串映射在新增错误码时,旧前端版本是否因找不到映射而白屏
- [ ] v4 CURSOR 自动重置逻辑在用户已浏览到第 50 页时,突然重置到第 1 页的 UX 提示
- [ ] v4 Googlebot UA 白名单在 nginx 配置热重载期间的瞬态 503
- [ ] v4 IProductWriteStrategy 双写策略在阶段 3(读切换)时,若写入新表失败是否回滚到旧表
- [ ] v4 sessionStorage QuotaExceededError 降级在 Safari 隐私模式下的兼容性
- [ ] v4 vue-gallery → gallery-app 命名同步在 E2E 测试基线截图的回归

### 持续迭代验证点(每轮审查后追加)

> 每完成一轮审查 + 修复后,在此追加下一轮验证点
> 循环终止条件: 连续一轮审查无任何新漏洞检出

### 第五轮审查(暂无,待第四轮审查后追加)
_待启动第四轮深度审查后追加_

### 第六轮审查(暂无,待第四轮审查后追加)
_待启动第四轮深度审查后追加_
