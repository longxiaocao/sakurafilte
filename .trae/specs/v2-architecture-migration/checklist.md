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

---

## v5 修订 48 项衍生漏洞修复验证清单

> 对照 spec.md 末尾"第四轮深度审查衍生漏洞修复清单(v5 修订)"第一节"数据关联维度衍生漏洞(21 项)" + 第二节"检索逻辑维度衍生漏洞(16 项)" + 第三节"前后端联动维度衍生漏洞(14 项)",逐项验证修复落地。
> 48 项 = 21(D4) + 16(S4) + 14(F3) - 3 重复(D4-3/F3-6 同源 + D4-15/D4-16 关联 + S4-9/D4-6 关联)

### 一、数据关联维度衍生漏洞修复验证(21 项)

#### D4-1: CursorHmac LEGACY_CUTOFF_TS 硬编码已过期 [高]
- [ ] `CursorHmacOptions` 配置类含 `CurrentKey` / `PreviousKey` / `LegacyCutoffTs` 三字段
- [ ] `appsettings.json` 含 `CursorHmac:CurrentKey` + `CursorHmac:PreviousKey` + `CursorHmac:LegacyCutoffTs` 配置项
- [ ] `CursorHmac.cs` 构造函数注入 `IOptions<CursorHmacOptions>`
- [ ] `LegacyCutoffTs` 默认值 = `DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()`(部署时配置)
- [ ] 单元测试 `CursorHmac_LegacyCutoffTs_FromConfig` 通过

#### D4-2: AdminProductService UpdateAsync 与 ETL DELETE+INSERT 并发冲突 [中]
- [ ] `AdminProductService.UpdateAsync` 事务内执行 `pg_try_advisory_xact_lock(7740002)`
- [ ] 锁获取失败抛 `ETL_IN_PROGRESS` 错误码
- [ ] 单元测试 `Admin_XrefLock_Acquired` 通过

#### D4-3: IProductWriteStrategy 双写事务边界未明(与 F3-6 同源) [高]
- [ ] `IProductWriteStrategy` 接口含 `WriteAsync` / `DeleteAsync` / `RestoreAsync` 三方法
- [ ] `AdminProductService.CreateAsync` / `UpdateAsync` / `DeleteAsync` / `RestoreAsync` 全部注入 `IProductWriteStrategy`
- [ ] 双写必须在同一事务内(`BeginTransactionAsync` → `_writeStrategy.WriteAsync` → `CommitAsync`)
- [ ] 失败时 `RollbackAsync` 回滚
- [ ] 对账脚本存在(`spike-test/_reconcile_mr1.py` 或类似)
- [ ] 单元测试 `Product_Delete_WriteStrategyInvoked` + `Product_Restore_WriteStrategyInvoked` 通过

#### D4-4: DeleteAsync/RestoreAsync 未覆盖 IProductWriteStrategy [中]
- [ ] `AdminProductService.DeleteAsync` 调用 `_writeStrategy.DeleteAsync(productId, ct)`
- [ ] `AdminProductService.RestoreAsync` 调用 `_writeStrategy.RestoreAsync(productId, ct)`
- [ ] 两个方法均显式开启事务
- [ ] 单元测试覆盖(见 D4-3)

#### D4-5: ProductImage 多存储后端孤儿图片清理 [中]
- [ ] `CleanupOrphanImagesAsync` 方法存在
- [ ] 遍历所有 `IObjectStorage` 实现(MinIO + Aliyun OSS)
- [ ] 时间戳过滤 `uploaded_at < now() - interval '1 hour'`
- [ ] 单元测试 `Etl_CleanupOrphanImages_MultiBackend` 通过

#### D4-6: Meilisearch 双索引 WriteTargets 阶段 3 _oldIndex 为 null(与 S4-9 关联) [中]
- [ ] `MeiliSearchOptions.WriteTargets: List<string>` 字段存在
- [ ] `MeiliSearchProvider._index` 加 `volatile` 关键字
- [ ] `RefreshWriteTargets()` 方法根据 `WriteTargets` 重建列表
- [ ] `DeleteAsync` 遍历 `_writeTargets` 全部删除
- [ ] 单元测试 `Meili_DeleteAsync_AllWriteTargetsInvoked` 通过

#### D4-7: Brand sort_order 变更无 Channel 写入 [中]
- [ ] `XrefOemBrandService.ApplyChangeAsync(brand, isDeleted)` 方法存在
- [ ] Update/SoftDelete/Restore 全部调用此方法
- [ ] 内部 `Channel<string>.Writer.WriteAsync(brand)` 触发后台重建
- [ ] 单元测试 `XrefOemBrand_ApplyChange_ChannelWrite` 通过

#### D4-8: Channel 崩溃丢任务 [中]
- [ ] Channel 写入失败 fallback 到 `search_index_pending` 表持久化
- [ ] `INSERT INTO search_index_pending (mr_1, action, created_at) SELECT mr_1, 'rebuild', now() FROM cross_references WHERE oem_brand = @brand`
- [ ] 单元测试 `XrefOemBrand_ApplyChange_FallbackToDb` 通过

#### D4-9: ETL TRUNCATE 与 AdminProductService 并发冲突 [中]
- [ ] ETL TRUNCATE 前执行 `LOCK TABLE products IN ACCESS EXCLUSIVE MODE NOWAIT`
- [ ] NOWAIT 失败时 ETL 抛 `ETL_ADMIN_IN_PROGRESS` 错误码
- [ ] AdminProductService 事务内 `pg_try_advisory_xact_lock(7740001)`
- [ ] 单元测试 `Admin_LockConflict_Throws_ETL_IN_PROGRESS` 通过

#### D4-10: AdminProductService xref 写入与 ETL 增量冲突 [中]
- [ ] `CreateAsync` + `UpdateAsync` xref 写入前 `pg_try_advisory_xact_lock(7740002)`
- [ ] 单元测试 `Admin_XrefLock_Acquired` 通过

#### D4-11: AdminProductService Create/Update 与 ETL TRUNCATE 冲突 [中]
- [ ] `CreateAsync` / `UpdateAsync` / `DeleteAsync` 事务开始时 `pg_try_advisory_xact_lock(7740001)`
- [ ] 与 D4-9 协调(7740001 共享锁)
- [ ] 单元测试覆盖

#### D4-12: 增量更新误改下架记录 [中]
- [ ] `UpdateAsync` xref 增量更新匹配条件附加 `WHERE is_published = true AND is_discontinued = false`
- [ ] 单元测试 `Xref_IncrementalUpdate_SkipDiscontinued` 通过

#### D4-13: oem_2 反向更新空指针风险 [中]
- [ ] `oem_2` 取值逻辑: sort_order 排序后第一个非空 `FirstOrDefault(x => !string.IsNullOrEmpty(x.Oem2))?.Oem2`
- [ ] 全空列表置 NULL
- [ ] 单元测试 `Product_UpdateOem2_FallbackToFirstNonNull` 通过

#### D4-14: naming_field NULL 时前端兜底 [低]
- [ ] spec.md L489 含 'legacy' 兜底描述
- [ ] 前端 `AdminProductFormView.vue` 含 `namingField ?? 'legacy'` 分支
- [ ] 旧数据按 `image_key` 字段直接展示,不动态生成 URL

#### D4-15: MinIO 孤儿图片清理(与 D4-16 关联) [中]
- [ ] `CleanupOrphanImagesAsync` 遍历 MinIO 实现
- [ ] 删除 `product_images WHERE uploaded_at < now() - interval '1 hour' AND product_id IS NULL`
- [ ] 单元测试覆盖(见 D4-5)

#### D4-16: Aliyun OSS 孤儿图片清理(与 D4-15 关联) [中]
- [ ] `CleanupOrphanImagesAsync` 遍历 Aliyun OSS 实现
- [ ] 删除 `product_images WHERE product_id IS NOT NULL AND product_id NOT IN (SELECT id FROM products)`
- [ ] 单元测试覆盖(见 D4-5)

#### D4-17: ETL 字段映射黑盒 [中]
- [ ] spec.md D3-9 含完整 `xrefs_stage` 临时表定义 + COPY 列清单 + INSERT 列清单 SQL
- [ ] `EtlImportService.ImportXrefsAsync` 实现与 spec 字段顺序一致
- [ ] 字段顺序与 JSONL 严格对齐

#### D4-18: products NUMERIC(10,2) 与 EF Core decimal(18,2) 不一致 [中]
- [ ] `Product.cs` 8 个尺寸字段 `HasColumnType("numeric(10,2)")`
- [ ] `ProductDbContext.cs` `HasPrecision(10,2)` 显式配置
- [ ] `dotnet ef migrations script` 生成的 SQL 含 `numeric(10,2)`
- [ ] 单元测试 `Product_DecimalPrecision_AllFields` 通过

#### D4-19: cascade=false TRUNCATE 与 FK ON DELETE CASCADE 语义冲突 [高]
- [ ] `EtlImportService` cascade=false 路径先 DROP 所有 FK
- [ ] TRUNCATE products/cross_references/product_images/machine_applications
- [ ] TRUNCATE 后再 ADD FK 重建
- [ ] 单元测试 `Etl_FullLoad_CascadeFalse_NoChildTableWipe` 通过

#### D4-20: LoadExistingOemMapAsync 旧数据无 mr_1 时无法关联 [中]
- [ ] `LoadExistingOemMapAsync` 同时返回 mr_1 map + oem_2 map
- [ ] JSONL 字段优先匹配 mr_1,mr_1 缺失时 fallback 到 oem_2
- [ ] 单元测试 `Etl_Import_FallbackToOem2_WhenMr1Missing` 通过

#### D4-21: 409 XREF_CONFLICT 前端无提示 [低]
- [ ] `AdminProductController.UpdateAsync` 收到 409 返回 `errorCode: "XREF_CONFLICT"` + `detail: "数据已被 ETL 更新,请刷新页面重试"`
- [ ] 前端 `AdminProductFormView.vue` catch 409 时 `ElMessage.warning`
- [ ] 强制重新加载详情
- [ ] 单元测试 `Admin_Update_XrefConflict_409` + 前端 `ProductForm_XrefConflict_ShowToast` 通过

### 二、检索逻辑维度衍生漏洞修复验证(16 项)

#### S4-1: 占位符法 XSS 防御被绕过(C0 控制字符非 BMP 私用区) [高]
- [ ] `MeiliSearchProvider` `MARK_OPEN = "\uE000"` + `MARK_CLOSE = "\uE001"`(BMP 私用区单字符)
- [ ] `SanitizeFormatted` 步骤 1: 暂存到 `\uFDD0`/`\uFDD1`(非字符)
- [ ] 步骤 2: `WebUtility.HtmlEncode` 转义
- [ ] 步骤 3: 移除 C0 控制字符(U+0000-U+001F,保留 \t \n \r) + BMP 私用区(U+E000-U+F8FF) + 非字符(U+FDD0-U+FDEF, U+FFFE/U+FFFF)
- [ ] 步骤 4: 还原 `\uFDD0`→`<mark>` + `\uFDD1`→`</mark>`
- [ ] `AdminProductService.ValidateForm` 加 `StripControlChars` 过滤
- [ ] 单元测试 `Meili_SanitizeFormatted_StripsUserInputMarkerLiteral` 通过(用户输入 `\uE000MO\uE000` 字面量被过滤)
- [ ] 单元测试 `ValidateForm_StripsControlChars` 通过

#### S4-2: Meilisearch nested filter 语法不完整 [中]
- [ ] `oem_list.oem_brand IN [BOSCH]` 语法支持
- [ ] `oem_list.is_published = true` 语法支持
- [ ] 单元测试 `Meili_NestedFilter_Syntax` 通过

#### S4-3: PG 兜底 tokens 无上限 [中]
- [ ] `SearchRequest.MaxTokenCount = 10` 常量存在
- [ ] `PostgresSearchProvider` tokens.Take(MaxTokenCount) 限制
- [ ] 单元测试 `PG_Search_TokenLimit_10` 通过

#### S4-4: keyset 分页排序字段非 UNIQUE 导致跳页 [中]
- [ ] PG 兜底 keyset 分页 SQL 末尾追加 `p.id` 作为 UNIQUE 兜底
- [ ] 四元组比较 `(brand_sort_order_min, oem_list_sort_order_min, updated_at, id)`
- [ ] CursorHmac `SignV2` 追加 `long id` 参数
- [ ] 单元测试 `PG_KeysetPagination_FourTuple_NoSkip` 通过(连续翻页 50 次无重复无跳页)

#### S4-5: Meilisearch nested sort 不支持 [中]
- [ ] `sortableAttributes` 用扁平化 `brand_sort_order_min` + `oem_list_sort_order_min`
- [ ] 单元测试 `Meili_Sort_FlatField` 通过

#### S4-6: filter 语法不完整(单值/多值/AND/OR) [中]
- [ ] `BuildBrandFilter(List<string>, string matchMode)` 方法存在
- [ ] 单值 `IN [x]` / 多值 OR `IN [a, b, c]` / 多值 AND `oem_list_published_brands IN [a] AND ... IN [b]`
- [ ] `SearchRequest.OemBrandMatchMode` 字段(默认 "OR")
- [ ] 单元测试 `Meili_BuildBrandFilter_Single` / `Or` / `And` 通过

#### S4-7: Channel 崩溃丢任务 [中]
- [ ] Channel 写入失败 fallback 到 `search_index_pending` 表(见 D4-8)
- [ ] 单元测试覆盖

#### S4-8: 多实例重复处理 [中]
- [ ] `IndexReplayWorker` `SELECT FOR UPDATE SKIP LOCKED LIMIT 100`
- [ ] `pg_advisory_xact_lock(mr1_hash)` 防跨实例重复
- [ ] retry_count > 3 标记 `is_dead = true`
- [ ] 单元测试 `IndexReplayWorker_SkipLocked` + `AdvisoryLock_PreventsDuplicate` + `DeadLetter_After3Retries` 通过

#### S4-9: _oldIndex 阶段 3 双写期为 null(与 D4-6 关联) [中]
- [ ] `WriteTargets: List<string>` 替代 `IndexName` 单值
- [ ] `_index` 加 `volatile`
- [ ] `DeleteAsync` 遍历 WriteTargets(见 D4-6)
- [ ] 单元测试覆盖

#### S4-10: DeleteAsync 非事务 [中]
- [ ] `DeleteAsync` 遍历 `_writeTargets` 全部删除
- [ ] 任一失败写入死信队列(`Channel<DeleteTask>` + `search_index_pending`)
- [ ] 单元测试 `Meili_DeleteAsync_DeadLetterOnFailure` 通过

#### S4-11: S3-8 修复与 D21 决策语义冲突 [中]
- [ ] `BuildMr1DocumentAsync` `oem_list` 保留软删除 brand 的 OEM 3(查询不过滤 `b.DeletedAt IS NULL`)
- [ ] `brand_sort_order_min` 用 CASE WHEN(`publishedOemList.Where(x => x.BrandDeletedAt == null && x.BrandSortOrder.HasValue)`)
- [ ] 单元测试 `Meili_BuildMr1Doc_BrandSoftDeleted_Oem3StillSearchable` 通过

#### S4-12: 短关键词 ILIKE 全表扫 [低]
- [ ] PG 兜底短关键词(< 3 字符)走精确匹配(`oem_no_3 = @token` 或 `oem_brand = @token`)
- [ ] 不走 ILIKE
- [ ] 单元测试 `PG_Search_ShortKeyword_ExactMatch` 通过

#### S4-13: OemBrandsStr 分隔符 `|` 不在 separatorTokens 中 [中]
- [ ] `OemBrandsStr` / `OemNo3sStr` 分隔符从 `|` 改为空格
- [ ] 对齐 Meilisearch `separatorTokens` 配置
- [ ] 单元测试 `Meili_OemBrandsStr_SpaceSeparated` 通过

#### S4-14: trgm GIN 索引未覆盖 oem_2 [低]
- [ ] `idx_products_oem_2_trgm` GIN 索引存在
- [ ] 单元测试 `PG_Search_Oem2_TrgmHit` 通过

#### S4-15: cursor 签名双 key 轮转期 payload 不一致 [低]
- [ ] `VerifyKeyV2` 双 key 验签(_currentKey + _previousKey)
- [ ] 单元测试 `CursorHmac_DualKey_RotationPeriod` 通过

#### S4-16: sort_order 无冗余字段无法排序 [低]
- [ ] `Mr1IndexDoc` record 含 `int OemListSortOrderMin` 字段
- [ ] `sortableAttributes` 配置加 `oem_list_sort_order_min`
- [ ] 单元测试 `Meili_Sort_ByOemListSortOrderMin` 通过

### 三、前后端联动维度衍生漏洞修复验证(14 项)

#### F3-1: JSON 数据岛安全描述不完整 [中]
- [ ] spec.md L1899 含 `JavaScriptEncoder.Default` + `@Json.Serialize` + 严禁 `@Html.Raw` 三要素
- [ ] `Detail.cshtml` 用 `@Json.Serialize` 而非 `@Html.Raw`

#### F3-2: safeMount 无法捕获 module 加载失败 [中]
- [ ] `Detail.cshtml` `<script type="module">` 后追加 `window.addEventListener('error', ...)` 捕获资源加载错误
- [ ] 渲染 `mount-fallback` UI
- [ ] 手动测试: 断网情况下挂载点显示 "JS 加载失败" 提示

#### F3-3: BuildSlug 中文 slugify 顺序混乱 [中]
- [ ] `BuildSlug` 单一逻辑: 先 `EscapeDataString` 再替换非字母数字(% 保留)
- [ ] 单元测试 `BuildSlug_Chinese_EscapedPreserved` 通过(`"机油滤芯"` → `"%e6%9c%ba%e6%b2%b9%e6%bb%a4%e8%8a%af"`)
- [ ] 单元测试 `BuildSlug_SpecialChar_Hyphenated` 通过

#### F3-4: SEO URL 反向解析未覆盖 [低]
- [ ] `Detail.cshtml.cs` `OnGetAsync(pn1, pn2, brand, oem3)` 反向解析 DB 查询
- [ ] 单元测试 `Razor_DetailPage_ReverseResolve` 通过

#### F3-5: CURSOR 自动重置整页刷新 [中]
- [ ] `http.ts` CURSOR 重置改用 `router.replace` + `sessionStorage` 提示
- [ ] 不触发 `window.location.reload`
- [ ] 前端单元测试 `Http_CursorExpired_RouterReplace` 通过
- [ ] `App.vue` mounted 时检查 sessionStorage 显示一次性 toast

#### F3-6: IProductWriteStrategy 双写事务边界(与 D4-3 同源) [高]
- [ ] 见 D4-3 验证点

#### F3-7: ETL 进度 SSE 在 nginx 磁盘缓冲下的内存泄漏 [低]
- [ ] nginx `proxy_buffering off` 配置 SSE 端点
- [ ] 单元测试 `Sse_NoBuffering` 通过

#### F3-8: spec L1128 vue-gallery 命名未同步 [中]
- [ ] spec.md L1128 `<div id="vue-gallery">` → `<div id="gallery-app">`
- [ ] 与 L1610-1612 的 `gallery-app` / `compare-app` / `inquiry-app` 三挂载点一致
- [ ] 字符串搜索 `id="vue-gallery"` 无命中

#### F3-9: JS 加载/挂载失败无监控 [中]
- [ ] `product-detail-client.ts` `safeMount` catch 块调用 `captureException`
- [ ] tags 含 mr1/oem3
- [ ] 手动测试: Sentry dashboard 收到事件

#### F3-10: 多产品同 pn1/pn2/brand/oem3 时 slug 冲突 [中]
- [ ] `BuildProductUrl` 末尾附加 `mr_1` 末 6 位
- [ ] 单元测试 `BuildProductUrl_Mr1Suffix_PreventsCollision` 通过
- [ ] 单元测试 `BuildProductUrl_ShortMr1_FullString` 通过

#### F3-11: sitemap 分片并发查询的内存峰值 [低]
- [ ] sitemap 分片查询 LIMIT 50000 + 流式输出
- [ ] 单元测试 `Sitemap_Shard_MemoryBounded` 通过

#### F3-12: 跨域 module 加载的 CORS 配置 [低]
- [ ] `Detail.cshtml` `<script type="module">` 加 `crossorigin="use-credentials"`
- [ ] nginx 配置 ACAO + ACAC
- [ ] 部署后 curl 验证 CORS 头

#### F3-13: 旧 API 兼容性未明 [中]
- [ ] `api/index.ts` + `SearchView.vue` 特性检测 `searchApi.aggregate`
- [ ] 404 `ENDPOINT_NOT_FOUND` 时 fallback 到 `searchApi.search`
- [ ] 前端单元测试 `Search_FallbackToOldApi_On404` 通过

#### F3-14: 跨域 module 加载失败(与 F3-12 关联) [中]
- [ ] 见 F3-12 验证点

---

## 36 个 v5 补丁任务验证清单

> 对照 tasks.md 末尾"v5 补丁任务清单(共 36 个)",按 Phase 0/1/3/4/5 分布逐项验证。

### Phase 0 v5 补丁任务验证(19 个)

#### Task 0.2.19: Product.cs + ProductDbContext.cs 精度配置
- [ ] 8 个尺寸字段 `HasColumnType("numeric(10,2)")` + `HasPrecision(10,2)`
- [ ] `dotnet ef migrations script` SQL 含 `numeric(10,2)`
- [ ] 单元测试 `Product_DecimalPrecision_AllFields` 通过

#### Task 0.2.20: spec L1128 挂载点命名同步
- [ ] spec.md L1128 `id="gallery-app"`
- [ ] 字符串搜索 `id="vue-gallery"` 无命中

#### Task 0.2.21: oem_2 反向更新 fallback 逻辑
- [ ] `FirstOrDefault(x => !string.IsNullOrEmpty(x.Oem2))?.Oem2` 实现
- [ ] 单元测试 `Product_UpdateOem2_FallbackToFirstNonNull` 通过

#### Task 0.2.22: 增量更新匹配条件附加 WHERE
- [ ] `WHERE is_published = true AND is_discontinued = false`
- [ ] 单元测试 `Xref_IncrementalUpdate_SkipDiscontinued` 通过

#### Task 0.2.23: naming_field NULL 时 'legacy' 策略
- [ ] spec.md L489 含 'legacy' 兜底描述
- [ ] 前端 `namingField ?? 'legacy'` 分支

#### Task 0.2.24: xrefs_stage 完整 SQL
- [ ] spec.md D3-9 含 CREATE TEMP TABLE + COPY + INSERT SQL
- [ ] `EtlImportService.ImportXrefsAsync` 字段顺序一致

#### Task 0.3.16: ValidateForm StripControlChars
- [ ] `StripControlChars` 方法存在
- [ ] 移除 U+0000-U+001F(保留 \t \n \r) + U+007F-U+009F + BMP 私用区 + 非字符
- [ ] 单元测试 `ValidateForm_StripsControlChars` 通过

#### Task 0.3.17: DeleteAsync + RestoreAsync 注入 IProductWriteStrategy
- [ ] `AdminProductService` 构造函数注入 `IProductWriteStrategy`
- [ ] `DeleteAsync` + `RestoreAsync` 调用 `_writeStrategy.DeleteAsync` / `RestoreAsync`
- [ ] 单元测试 `Product_Delete_WriteStrategyInvoked` + `Product_Restore_WriteStrategyInvoked` 通过

#### Task 0.3.18: Create/Update/Delete advisory_xact_lock(7740001)
- [ ] 事务内 `pg_try_advisory_xact_lock(7740001)`
- [ ] 失败抛 `ETL_IN_PROGRESS`
- [ ] 单元测试 `Admin_LockConflict_Throws_ETL_IN_PROGRESS` 通过

#### Task 0.3.19: xref 写入前 advisory_xact_lock(7740002)
- [ ] `CreateAsync` + `UpdateAsync` xref 写入前 `pg_try_advisory_xact_lock(7740002)`
- [ ] 单元测试 `Admin_XrefLock_Acquired` 通过

#### Task 0.3.20: 409 XREF_CONFLICT 前端提示
- [ ] `AdminProductController.UpdateAsync` 409 返回 `errorCode: "XREF_CONFLICT"` + detail
- [ ] 前端 `ElMessage.warning` + 强制重新加载
- [ ] 单元测试 + 前端单元测试通过

#### Task 0.4.16: SearchQuery 高亮标签 BMP 私用区
- [ ] `MARK_OPEN = "\uE000"` + `MARK_CLOSE = "\uE001"`
- [ ] 单元测试 `Meili_HighlightTag_BmpPrivateArea` 通过

#### Task 0.4.17: SanitizeFormatted 重构
- [ ] 4 步骤实现(暂存 → HtmlEncode → 过滤 → 还原)
- [ ] 单元测试 `Meili_SanitizeFormatted_StripsUserInputMarkerLiteral` + `RestoresMarkTag` 通过

#### Task 0.4.18: BuildMr1DocumentAsync 保留软删除 brand
- [ ] 查询不过滤 `b.DeletedAt IS NULL`
- [ ] `brand_sort_order_min` 用 CASE WHEN
- [ ] 单元测试 `Meili_BuildMr1Doc_BrandSoftDeleted_Oem3StillSearchable` 通过

#### Task 0.4.19: Mr1IndexDoc OemListSortOrderMin
- [ ] `Mr1IndexDoc` 含 `OemListSortOrderMin` 属性
- [ ] `sortableAttributes` 含 `oem_list_sort_order_min`

#### Task 0.4.20: MeiliSearchOptions WriteTargets
- [ ] `WriteTargets: List<string>` 字段
- [ ] `appsettings.json` 含 `MeiliSearch:WriteTargets`

#### Task 0.4.21: MeiliSearchProvider volatile + RefreshWriteTargets + 死信队列
- [ ] `_index` 加 `volatile`
- [ ] `RefreshWriteTargets()` 方法
- [ ] `DeleteAsync` 遍历 WriteTargets + 死信队列
- [ ] 单元测试 `Meili_DeleteAsync_AllWriteTargetsInvoked` + `DeadLetterOnFailure` 通过

#### Task 0.4.22: OemBrandsStr 分隔符改空格
- [ ] `string.Join(" ", ...)` 替代 `string.Join("|", ...)`
- [ ] 单元测试 `Meili_OemBrandsStr_SpaceSeparated` 通过

#### Task 0.4.23: BuildBrandFilter 方法
- [ ] 单值/多值/AND/OR 三模式
- [ ] 单元测试 `Meili_BuildBrandFilter_Single` / `Or` / `And` 通过

### Phase 1 v5 补丁任务验证(3 个)

#### Task 1.2.13b: SearchRequest MaxTokenCount + OemBrandMatchMode
- [ ] `MaxTokenCount = 10` 常量
- [ ] `OemBrandMatchMode` 字段(默认 "OR")
- [ ] 单元测试 `SearchRequest_DefaultMatchMode_IsOR` 通过

#### Task 1.2.14a: PG 兜底 keyset 四元组
- [ ] SQL 含 `p.id` UNIQUE 兜底
- [ ] 四元组比较 `(brand_sort_order_min, oem_list_sort_order_min, updated_at, id)`
- [ ] cursor 含 id 字段
- [ ] 单元测试 `PG_KeysetPagination_FourTuple_NoSkip` + `CursorIncludesId` 通过

#### Task 1.2.15a: PG 兜底 tokens.Take + 短关键词精确匹配
- [ ] `tokens.Take(MaxTokenCount)` 限制
- [ ] 短关键词(< 3 字符)走 `=` 不走 ILIKE
- [ ] 单元测试 `PG_Search_TokenLimit_10` + `ShortKeyword_ExactMatch` 通过

### Phase 3 v5 补丁任务验证(1 个)

#### Task 3.2.12: ETL cascade=false DROP FK → TRUNCATE → ADD FK
- [ ] cascade=false 路径先 DROP 所有 FK
- [ ] TRUNCATE 后 ADD FK 重建
- [ ] 单元测试 `Etl_FullLoad_CascadeFalse_NoChildTableWipe` 通过

### Phase 4 v5 补丁任务验证(9 个)

#### Task 4.1.17: Detail.cshtml error 事件捕获
- [ ] `window.addEventListener('error', ...)` 捕获资源加载错误
- [ ] 渲染 `mount-fallback` UI
- [ ] 手动测试: 断网显示 "JS 加载失败"

#### Task 4.1.18: crossorigin="use-credentials" + nginx CORS
- [ ] `<script type="module">` 加 `crossorigin="use-credentials"`
- [ ] nginx ACAO + ACAC 配置
- [ ] curl 验证 CORS 头

#### Task 4.1.19: safeMount captureException
- [ ] `safeMount` catch 块调用 `captureException`
- [ ] tags 含 mr1/oem3
- [ ] 手动测试: Sentry 收到事件

#### Task 4.1.20: spec L1899 JSON 数据岛描述修正
- [ ] spec.md L1899 含 `JavaScriptEncoder.Default` + `@Json.Serialize` + 严禁 `@Html.Raw`
- [ ] `Detail.cshtml` 用 `@Json.Serialize`

#### Task 4.5.11: CursorHmac 配置化 + id 字段
- [ ] `IOptions<CursorHmacOptions>` 注入
- [ ] `SignV2` 追加 `long id` 参数
- [ ] payload 格式 `v2:{expUnixTs}|{tsB64}|{mr1B64}|{pageNum}|{id}`
- [ ] `VerifyAndExtractV2` 返回四元组
- [ ] `pageNum > 1000` 抛 `CURSOR_PAGE_TOO_DEEP`
- [ ] 单元测试 `CursorHmac_SignV2_IncludesId` + `VerifyAndExtractV2_ReturnsFourTuple` + `LegacyCutoffTs_FromConfig` 通过

#### Task 4.5.12: BuildSlug 单一逻辑
- [ ] 先 `EscapeDataString` 再替换非字母数字(% 保留)
- [ ] 单元测试 `BuildSlug_Chinese_EscapedPreserved` + `SpecialChar_Hyphenated` + `Empty_ReturnsUntyped` 通过

#### Task 4.5.13: BuildProductUrl mr_1 末 6 位
- [ ] `mr1Suffix = p.Mr1.Length > 6 ? p.Mr1[^6..] : p.Mr1`
- [ ] URL 格式 `/products/{pn1Slug}-{mr1Suffix}/{pn2Slug}/{brandSlug}/{oem3Slug}`
- [ ] 单元测试 `BuildProductUrl_Mr1Suffix_PreventsCollision` + `ShortMr1_FullString` 通过

#### Task 4.5.14: CURSOR router.replace + sessionStorage
- [ ] `router.replace({ path, query: { ...query, page: 1 } })` 替代 `window.location.reload`
- [ ] `sessionStorage.setItem('cursor-reset-toast', '1')`
- [ ] `App.vue` mounted 检查 sessionStorage 显示一次性 toast
- [ ] 前端单元测试 `Http_CursorExpired_RouterReplace` + `App_CursorResetToast_OneTimeShow` 通过

#### Task 4.5.15: searchApi.aggregate 特性检测 + fallback
- [ ] `typeof searchApi.aggregate === 'function'` 检测
- [ ] 404 `ENDPOINT_NOT_FOUND` fallback 到 `searchApi.search`
- [ ] 前端单元测试 `Search_FallbackToOldApi_On404` + `UseAggregate_WhenAvailable` 通过

### Phase 5 v5 补丁任务验证(4 个)

#### Task 5.1.19: LoadExistingOemMapAsync 双 key
- [ ] 同时返回 mr_1 map + oem_2 map
- [ ] 优先匹配 mr_1,缺失时 fallback 到 oem_2
- [ ] 单元测试 `Etl_LoadExistingOemMap_DualKey` + `Etl_Import_FallbackToOem2_WhenMr1Missing` 通过

#### Task 5.1.20: CleanupOrphanImagesAsync
- [ ] 遍历所有 `IObjectStorage` 实现
- [ ] 时间戳过滤 `uploaded_at < now() - interval '1 hour'`
- [ ] 单元测试 `Etl_CleanupOrphanImages_MultiBackend` + `TimestampFilter` 通过

#### Task 5.1.21: XrefOemBrandService.ApplyChangeAsync
- [ ] Update/SoftDelete/Restore 全部调用
- [ ] Channel 写入 + fallback 到 `search_index_pending`
- [ ] 单元测试 `XrefOemBrand_ApplyChange_ChannelWrite` + `FallbackToDb` + `Restore_TriggersRebuild` 通过

#### Task 5.1.22: IndexReplayWorker SKIP LOCKED + advisory_xact_lock
- [ ] `SELECT FOR UPDATE SKIP LOCKED LIMIT 100`
- [ ] `pg_advisory_xact_lock(mr1_hash)` 防跨实例重复
- [ ] retry_count > 3 标记 `is_dead = true`
- [ ] 单元测试 `IndexReplayWorker_SkipLocked` + `AdvisoryLock_PreventsDuplicate` + `DeadLetter_After3Retries` 通过

---

## 第五轮深度审查验证点(v5 修复衍生风险)

> 第五轮审查将在 v5 修订完成后启动,验证 v5 修复方案是否产生新的衍生问题。

### 一、数据关联维度第五轮审查重点(10 个验证点)

- [ ] v5 advisory_xact_lock(7740001/7740002) 在 PostgreSQL 14+ 的事务级锁语义是否正确(事务结束自动释放)
- [ ] v5 IProductWriteStrategy 双写在阶段 3(读切换)时,若新索引写入失败是否回滚到旧索引
- [ ] v5 WriteTargets 配置热切换期间,正在进行的 DeleteAsync 是否会丢失目标
- [ ] v5 CleanupOrphanImagesAsync 遍历多 IObjectStorage 时,若 1 个失败是否影响其他存储后端
- [ ] v5 LoadExistingOemMapAsync 双 key fallback 时,oem_2 重复是否导致 mr_1 错误关联
- [ ] v5 BuildMr1DocumentAsync 保留软删除 brand 的 OEM 3 后,brand_sort_order_min 全软删除时 int.MaxValue 是否影响排序
- [ ] v5 cascade=false DROP FK → TRUNCATE → ADD FK 期间,并发 AdminProductService 写入是否被阻塞
- [ ] v5 StripControlChars 过滤是否误删合法的 Unicode 辅助平面字符(如 emoji)
- [ ] v5 IProductWriteStrategy 对账脚本的频率与 Meilisearch 索引延迟的容忍度
- [ ] v5 IndexReplayWorker SKIP LOCKED 在 PostgreSQL 14+ 的行级锁兼容性

### 二、检索逻辑维度第五轮审查重点(10 个验证点)

- [ ] v5 BMP 私用区 U+E000/U+E001 在 Meilisearch 不同版本的高亮标签支持
- [ ] v5 SanitizeFormatted 步骤 3 过滤 BMP 私用区后,是否影响合法的中文/日文/韩文字符
- [ ] v5 keyset 四元组比较在 brand_sort_order_min = NULL时的 NULLS LAST 语义
- [ ] v5 tokens.Take(10) 截断后,长查询的搜索召回率是否下降
- [ ] v5 短关键词(< 3 字符)精确匹配在 100 万数据量下的性能
- [ ] v5 BuildBrandFilter AND 模式在 5+ 品牌时的 filter 长度限制
- [ ] v5 OemBrandsStr 空格分隔后,品牌名含空格(如 "AUDI AG")是否被错误分词
- [ ] v5 Mr1IndexDoc OemListSortOrderMin 在 oem_list 为空时的默认值
- [ ] v5 CursorHmac 双 key 轮转期,旧 key 签名的 cursor 在 LegacyCutoffTs 后的行为
- [ ] v5 WriteTargets 死信队列的容量上限与溢出处理

### 三、前后端联动维度第五轮审查重点(10 个验证点)

- [ ] v5 BuildSlug `Uri.EscapeDataString` 在 .NET 8 的行为是否与 .NET 6 一致(中文编码)
- [ ] v5 BuildProductUrl mr_1 末 6 位在 MR.1 长度 < 6 时的 URL 可读性
- [ ] v5 router.replace CURSOR 重置在路由守卫期间的副作用
- [ ] v5 sessionStorage 在 Safari 隐私模式下的 QuotaExceededError 处理
- [ ] v5 searchApi.aggregate 特性检测在 SSR 预渲染阶段的行为
- [ ] v5 `window.addEventListener('error', ...)` 是否误捕获非资源加载错误
- [ ] v5 `crossorigin="use-credentials"` 在同源部署时的副作用
- [ ] v5 `captureException` 在 Sentry SDK 未加载时的兜底
- [ ] v5 409 XREF_CONFLICT 前端强制重新加载是否会丢失未保存的表单数据
- [ ] v5 Detail.cshtml error 事件捕获在 React/Vue 框架内部的错误冒泡行为

### 持续迭代验证点(每轮审查后追加)

> 每完成一轮审查 + 修复后,在此追加下一轮验证点
> 循环终止条件: 连续一轮审查无任何新漏洞检出

### 第六轮审查(已完成,发现 33 个衍生漏洞)

第六轮三维度并行深度审查已完成,发现 33 个衍生漏洞:
- 数据关联维度 9 项(D6-1 ~ D6-9):高危 3 / 中危 4 / 低危 2
- 检索逻辑维度 13 项(S6-1 ~ S6-13):高危 3 / 中危 7 / 低危 3
- 前后端联动维度 11 项(F5-1 ~ F5-11):高危 4 / 中危 5 / 低危 2

**关键发现**: v6 修复方案存在 3 项对当前代码状态的事实性误判:
- E1 [高] v6 D5-7 误判 ProductDbContext 无 FK,实际已有 3 个 FK CASCADE(AddForeignKeysV6 必失败,42710 错误)
- E2 [高] v6 D5-5 误判 LoadExistingOemMapAsync 读 oem_2,实际读 oem_no_normalized
- E3 [高] v6 D5-1 TRUNCATE CASCADE 遗漏 product_images 表级联(已有 FK)

详见 spec.md L3584+ "第七轮深度审查衍生漏洞修复清单(v7 修订)"
v7 修复方案: 11 项关键设计调整 + 27 个补丁任务(Phase 0/1/3/4/5 分布)

### 第七轮审查(待启动,验证 v7 修复衍生风险)

_待启动第七轮深度审查后追加验证点_

---

## v6 修订 37 项衍生漏洞修复验证清单

> 第六轮(即第五轮迭代)审查发现 37 个衍生漏洞,本节为 v6 修复方案的逐项验证清单
> 验证范围: 数据关联 8 项(D5-1~D5-8) + 检索逻辑 15 项(S5-1~S5-15) + 前后端联动 14 项(F4-1~F4-14)

### 一、数据关联维度 v6 修复验证(8 项)

- [ ] **D5-1** advisory_xact_lock 紧接 BeginTransactionAsync 后通过 GetDbConnection().CreateCommand() 执行,复用同一事务连接
- [ ] **D5-1** lock 失败时立即 RollbackAsync + 抛 XREF_CONFLICT (409) + 日志 AdvisoryLockFailed
- [ ] **D5-1** 单元测试 `AdvisoryXactLock_BindsToTransaction` + `AdvisoryXactLock_Failure_RollsBack` 通过
- [ ] **D5-2** 对账 SQL `WHERE p.mr_1 IS NOT NULL AND p.mr_1 <> m.mr_1` 过滤 NULL
- [ ] **D5-2** NULL 记录单独统计 + 告警阈值(NULL 比例 > 5% 时告警)
- [ ] **D5-2** 单元测试 `Reconcile_SkipsNullMr1` + `Reconcile_NullRatioAlert` 通过
- [ ] **D5-3** WriteTargets 改用 IOptionsMonitor<MeiliOptions> + OnChange 回调
- [ ] **D5-3** WriteTargets 属性返回 IReadOnlyList<string>,内部存储为 ImmutableArray<string>
- [ ] **D5-3** 配置变更时通过 CancellationToken 取消并重启正在进行的 DeleteAsync
- [ ] **D5-3** 单元测试 `WriteTargets_HotSwap_NoException` 通过(并发 ToList 与配置变更)
- [ ] **D5-4** 每个 IObjectStorage 实现的清理用 try-catch 包裹,失败记录到日志但继续下一个
- [ ] **D5-4** 时间戳统一用 UTC: `uploaded_at < DateTime.UtcNow.AddHours(-1)` + 列类型 timestamptz
- [ ] **D5-4** 清理失败记录到 cleanup_failures 表(id/storage_backend/last_failure_at/retry_count)
- [ ] **D5-4** 单元测试 `CleanupOrphanImages_PartialFailure_Continues` + `CleanupOrphanImages_UtcTimezone` 通过
- [ ] **D5-5** LoadExistingOemMapAsync 检测到 oem_2 多值时返回 Dictionary<string, List<string>>
- [ ] **D5-5** 调用方检测到多值时拒绝 fallback + 记录 Oem2Ambiguous 告警 + 写入 import_skips 表
- [ ] **D5-5** oem_2 多值比例 > 1% 时阻断 ETL 导入
- [ ] **D5-5** 单元测试 `LoadOemMap_Oem2Ambiguous_SkipsRecord` 通过
- [ ] **D5-6** brand_sort_order_min 改为 long? 可空类型,NULL 表示"无有效 brand"
- [ ] **D5-6** ORDER BY 时显式声明 NULLS LAST,NULL 行排在最后
- [ ] **D5-6** 全软删除时 brand_sort_order_min = NULL (而非 int.MaxValue)
- [ ] **D5-6** oem_list_sort_order_min 同样处理
- [ ] **D5-6** 单元测试 `BuildMr1Doc_AllBrandSoftDeleted_NullsLast` + `BuildMr1Doc_PartialBrandSoftDeleted` 通过
- [ ] **D5-7** ETL TRUNCATE 改用 `TRUNCATE products, cross_references, machine_applications, product_images RESTART IDENTITY CASCADE` 单条 SQL
- [ ] **D5-7** ProductDbContext.OnModelCreating 中显式添加 FK 配置(CrossReference/MachineApplication/ProductImage 三个 HasOne + OnDelete Cascade)
- [ ] **D5-7** EF Core 迁移 AddForeignKeysV6 生成 + UP/DOWN 脚本正确
- [ ] **D5-7** 单元测试 `Truncate_CascadesToChildren` + `Fk_AddedAndDropped` + `ProductDbContext_FkConfiguration` 通过
- [ ] **D5-8** 文档明确调用顺序: StripControlChars 在 BuildSlug 之前
- [ ] **D5-8** BuildSlug 方法头部加 WHY 注释
- [ ] **D5-8** 单元测试 `StripControlChars_Before_BuildSlug_Order` 通过

### 二、检索逻辑维度 v6 修复验证(15 项)

- [ ] **S5-1** SanitizeFormatted 步骤 0(新增): 过滤用户输入 \uFDD0/\uFDD1 字面量
- [ ] **S5-1** 步骤 3 还原逻辑改为 `if (c == 0xFDD0) sb.Append("<mark>"); else if (c == 0xFDD1) sb.Append("</mark>"); else sb.Append(c);` (而非过滤)
- [ ] **S5-1** 步骤 4(新增): 还原后再次扫描残留 + 记日志 + 移除
- [ ] **S5-1** 单元测试 `SanitizeFormatted_PreservesHighlight` + `SanitizeFormatted_StripsUserLiteralFDD0` 通过
- [ ] **S5-2** keyset WHERE 条件改用显式方向: `WHERE ROW(b, o, u DESC, i DESC) < ROW(@prev_b, @prev_o, @prev_u, @prev_i)`
- [ ] **S5-2** DESC 字段在行构造器中显式声明方向,整体用 `<` (向后翻页)
- [ ] **S5-2** 单元测试 `Keyset_SecondPage_ReturnsCorrectRows` + `Keyset_DescDirection_Consistent` 通过
- [ ] **S5-3** 用 COALESCE 替换 NULL 为哨兵值: `COALESCE(b, 9223372036854775807)`
- [ ] **S5-3** long.MaxValue 作为"无有效 brand"的哨兵,与 NULLS LAST 语义对齐
- [ ] **S5-3** 单元测试 `Keyset_NullBrandSortOrder_PaginatesCorrectly` 通过
- [ ] **S5-4** 步骤 0 实现: `input = input.Replace("\uFDD0", "").Replace("\uFDD1", "");`
- [ ] **S5-4** 单元测试 `SanitizeFormatted_UserInputFDD0_Stripped` + `SanitizeFormatted_XSS_Bypass_Prevented` 通过
- [ ] **S5-5** 截断前先按 token 长度降序排序
- [ ] **S5-5** 停用词优先剔除: 先过滤 stopWords,再 Take
- [ ] **S5-5** MaxTokenCount 从配置注入,默认 10
- [ ] **S5-5** 单元测试 `Tokens_Take_PreservesImportantTokens` + `Tokens_StopWordsFiltered` 通过
- [ ] **S5-6** 实现 `EscapeMeiliFilterValue(string value)` 转义双引号和反斜杠
- [ ] **S5-6** BuildBrandFilter 调用 EscapeMeiliFilterValue 转义品牌名
- [ ] **S5-6** 单元测试 `BuildBrandFilter_EscapesQuote` + `BuildBrandFilter_EscapesBackslash` 通过
- [ ] **S5-7** OemBrandsStr 内部分隔符改用 \u0001 (SOH)
- [ ] **S5-7** Meilisearch separatorTokens 配置加入 \u0001
- [ ] **S5-7** 单元测试 `OemBrandsStr_SpaceInBrandName_Preserved` + `OemBrandsStr_SohSeparator_Tokenized` 通过
- [ ] **S5-8** Channel 容量限制为 10000: `Channel.CreateBounded<DeleteTask>(10000)`
- [ ] **S5-8** 满时 WriteAsync 阻塞等待(默认超时 30 秒) + 日志告警 DeadLetterQueueFull
- [ ] **S5-8** 超时后写入持久化 search_index_pending 表(DB 兜底)
- [ ] **S5-8** 单元测试 `DeadLetterQueue_Full_FallsBackToDb` + `DeadLetterQueue_Full_LogsAlert` 通过
- [ ] **S5-9** SignV2 签名内容: `mr1 + ":" + id + ":" + brandSortOrderMin + ":" + updatedAtTicks` (不含 expUnixTs)
- [ ] **S5-9** expUnixTs 作为 cursor 前缀明文: `cursor = base64(expUnixTs + "." + hmac签名)`
- [ ] **S5-9** 验签时 long.TryParse 防异常
- [ ] **S5-9** 单元测试 `SignV2_StableSignature` + `VerifyAndExtractV2_MalformedCursor_NoException` 通过
- [ ] **S5-10** oem_list_sort_order_min 计算时: `MIN(CASE WHEN b.is_deleted THEN NULL ELSE x.sort_order END)`
- [ ] **S5-10** 与 brand_sort_order_min 统一规则(软删除 brand 用 NULL)
- [ ] **S5-10** 单元测试 `SortOrderMin_SoftDeletedBrand_Null` + `SortOrderMin_ConsistentBetweenBrandAndOemList` 通过
- [ ] **S5-11** PG 短关键词匹配改用 `LOWER(oem_brand) = LOWER(@q)`
- [ ] **S5-11** Meilisearch 配置 matchingStrategy: last + 短关键词特殊处理
- [ ] **S5-11** 单元测试 `ShortKeyword_CaseInsensitive_MeiliPgConsistent` 通过
- [ ] **S5-12** 改用 BMP 私用区 U+E000/U+E001 (与 v5 spec 一致)
- [ ] **S5-12** 跨组件兼容性测试矩阵: Meilisearch 索引/查询 + PostgreSQL JSONB + .NET JSON 序列化 + 浏览器
- [ ] **S5-12** 单元测试 `PlaceholderBmp_CrossComponentCompatible` 通过
- [ ] **S5-13** 文档明确要求 Meilisearch 1.6+
- [ ] **S5-13** 降级方案: Meilisearch < 1.6 改用 HTML escape + 正则还原 <mark>
- [ ] **S5-13** 启动时检测 Meilisearch 版本,低于 1.6 走降级路径
- [ ] **S5-13** 单元测试 `PlaceholderBmp_Meili16_Supported` + `PlaceholderBmp_DegradedPath_OlderMeili` 通过
- [ ] **S5-14** 轮转窗口缩短为 24 小时(从 7 天)
- [ ] **S5-14** 文档明确: PreviousKey 必须与 CurrentKey 同等保护
- [ ] **S5-14** 单元测试 `CursorHmac_DualKey_RotationWindow` 通过
- [ ] **S5-15** search_index_pending 定期清理: 已处理且 updated_at < now() - 30 天的记录删除
- [ ] **S5-15** BuildBrandFilter AND 模式品牌数上限 20,超出抛 BRAND_FILTER_TOO_LONG
- [ ] **S5-15** 单元测试 `SearchIndexPending_Cleanup_30Days` + `BuildBrandFilter_TooManyBrands` 通过

### 三、前后端联动维度 v6 修复验证(14 项)

- [ ] **F4-1** mr1Suffix 也调用 BuildSlug 转义: `mr1Suffix = BuildSlug(mr1.Substring(Math.Max(0, mr1.Length - 6)))`
- [ ] **F4-1** 单元测试 `BuildProductUrl_Mr1Suffix_Escaped` 通过
- [ ] **F4-2** 实现 `TrimIncompletePercentEncoding(string s)`: 末尾 % 删除,末尾 %X (单 hex) 删除
- [ ] **F4-2** 单元测试 `BuildSlug_Truncate_PreservesPercentEncoding` + `BuildSlug_Truncate_RemovesIncompletePercent` 通过
- [ ] **F4-3** 改用 try-catch 404 fallback 实现 searchWithFallback 函数
- [ ] **F4-3** 单元测试 `AggregateApi_Fallback_On404` + `AggregateApi_Non404Error_Rethrown` 通过
- [ ] **F4-4** http.ts 改用动态 import: `const { default: router } = await import('@/router')`
- [ ] **F4-4** 单元测试 `Http_401_DynamicImportRouter_NoCircular` 通过
- [ ] **F4-5** BuildSlug 调整顺序: 先 EscapeDataString 再 lower (但只对非 %XX 部分 lower)
- [ ] **F4-5** %XX 中的 hex 字母保持大写,其他字母转小写
- [ ] **F4-5** 单元测试 `BuildSlug_PercentEncoding_UpperCase` + `BuildSlug_LowerCaseNonPercent` 通过
- [ ] **F4-6** 文档明确: crossorigin="use-credentials" 必须配合 SameSite=None; Secure
- [ ] **F4-6** appsettings.json 加 CookiePolicy 配置: SameSite=None, Secure=true
- [ ] **F4-6** Program.cs 加 app.UseCookiePolicy(... MinimumSameSitePolicy = SameSiteMode.None, Secure = CookieSecurePolicy.Always)
- [ ] **F4-6** 单元测试 `CookiePolicy_SameSiteNone_WithCrossOrigin` 通过
- [ ] **F4-7** error 处理器先检查 document.getElementById('app').children.length > 0,已挂载则跳过
- [ ] **F4-7** 检查 event.target 是否为 SCRIPT/LINK/IMG 标签
- [ ] **F4-7** 单元测试 `ErrorListener_SkipsMountedApp` + `ErrorListener_OnlyScriptLoad` 通过
- [ ] **F4-8** sessionStorage 写入用 try-catch 包裹,失败降级到内存 Map
- [ ] **F4-8** 实现 safeSessionStorage 工具方法
- [ ] **F4-8** 单元测试 `SessionStorage_SafariPrivateMode_FallbackToMemory` 通过
- [ ] **F4-9** 实现 captureException 类型适配层(接受 unknown,内部转换为 Error)
- [ ] **F4-9** 单元测试 `CaptureException_TypeAdapted` 通过
- [ ] **F4-10** 表单数据自动持久化到 localStorage (debounce 500ms)
- [ ] **F4-10** 409 时提示"是否恢复本地草稿?"
- [ ] **F4-10** 单元测试 `FormDraft_AutoSaveAndRestore` 通过
- [ ] **F4-11** 已在 F4-5 修复(BuildSlug 统一处理)
- [ ] **F4-12** mount-fallback 加去重标志: `if (window.__fallbackMounted) return;`
- [ ] **F4-12** 单元测试 `MountFallback_Dedup` 通过
- [ ] **F4-13** errorMonitor 加 init 状态标志 + 缓冲队列(最多 50 条)
- [ ] **F4-13** init 时把缓冲队列的事件 flush 到 Sentry
- [ ] **F4-13** 单元测试 `CaptureException_BeforeInit_Buffered` 通过
- [ ] **F4-14** router.replace 后 nextTick + 标志位 isRedirecting
- [ ] **F4-14** PublicSearchView watch 中检查标志位跳过
- [ ] **F4-14** 单元测试 `RouterReplace_NoUrlSyncLoop` 通过

---

## 33 个 v6 补丁任务验证清单

> v6 补丁任务逐项验证清单,按 Phase 0/1/3/4/5 分布

### Phase 0 v6 补丁任务验证(19 个)

- [ ] **Task 0.1.25** ProductDbContext.OnModelCreating 添加 FK 配置(CrossReference/MachineApplication/ProductImage 三个 HasOne + OnDelete Cascade)
- [ ] **Task 0.1.25** 导航属性 Product.CrossReferences/MachineApplications/Images 添加
- [ ] **Task 0.1.25** 单元测试 `ProductDbContext_FkConfiguration` 通过
- [ ] **Task 0.1.26** ETL TRUNCATE 改用 CASCADE 单条 SQL
- [ ] **Task 0.1.26** 删除旧的 DROP CONSTRAINT + ADD CONSTRAINT SQL
- [ ] **Task 0.1.26** 单元测试 `Truncate_CascadesToChildren` + `Fk_AddedAndDropped` 通过
- [ ] **Task 0.2.25** EF Core 迁移 AddForeignKeysV6 生成 + UP/DOWN 脚本正确
- [ ] **Task 0.2.25** `dotnet ef migrations script --idempotent` 无报错
- [ ] **Task 0.2.25** `psql \d cross_references` 显示 FK
- [ ] **Task 0.2.26** Product 类添加导航属性 ICollection<CrossReference>/MachineApplications/Images
- [ ] **Task 0.2.26** `dotnet build` 通过 + ModelSnapshot 与迁移一致
- [ ] **Task 0.3.21** AdminProductService advisory_xact_lock 紧接 BeginTransactionAsync 后执行
- [ ] **Task 0.3.21** lock 失败立即 RollbackAsync + 抛 XREF_CONFLICT + 日志 AdvisoryLockFailed
- [ ] **Task 0.3.21** 单元测试 `AdvisoryXactLock_BindsToTransaction` + `AdvisoryXactLock_Failure_RollsBack` 通过
- [ ] **Task 0.3.22** ReconcileService.cs 对账 SQL `WHERE p.mr_1 IS NOT NULL AND p.mr_1 <> m.mr_1`
- [ ] **Task 0.3.22** NULL 比例 > 5% 时告警
- [ ] **Task 0.3.22** 单元测试 `Reconcile_SkipsNullMr1` + `Reconcile_NullRatioAlert` 通过
- [ ] **Task 0.4.24** MeiliSearchProvider WriteTargets 改用 ImmutableArray + IOptionsMonitor.OnChange
- [ ] **Task 0.4.24** 配置变更时通过 CancellationToken 取消并重启 DeleteAsync
- [ ] **Task 0.4.24** 单元测试 `WriteTargets_HotSwap_NoException` 通过
- [ ] **Task 0.4.25** Mr1IndexDoc.brand_sort_order_min 类型从 int 改为 long?
- [ ] **Task 0.4.25** 全软删除时 brand_sort_order_min = NULL (而非 int.MaxValue)
- [ ] **Task 0.4.25** ORDER BY 显式声明 NULLS LAST
- [ ] **Task 0.4.25** 单元测试 `BuildMr1Doc_AllBrandSoftDeleted_NullsLast` + `BuildMr1Doc_PartialBrandSoftDeleted` 通过
- [ ] **Task 0.4.26** oem_list_sort_order_min 计算时 `MIN(CASE WHEN b.is_deleted THEN NULL ELSE x.sort_order END)`
- [ ] **Task 0.4.26** 单元测试 `SortOrderMin_SoftDeletedBrand_Null` + `SortOrderMin_ConsistentBetweenBrandAndOemList` 通过
- [ ] **Task 0.4.27** BuildSlug 方法头部加 WHY 注释
- [ ] **Task 0.4.27** 单元测试 `StripControlChars_Before_BuildSlug_Order` 通过
- [ ] **Task 0.4.28** SanitizeFormatted 步骤 0 过滤用户输入 \uFDD0/\uFDD1 字面量
- [ ] **Task 0.4.28** 步骤 3 还原逻辑改为 if-else (而非过滤)
- [ ] **Task 0.4.28** 步骤 4 还原后扫描残留 + 记日志 + 移除
- [ ] **Task 0.4.28** 改用 BMP 私用区 U+E000/U+E001
- [ ] **Task 0.4.28** 单元测试 `SanitizeFormatted_PreservesHighlight` + `SanitizeFormatted_StripsUserLiteralFDD0` + `PlaceholderBmp_CrossComponentCompatible` 通过
- [ ] **Task 0.4.29** PostgresSearchProvider keyset WHERE 条件改用显式 DESC + COALESCE 哨兵
- [ ] **Task 0.4.29** 单元测试 `Keyset_SecondPage_ReturnsCorrectRows` + `Keyset_DescDirection_Consistent` + `Keyset_NullBrandSortOrder_PaginatesCorrectly` 通过
- [ ] **Task 0.4.30** tokens.Take 前按 token 长度降序排序 + 停用词优先剔除
- [ ] **Task 0.4.30** 单元测试 `Tokens_Take_PreservesImportantTokens` + `Tokens_StopWordsFiltered` 通过
- [ ] **Task 0.4.31** 实现 `EscapeMeiliFilterValue` 工具方法
- [ ] **Task 0.4.31** BuildBrandFilter AND 模式品牌数上限 20 + 抛 BRAND_FILTER_TOO_LONG
- [ ] **Task 0.4.31** 单元测试 `BuildBrandFilter_EscapesQuote` + `BuildBrandFilter_EscapesBackslash` + `BuildBrandFilter_TooManyBrands` 通过
- [ ] **Task 0.4.32** OemBrandsStr 内部分隔符改用 \u0001 (SOH)
- [ ] **Task 0.4.32** Meilisearch separatorTokens 配置加入 \u0001
- [ ] **Task 0.4.32** 单元测试 `OemBrandsStr_SpaceInBrandName_Preserved` + `OemBrandsStr_SohSeparator_Tokenized` 通过
- [ ] **Task 0.4.33** Channel 容量限制为 10000
- [ ] **Task 0.4.33** 满时阻塞等待(超时 30 秒) + 日志告警 DeadLetterQueueFull + DB 兜底
- [ ] **Task 0.4.33** 单元测试 `DeadLetterQueue_Full_FallsBackToDb` + `DeadLetterQueue_Full_LogsAlert` 通过
- [ ] **Task 0.4.34** CursorHmac SignV2 签名内容不含 expUnixTs
- [ ] **Task 0.4.34** expUnixTs 作为 cursor 前缀明文 + long.TryParse 防异常
- [ ] **Task 0.4.34** 单元测试 `SignV2_StableSignature` + `VerifyAndExtractV2_MalformedCursor_NoException` 通过
- [ ] **Task 0.4.35** PG 短关键词匹配改用 `LOWER(oem_brand) = LOWER(@q)`
- [ ] **Task 0.4.35** 单元测试 `ShortKeyword_CaseInsensitive_MeiliPgConsistent` 通过
- [ ] **Task 0.4.36** 文档明确要求 Meilisearch 1.6+
- [ ] **Task 0.4.36** 启动时检测版本 + 低于 1.6 走降级路径
- [ ] **Task 0.4.36** 单元测试 `PlaceholderBmp_Meili16_Supported` + `PlaceholderBmp_DegradedPath_OlderMeili` 通过
- [ ] **Task 0.4.37** 双 key 轮转窗口缩短为 24 小时
- [ ] **Task 0.4.37** 单元测试 `CursorHmac_DualKey_RotationWindow` 通过
- [ ] **Task 0.4.38** search_index_pending 后台清理任务(每天凌晨 3 点,删除 30 天前已处理记录)
- [ ] **Task 0.4.38** 单元测试 `SearchIndexPending_Cleanup_30Days` 通过

### Phase 1 v6 补丁任务验证(3 个)

- [ ] **Task 1.3.9** AggregateSearchView.vue v-html 渲染前用 DOMPurify 白名单只允许 <mark>
- [ ] **Task 1.3.9** 单元测试 `Search_Aggregate_XssDefense` 通过
- [ ] **Task 1.3.10** html-sanitizer.ts DOMPurify 配置 ALLOWED_TAGS: ['mark'], ALLOWED_ATTR: []
- [ ] **Task 1.3.10** 输入预处理: 移除 \uFDD0/\uFDD1 字面量
- [ ] **Task 1.3.10** 单元测试 `HtmlSanitizer_StripsAllExceptMark` 通过
- [ ] **Task 1.3.11** searchWithFallback 函数实现 try-catch 404 fallback
- [ ] **Task 1.3.11** 单元测试 `AggregateApi_Fallback_On404` + `AggregateApi_Non404Error_Rethrown` 通过

### Phase 3 v6 补丁任务验证(1 个)

- [ ] **Task 3.2.13** CleanupOrphanImagesAsync 每个 IObjectStorage 用 try-catch 包裹
- [ ] **Task 3.2.13** 时间戳统一用 UTC + 列类型 timestamptz
- [ ] **Task 3.2.13** cleanup_failures 表记录失败存储后端
- [ ] **Task 3.2.13** 单元测试 `CleanupOrphanImages_PartialFailure_Continues` + `CleanupOrphanImages_UtcTimezone` 通过

### Phase 4 v6 补丁任务验证(7 个)

- [ ] **Task 4.1.21** BuildProductUrl mr1Suffix 调用 BuildSlug 转义
- [ ] **Task 4.1.21** 单元测试 `BuildProductUrl_Mr1Suffix_Escaped` 通过
- [ ] **Task 4.1.22** BuildSlug 调整顺序: 先 EscapeDataString 再 lower (保留 %XX 大写)
- [ ] **Task 4.1.22** 实现 TrimIncompletePercentEncoding
- [ ] **Task 4.1.22** 单元测试 `BuildSlug_PercentEncoding_UpperCase` + `BuildSlug_Truncate_PreservesPercentEncoding` + `BuildSlug_Truncate_RemovesIncompletePercent` 通过
- [ ] **Task 4.5.16** http.ts 401 处理改用动态 import router
- [ ] **Task 4.5.16** 单元测试 `Http_401_DynamicImportRouter_NoCircular` 通过
- [ ] **Task 4.5.17** Detail.cshtml crossorigin="use-credentials" + CookiePolicy SameSite=None; Secure
- [ ] **Task 4.5.17** 单元测试 `CookiePolicy_SameSiteNone_WithCrossOrigin` 通过
- [ ] **Task 4.5.18** error 事件处理器检查 #app 已挂载 + script 标签 + 去重标志
- [ ] **Task 4.5.18** 单元测试 `ErrorListener_SkipsMountedApp` + `ErrorListener_OnlyScriptLoad` + `MountFallback_Dedup` 通过
- [ ] **Task 4.5.19** safeSessionStorage.ts 工具方法 + Safari 隐私模式降级
- [ ] **Task 4.5.19** 单元测试 `SessionStorage_SafariPrivateMode_FallbackToMemory` 通过
- [ ] **Task 4.5.20** errorMonitor captureException 缓冲队列(最多 50 条) + 类型适配
- [ ] **Task 4.5.20** 单元测试 `CaptureException_BeforeInit_Buffered` + `CaptureException_TypeAdapted` 通过

### Phase 5 v6 补丁任务验证(3 个)

- [ ] **Task 5.1.23** LoadExistingOemMapAsync 检测 oem_2 多值返回 Dictionary<string, List<string>>
- [ ] **Task 5.1.23** 调用方拒绝 fallback + 记录 Oem2Ambiguous 告警 + 写入 import_skips 表
- [ ] **Task 5.1.23** oem_2 多值比例 > 1% 阻断 ETL 导入
- [ ] **Task 5.1.23** 单元测试 `LoadOemMap_Oem2Ambiguous_SkipsRecord` 通过
- [ ] **Task 5.1.24** import_skips 表创建(id/file_name/row_number/reason/created_at)
- [ ] **Task 5.1.24** 后台管理页面展示 import_skips 记录
- [ ] **Task 5.1.24** 单元测试 `ImportSkips_RecordOem2Ambiguous` 通过
- [ ] **Task 5.1.25** FormDraft 表单数据 localStorage 持久化(debounce 500ms)
- [ ] **Task 5.1.25** 409 时提示"是否恢复本地草稿?" + 草稿 7 天过期清理
- [ ] **Task 5.1.25** 单元测试 `FormDraft_AutoSaveAndRestore` 通过

---

## 第六轮深度审查验证点(v6 修复衍生风险)

> 第六轮审查将在 v6 修订完成后启动,验证 v6 修复方案是否产生新的衍生问题。
> 审查范围: 数据关联维度(D6)/检索逻辑维度(S6)/前后端联动维度(F5)

### 一、数据关联维度第六轮审查重点(10 个验证点)

- [ ] v6 advisory_xact_lock 通过 GetDbConnection().CreateCommand() 执行时,EF Core 是否复用同一事务连接(无新连接池借用)
- [ ] v6 IProductWriteStrategy 对账脚本过滤 NULL 后,是否遗漏 mr_1 IS NULL 但 meili 有值的场景
- [ ] v6 WriteTargets ImmutableArray + IOptionsMonitor.OnChange 配置变更时,正在进行的 DeleteAsync 是否被正确取消且无资源泄漏
- [ ] v6 CleanupOrphanImagesAsync try-catch 包裹后,失败的 IObjectStorage 是否在下次清理时优先重试且不重复清理已成功的
- [ ] v6 LoadExistingOemMapAsync oem_2 多值检测阈值 1% 是否合理(过高导致错误关联,过低导致 ETL 频繁阻断)
- [ ] v6 brand_sort_order_min 改 long? NULL 后,Meilisearch 索引是否支持 long? 类型 + 排序 NULLS LAST
- [ ] v6 ProductDbContext 添加 FK 配置后,EF Core 迁移是否会因已有数据违反 FK 而失败(需要先清理孤儿数据)
- [ ] v6 TRUNCATE CASCADE 单条 SQL 是否会因 FK 循环引用而失败(虽然当前无循环,但需验证)
- [ ] v6 StripControlChars 在 BuildSlug 之前调用时,是否影响 BuildSlug 的中文 EscapeDataString 编码
- [ ] v6 cleanup_failures 表的 retry_count 是否有上限(无限重试可能导致存储后端雪崩)

### 二、检索逻辑维度第六轮审查重点(10 个验证点)

- [ ] v6 SanitizeFormatted 步骤 0 过滤用户输入 \uFDD0/\uFDD1 字面量后,是否影响合法的高亮还原
- [ ] v6 BMP 私用区 U+E000/U+E001 在 Meilisearch 索引/查询/高亮全链路的兼容性
- [ ] v6 keyset 显式 DESC + COALESCE 哨兵后,PostgreSQL 14+ 是否正确使用索引(无全表扫描)
- [ ] v6 tokens.Take 按长度降序排序后,短但重要的 token(如品牌名缩写)是否被截断
- [ ] v6 EscapeMeiliFilterValue 转义后,Meilisearch filter 语法是否完整(无遗漏的特殊字符)
- [ ] v6 OemBrandsStr 改用 \u0001 分隔符后,Meilisearch separatorTokens 配置是否生效
- [ ] v6 Channel 容量 10000 + 超时 30 秒 + DB 兜底链路是否完整(无丢任务)
- [ ] v6 SignV2 expUnixTs 明文前缀是否会被客户端篡改(虽然不影响验签,但可能影响过期判断)
- [ ] v6 短关键词 LOWER 大小写不敏感后,PG 性能是否下降(无索引命中)
- [ ] v6 Meilisearch 1.6+ 版本检测 + 降级路径是否在启动时正确执行

### 三、前后端联动维度第六轮审查重点(10 个验证点)

- [ ] v6 BuildSlug %XX 大写 + TrimIncompletePercentEncoding 后,URL 反查 MR.1 是否正确
- [ ] v6 http.ts 动态 import router 在生产构建(Vite)中是否生成独立 chunk
- [ ] v6 CookiePolicy SameSite=None; Secure 在 HTTP 开发环境下是否导致 Cookie 不发送
- [ ] v6 error 事件处理器检查 #app.children.length 后,Vue 应用挂载延迟期间是否漏捕获错误
- [ ] v6 safeSessionStorage 内存降级后,页面刷新是否丢失数据(sessionStorage 通常用于会话内)
- [ ] v6 captureException 缓冲队列 50 条上限是否足够(高并发错误场景)
- [ ] v6 FormDraft localStorage 持久化是否在多标签页同时编辑时产生冲突
- [ ] v6 mount-fallback 去重标志 __fallbackMounted 是否在 SPA 路由切换时重置
- [ ] v6 router.replace isRedirecting 标志位在异步操作中是否有竞态条件
- [ ] v6 searchWithFallback try-catch 404 fallback 是否会掩盖真实的 404 错误(如 API 路径配置错误)

### 持续迭代验证点(每轮审查后追加)

> 每完成一轮审查 + 修复后,在此追加下一轮验证点
> 循环终止条件: 连续一轮审查无任何新漏洞检出

### 第七轮审查(待启动,验证 v7 修复衍生风险)

_待启动第七轮深度审查后追加验证点(详见下方"第七轮深度审查验证点")_

### 第八轮审查(暂无,待第七轮审查后追加)
_待启动第七轮深度审查后追加_

---

## v7 修订 33 项衍生漏洞 + 3 项 v6 事实性误判纠正验证清单

> 第七轮(即第六轮迭代)审查发现 33 个衍生漏洞 + 3 项 v6 事实性误判,本节为 v7 修复方案的逐项验证清单
> 验证范围: 误判纠正 3 项(E1~E3) + 数据关联 9 项(D6-1~D6-9) + 检索逻辑 13 项(S6-1~S6-13) + 前后端联动 11 项(F5-1~F5-11)

### 零、v6 事实性误判纠正验证(3 项)

- [ ] **E1** `SyncFkConfigurationsV7` 迁移为空 Up/Down,`dotnet ef migrations script --idempotent` 无 42710 错误
- [ ] **E1** `ProductDbContext.OnModelCreating` 含 3 个 HasOne Cascade 配置,ModelSnapshot 与 DB 现状一致
- [ ] **E1** `AddForeignKeysV6` 迁移文件已删除(若曾创建)
- [ ] **E2** `LoadExistingOem2MapAsync` 独立方法不影响现有 `LoadExistingOemMapAsync`(读 oem_no_normalized)
- [ ] **E2** ETL 流程调用 `LoadExistingOem2MapAsync`,oem_2 多值占比 > 1% 记录告警(不阻断)
- [ ] **E3** `PurgeAllAsync` 步骤 1 查询所有图片 URL + 步骤 2 批量删除对象存储图片
- [ ] **E3** TRUNCATE 显式列出所有业务表(避免依赖 CASCADE 隐式行为)
- [ ] **E3** 步骤 2 失败不阻断(记录警告,继续 TRUNCATE)
- [ ] **E3** `CleanupOrphanImagesService` 每周日凌晨 3 点扫描孤儿文件,> 100 触发告警

### 一、数据关联维度 v7 修复验证(9 项)

- [ ] **D6-1** `PurgeAll_ImagesDeleted` 集成测试通过(TRUNCATE 前先清理图片文件)
- [ ] **D6-1** `PurgeAll_StorageFailure_NoBlock` 集成测试通过(存储失败不阻断 TRUNCATE)
- [ ] **D6-2** `SyncFkConfigurationsV7` 迁移无 42710 错误,ModelSnapshot 含 HasOne
- [ ] **D6-3** `Etl_Oem2MultiValue_Detection` 单元测试通过(oem_2 多值占比 > 1% 告警)
- [ ] **D6-4** `Reconciliation_NullDrift_Detected` 集成测试通过(三维度 NULL 漂移检测)
- [ ] **D6-5** `Search_BrandSortOrder_NullLast` 单元测试通过(NULL 品牌排最末)
- [ ] **D6-5** `brand_sort_order_min_or_max` 冗余字段(NULL → long.MaxValue)
- [ ] **D6-6** `Cleanup_NoRepeat_Success` 单元测试通过(成功后 status='success' 不重试)
- [ ] **D6-6** `Cleanup_RetryLimit_Permanent` 单元测试通过(retry_count >= 5 标记永久失败)
- [ ] **D6-6** cleanup_failures 表含 status/cleaned_at/retry_count/last_error 字段 + chk_cleanup_status 约束
- [ ] **D6-6** 定期清理 status='success' AND cleaned_at < now() - INTERVAL '7 days'
- [ ] **D6-7** `Delete_Idempotent_404` 单元测试通过(404 异常不重试)
- [ ] **D6-7** `Cleanup_CheckExistence_First` 单元测试通过(重试前 GetDocumentAsync 检查)
- [ ] **D6-8** `StripControlChars_InvisibleChars` 单元测试通过(覆盖 9 个不可见字符)
- [ ] **D6-9** 永久失败任务数 > 0 触发告警 + 每日报告

### 二、检索逻辑维度 v7 修复验证(13 项)

- [ ] **S6-1** `SanitizeFormatted_XssDefense_E000Literal` 单元测试通过(用户输入 U+E000 字面量被过滤)
- [ ] **S6-1** 步骤 0 过滤 U+E000/U+E001(主)+ 兼容 \uFDD0/\uFDD1(历史)
- [ ] **S6-2** `Cursor_TamperedExpUnixTs_Rejected` 单元测试通过(expUnixTs 范围校验)
- [ ] **S6-2** `Cursor_MalformedExp_Rejected` 单元测试通过(long.TryParse 防异常)
- [ ] **S6-2** expUnixTs 范围:`now - 86400 <= expUnixTs <= now + 86400 * 7`
- [ ] **S6-3** `Search_Fallback_XssDefense` 单元测试通过(用户输入 `<mark>` 字面量被过滤)
- [ ] **S6-3** PG 降级用 `concat(U&E'\\uE000', field, U&E'\\uE001')` 占位符 + SanitizeFormatted 共用
- [ ] **S6-4** EXPLAIN ANALYZE 显示使用 `idx_products_keyset_v7`(非 Seq Scan)
- [ ] **S6-4** products 表含 brand_sort_order_min + oem_list_sort_order_min 冗余字段
- [ ] **S6-4** `UpdateProductRedundantFieldsAsync` 在 CreateAsync/UpdateAsync 末尾调用
- [ ] **S6-5** `Search_BrandWithSpace_Matched` 单元测试通过("BMW AG" 完整匹配)
- [ ] **S6-5** `oem_list_published_brands` 改数组类型,移除 separatorTokens 配置
- [ ] **S6-6** `DbFallback_NoResetRetryCount` 单元测试通过(retry_count 递增不重置)
- [ ] **S6-6** `DbFallback_PermanentFailure` 单元测试通过(retry_count >= 5 标记永久失败)
- [ ] **S6-7** `ChannelWrite_Timeout_DbFallback` 单元测试通过(5s 超时降级)
- [ ] **S6-7** appsettings.json 含 `MeiliSearch:ChannelWriteTimeoutSeconds: 5`
- [ ] **S6-8** `Search_ShortBrandAlias` 单元测试通过(短品牌缩写追加完整名)
- [ ] **S6-8** stopWords 配置 `["AG", "SA", "Co", "Ltd"]`
- [ ] **S6-9** `EscapeMeiliFilter_AllSpecialChars` 单元测试通过(覆盖 \\"/'/[/]/null)
- [ ] **S6-10** EXPLAIN ANALYZE 显示使用 `idx_products_name1_lower`(非 Seq Scan)
- [ ] **S6-10** 启用 pg_trgm 扩展 + GIN trigram 索引
- [ ] **S6-11** `JsonSerializer_PuaPreserved` 单元测试通过(U+E000/U+E001 不被转义)
- [ ] **S6-11** `AllowPuaJavaScriptEncoder` 注册到 `AddJsonOptions`
- [ ] **S6-12** `BuildDocument_PuaStripped` 单元测试通过(用户数据中 PUA 字符被过滤)
- [ ] **S6-12** `StripPua` 移除 BMP 私用区(U+E000~U+F8FF)字符
- [ ] **S6-13** `Startup_MeiliUnreachable_PgFallback` 集成测试通过(启动时降级)
- [ ] **S6-13** `HealthCheck_MeiliRecover_SwitchBack` 集成测试通过(恢复后切回)
- [ ] **S6-13** `MeiliHealthCheckService` BackgroundService 每 60s 重试连接
- [ ] **S6-13** ISearchProvider 含 `SetAvailability(bool)` + `IsMeiliAvailable` 属性

### 三、前后端联动维度 v7 修复验证(11 项)

- [ ] **F5-1** `Url_Mr1_CasePreserved` 集成测试通过(ABC123 → /products/.../ABC123 → 反查 ABC123)
- [ ] **F5-1** mr1Suffix 用 `encodeURIComponent` 保留大小写,不走 buildSlug
- [ ] **F5-1** 后端 OnGetAsync 用 `Uri.UnescapeDataString` + 大小写敏感查询
- [ ] **F5-2** `SafeStorage_SafariPrivateMode_ReadFromMemory` 单元测试通过
- [ ] **F5-2** 启动时检测 sessionStorage 可用性(try-catch setItem/removeItem 测试)
- [ ] **F5-2** safeGetItem sessionStorage 返回 null 时尝试 memoryStore
- [ ] **F5-2** safeSetItem 总是写入 memoryStore + 尝试写 sessionStorage
- [ ] **F5-3** `FormDraft_MultiTab_BroadcastSync` 单元测试通过(多标签页同步)
- [ ] **F5-3** `FormDraft_Expired_Cleanup` 单元测试通过(7 天 TTL 自动清理)
- [ ] **F5-3** 草稿 key 加 sessionId(UUID v4)
- [ ] **F5-3** BroadcastChannel 多标签同步
- [ ] **F5-4** `Http401_NoUrlSyncLoop` 单元测试通过
- [ ] **F5-4** handle401 同步设置 isRedirecting(在 router.replace 之前)
- [ ] **F5-4** router.replace.finally 延迟 1500ms 重置 isRedirecting
- [ ] **F5-4** PublicSearchView watch route.query 检查 isHttpRedirecting
- [ ] **F5-5** `CookiePolicy_Dev_HttpWorks` 集成测试通过(dev HTTP 接受)
- [ ] **F5-5** `CookiePolicy_Prod_HttpsOnly` 集成测试通过(prod 强制 HTTPS)
- [ ] **F5-5** Development 用 `CookieSecurePolicy.SameAsRequest` + `SameSiteMode.Lax`
- [ ] **F5-5** Production 用 `CookieSecurePolicy.Always` + `SameSiteMode.Strict`
- [ ] **F5-6** `ErrorEvent_RuntimeError_Captured` 单元测试通过
- [ ] **F5-6** error 事件区分资源加载错误和运行时错误(event.target null 检查)
- [ ] **F5-6** `unhandledrejection` 事件监听(Promise 异常)
- [ ] **F5-7** `FallbackMount_ResetOnRouteChange` 单元测试通过
- [ ] **F5-7** `router.afterEach` 延迟 1000ms 重置 `window.__fallbackMounted = false`
- [ ] **F5-8** `SearchFallback_404_ReportsError` 单元测试通过(console.error + Sentry)
- [ ] **F5-8** `SearchFallback_NoFallback_Throws` 单元测试通过(默认不降级直接抛错)
- [ ] **F5-8** .env.development 含 `VITE_ENABLE_LEGACY_FALLBACK=true`
- [ ] **F5-8** .env.production 含 `VITE_ENABLE_LEGACY_FALLBACK=false`
- [ ] **F5-9** `Http401_ReturnUrlPreserved` 单元测试通过(returnUrl 透传)
- [ ] **F5-9** `Http401_ChunkFailure_NativeRedirect` 单元测试通过(chunk 失败用 window.location.href)
- [ ] **F5-9** LoginView.vue 接收 returnUrl query,登录成功后跳转
- [ ] **F5-10** `CaptureException_Dedup` 单元测试通过(Map 去重)
- [ ] **F5-10** `CaptureException_BufferLimit` 单元测试通过(容量 50 + LRU)
- [ ] **F5-10** init 后 flush buffer + 30s 安全兜底 console.error
- [ ] **F5-11** `TrimPercentEncoding_InvalidUtf8_Truncated` 单元测试通过
- [ ] **F5-11** TrimIncompletePercentEncoding 验证剩余 %XX 序列构成有效 UTF-8
- [ ] **F5-11** `FindLastCompletePercent` 找到最后一个完整 %XX

## 27 个 v7 补丁任务验证清单

> v7 补丁任务逐项验证清单,按 Phase 0/1/3/4/5 分布

### Phase 0 v7 补丁任务验证(15 个)

- [ ] **Task 0.1.4** `SyncFkConfigurationsV7` 迁移:空 Up/Down + ModelSnapshot 含 HasOne + 无 42710
- [ ] **Task 0.1.5** `AddKeysetRedundantFieldsV7` 迁移:products 表含冗余字段 + `idx_products_keyset_v7` 索引
- [ ] **Task 0.1.6** `AddLowerExpressionIndexesV7` 迁移:LOWER 表达式索引 + trigram GIN 索引
- [ ] **Task 0.1.7** `AddCleanupFailuresStatusV7` 迁移:cleanup_failures 表含 status/cleaned_at/retry_count/last_error
- [ ] **Task 0.1.8** v7 对账脚本:FULL OUTER JOIN + 三维度 NULL 漂移检测
- [ ] **Task 0.3.7** `PurgeAllAsync`:4 步流程 + 事务回滚 + 存储失败不阻断
- [ ] **Task 0.3.8** `UpdateProductRedundantFieldsAsync`:CreateAsync/UpdateAsync 末尾调用
- [ ] **Task 0.4.12** `BuildMr1DocumentAsync` 数组字段改造:oem_list_published_brands 数组 + brand_sort_order_min_or_max + PUA 过滤 + 短品牌别名
- [ ] **Task 0.4.13** `SanitizeFormatted` 步骤 0 字符集统一:U+E000/U+E001 + 兼容 \uFDD0/\uFDD1
- [ ] **Task 0.4.14** `DeleteAsync` 幂等 404:MeilisearchApiException 捕获 + 文档存在性检查
- [ ] **Task 0.4.15** `ChannelWriteTimeout`:5s 超时 + DB 兜底 + IOptionsMonitor 动态调整
- [ ] **Task 0.4.16** `FallbackToDb` 不重置 retry_count:递增 + 永久失败标记
- [ ] **Task 0.4.17** PG 降级路径用 BMP PUA 占位符:`concat(U&E'\\uE000', field, U&E'\\uE001')`
- [ ] **Task 0.4.18** `VerifyAndExtractV2` long.TryParse + 范围校验
- [ ] **Task 0.4.19** `EscapeMeiliFilterValue` 补全转义:\\"/'/[/]/null
- [ ] **Task 0.4.20** `StripControlChars` 补全 9 个不可见字符
- [ ] **Task 0.4.21** `AllowPuaJavaScriptEncoder` 自定义编码器:允许 BMP PUA 原样输出

### Phase 1 v7 补丁任务验证(4 个)

- [ ] **Task 1.3.12** `html-sanitizer.ts` 步骤 0 字符集同步:U+E000/U+E001 + 兼容 \uFDD0/\uFDD1
- [ ] **Task 1.3.13** `safeStorage.ts` 重构:启动检测 + sessionStorage 返回 null 时 memoryStore
- [ ] **Task 1.3.14** `useFormDraft.ts` 多标签冲突修复:sessionId + BroadcastChannel + 7 天 TTL
- [ ] **Task 1.3.15** `errorMonitor.ts` captureException 去重:Map + LRU + 30s 兜底 flush

### Phase 3 v7 补丁任务验证(1 个)

- [ ] **Task 3.2.13.1** `CleanupOrphanImagesService` 定期孤儿清理:每周日 3 点 + > 100 告警

### Phase 4 v7 补丁任务验证(7 个)

- [ ] **Task 4.5.21** `buildProductUrl` mr1Suffix 直接 URL 编码:encodeURIComponent 保留大小写
- [ ] **Task 4.5.22** `handle401` 同步设置 isRedirecting:在 router.replace 之前 + 延迟重置
- [ ] **Task 4.5.23** `handle401` 保留 returnUrl + chunk 加载失败兜底:window.location.href
- [ ] **Task 4.5.24** `Program.cs` CookiePolicy 环境区分:dev SameAsRequest + prod Always
- [ ] **Task 4.5.25** `main.ts` error 事件捕获运行时错误 + unhandledrejection
- [ ] **Task 4.5.26** `main.ts` __fallbackMounted 路由切换重置:router.afterEach 延迟 1000ms
- [ ] **Task 4.5.27** `searchWithFallback` 404 上报 + 配置开关:console.error + Sentry + env 开关

### Phase 5 v7 补丁任务验证(2 个)

- [ ] **Task 5.1.26** `LoadExistingOem2MapAsync` 新增:独立方法 + oem_2 多值占比 > 1% 告警
- [ ] **Task 5.1.27** `Program.cs` 启动版本检测 + `MeiliHealthCheckService`:3s 超时 + 60s 重试

## 第七轮深度审查验证点(v7 修复衍生风险)

> 第七轮审查将在 v7 修订完成后启动,验证 v7 修复方案是否产生新的衍生问题。
> 审查范围: 数据关联维度(D7)/检索逻辑维度(S7)/前后端联动维度(F6)

### 一、数据关联维度第七轮审查重点(10 个验证点)

- [ ] v7 `SyncFkConfigurationsV7` 空迁移是否会导致 EF Core 模型与 DB 不一致(HasOne 配置但迁移无操作)
- [ ] v7 `PurgeAllAsync` 4 步流程非原子性(步骤 2 失败后步骤 3 TRUNCATE 仍执行),是否产生孤儿文件
- [ ] v7 `CleanupOrphanImagesService` 每周扫描时,如果对象存储 ListAllAsync 返回大量文件(> 10万),是否会 OOM 或超时
- [ ] v7 `LoadExistingOem2MapAsync` 查询 cross_references 全表,oem_2 字段无索引时是否会全表扫描
- [ ] v7 `UpdateProductRedundantFieldsAsync` 在 CreateAsync/UpdateAsync 末尾调用,是否会因 cross_references 未提交查不到数据(同事务内可见性)
- [ ] v7 对账脚本 FULL OUTER JOIN 在千万级数据量下是否会超时(需分批 + 索引)
- [ ] v7 cleanup_failures 状态机 in_progress 状态在服务崩溃时是否会卡死(无超时回收机制)
- [ ] v7 `brand_sort_order_min_or_max` 用 long.MaxValue 兜底,如果产品真实 brand_sort_order 接近 long.MaxValue 是否会冲突
- [ ] v7 `DeleteAsync` 404 异常捕获范围过宽,是否会掩盖真实的 Meilisearch API 错误(如索引不存在)
- [ ] v7 `StripControlChars` 过滤 U+00A0 (NBSP) 是否会影响合法的法语/芬兰语产品名

### 二、检索逻辑维度第七轮审查重点(10 个验证点)

- [ ] v7 `SanitizeFormatted` 步骤 0 过滤 U+E000/U+E001 后,如果用户产品名合法包含 PUA 字符(传统字体)是否丢失数据
- [ ] v7 `AllowPuaJavaScriptEncoder` 使用 `UnsafeRelaxedJsonEscaping` 作为 inner,是否会引入其他 XSS 风险(如 `<` `>` 不转义)
- [ ] v7 PG 降级路径用 `concat(U&E'\\uE000', field, U&E'\\uE001')` 包裹整个字段,如果字段本身含 U+E000 字面量(历史数据)是否会被误识别
- [ ] v7 keyset 复合表达式索引 `idx_products_keyset_v7` 在数据量 < 1000 时 PostgreSQL 是否会选 Index Scan(可能 Full Scan 更快)
- [ ] v7 `oem_list_published_brands` 改数组类型后,Meilisearch filter `IN [BMW]` 是否支持多值匹配
- [ ] v7 `FallbackToDb` 用 ExecuteUpdateAsync 批量更新,如果同 MR.1 + 同 IndexName 有多条记录,是否会全部更新(需唯一约束)
- [ ] v7 `ChannelWriteTimeout` 5s 超时,如果 Channel 容量从 500 改为 10000,5s 是否还合理
- [ ] v7 `EscapeMeiliFilterValue` 转义 `\\` 为 `\\\\`,但 Meilisearch filter 语法是否真的支持 `\\\\` 转义(需验证)
- [ ] v7 LOWER 表达式索引在 PostgreSQL 14+ 是否支持 INCLUDE 子句(覆盖索引优化)
- [ ] v7 `MeiliHealthCheckService` 60s 重试间隔,在 Meilisearch 短暂抖动(5s 不可达)时是否会误判降级

### 三、前后端联动维度第七轮审查重点(10 个验证点)

- [ ] v7 `buildProductUrl` mr1Suffix 用 encodeURIComponent,如果 MR.1 含 `/` 字符(虽然 CHK 约束禁止)是否会被编码为 %2F 影响路由匹配
- [ ] v7 `handle401` isRedirecting 延迟 1500ms 重置,如果用户在 1500ms 内主动登出再登录,是否会因 isRedirecting 仍 true 导致 401 处理被跳过
- [ ] v7 `handle401` chunk 加载失败用 `window.location.href`,如果 returnUrl 含 `#fragment` 是否会丢失 fragment
- [ ] v7 `CookiePolicy` dev 用 SameAsRequest,如果反向代理(nginx)将 HTTPS 终止为 HTTP,dev 模式是否仍能正常工作
- [ ] v7 `error` 事件捕获运行时错误,如果 Vue 组件内 try-catch 捕获错误未 rethrow,是否会漏捕获
- [ ] v7 `__fallbackMounted` 路由切换重置延迟 1000ms,如果用户在 1000ms 内快速点击导航,是否会重复 mount fallback
- [ ] v7 `searchWithFallback` 404 上报 Sentry,如果 Sentry 本身初始化失败,是否会引发二次错误
- [ ] v7 `useFormDraft` BroadcastChannel 在 IE/旧 Edge 不支持,是否会降级为 localStorage event
- [ ] v7 `safeStorage` 启动检测 sessionStorage 可用性,如果 sessionStorage 配额满(ItemFullError),后续写入是否会抛错
- [ ] v7 `TrimIncompletePercentEncoding` 验证 UTF-8 有效性,如果产品名含 4 字节 emoji(如 U+1F600),截断后是否会破坏代理对

### 持续迭代验证点(每轮审查后追加)

> 每完成一轮审查 + 修复后,在此追加下一轮验证点
> 循环终止条件: 连续一轮审查无任何新漏洞检出

### 第八轮审查(暂无,待第七轮审查后追加)
_待启动第七轮深度审查后追加_

---

# v8 修订验证清单(64 项衍生漏洞 + 24 项 v7 误判纠正 + 8 项前置任务 + 27 项 v8 任务)

> **修订时间**: 2026-07-17
> **触发原因**: 第七轮深度审查发现 64 项衍生漏洞 + v7 24 项高危事实性误判
> **核心改进**: 引入"代码现状对齐审计"(30 项硬性基线 C1-C55),所有修复方案基于真实代码

## 一、代码现状对齐审计验证(30 项硬性基线)

> 每项必须基于实际代码读取,确认 v8 修复方案引用的字段/方法/类型/表真实存在。

### 后端实体字段对齐(C1-C12)

- [ ] C1: Product.Id 类型为 `long`(Product.cs L10)
- [ ] C2: Product.Mr1 字段名 `Mr1`,列名 `mr_1`(Product.cs L22)
- [ ] C3: Product 软删除用 `IsDiscontinued`(L74) + `DiscontinuedAt`(L75),无 deleted_at
- [ ] C4: Product 无 SearchIndexPending 字段(独立实体)
- [ ] C5: Product 无 brand_sort_order_min_or_max 字段
- [ ] C6: CrossReference 无 Product 导航属性(仅 ProductId 外键)
- [ ] C7: CrossReference 无 IsPublished 字段
- [ ] C8: CrossReference 无 OemBrandId 字段(用 OemBrand 字符串)
- [ ] C9: CrossReference 无 SortOrder 字段
- [ ] C10: ProductImage 字段名 `ImageKey`(L106),非 ImageUrl
- [ ] C11: XrefOemBrand 字段名 `SortOrder`(列名 `sort_order`),非 brand_sort_order
- [ ] C12: XrefOemBrand 软删除用 `DeletedAt`(列名 `deleted_at`)

### 后端基础设施对齐(C13-C30)

- [ ] C13: ProductDbContext DbSet 名为 `XrefOemBrands`(L23)
- [ ] C14: InitialCreate 仅 3 个 CASCADE FK(cross_references/machine_applications/product_images)
- [ ] C15: cleanup_failures 表不存在(v8 Pre-Task-V8-1 创建前)
- [ ] C16: partition6_placeholder 表不存在
- [ ] C17: IObjectStorage 仅 5 方法(无 DeleteBatchAsync/ListAllAsync)
- [ ] C18: ISearchProvider 仅 4 方法(无 GetWriteTargets/DeleteAllDocumentsAsync)
- [ ] C19: MeiliSearchProvider 字段名 `_client`(L28)
- [ ] C20: MeiliSearchProvider 删除方法 `DeleteDocumentsAsync`(L137)
- [ ] C21: BuildMr1DocumentAsync 不存在
- [ ] C22: Mr1Document 类型不存在
- [ ] C23: Meili 索引文档类型为 `ProductIndexDoc`
- [ ] C24: EtlImportService 是 Singleton,无 _db 字段
- [ ] C25: LoadExistingOem2MapAsync 是 static 方法
- [ ] C26: CleanupOrphanImagesService 不存在(v8 Task V8-1.7 创建前)
- [ ] C27: CleanupFailure 实体不存在(v8 Pre-Task-V8-1 创建前)
- [ ] C28: Meilisearch SDK 版本 0.15.4
- [ ] C29: 认证方案仅 JWT Bearer,无 CookiePolicy
- [ ] C30: PublicProductController 路由 `/api/public/product/{slug}` 单段

### API 服务层对齐(C31-C43)

- [ ] C31: CursorHmac V1 三段 ISO8601(违反硬约束,v8 Task V8-4.5 修复)
- [ ] C32: ResilientSearchProvider Polly v8 配置(1s 超时 + 1 次重试 + 50% 熔断)
- [ ] C33: IndexReplayWorker MaxRetryCount=5,不用 Channel<T>
- [ ] C34: PostgresSearchProvider 用 EF.Functions.ILike + ESCAPE
- [ ] C35: MeiliHealthCheckService 不存在
- [ ] C36: HistoryCursorService 不存在
- [ ] C37: Mr1Controller/Mr1Service 不存在
- [ ] C38: System.Threading.Channels 全项目未使用
- [ ] C39: AllowPuaJavaScriptEncoder 不存在
- [ ] C40: EtlAlertService 文件在 `SakuraFilter.Api/Services/`
- [ ] C41: EtlAlertService 注释与代码不符(v8 E24 修复)
- [ ] C42: NpgsqlDataSource 全局未注册
- [ ] C43: ETL 公开端点无限流(v8 E25 修复)

### 前端代码对齐(C44-C55)

- [ ] C44: http.ts 用 refreshPromise,无 isRedirecting(v8 Task V8-4.2 修复)
- [ ] C45: errorMonitor.ts 自研,写 localStorage
- [ ] C46: LoginView 参数名 `redirect`,无开放重定向防护(v8 Task V8-4.1 修复)
- [ ] C47: 产品详情路由 `/product/:oem` 单段
- [ ] C48: 路由守卫 redirect 传递 to.fullPath
- [ ] C49: ErrorBoundary 写 sakura_error_log,未集成 errorMonitor(v8 Task V8-4.3 修复)
- [ ] C50: utils/url.ts 不存在(v8 Pre-Task-V8-4 创建)
- [ ] C51: utils/safeStorage.ts 不存在(v8 Pre-Task-V8-5 创建)
- [ ] C52: 产品详情视图 `views/public/PublicProductView.vue`
- [ ] C53: BroadcastChannel 全 frontend/src 未使用
- [ ] C54: package.json vue ^3.5.13 / vue-router ^4.5.0 / pinia ^2.3.0
- [ ] C55: Sentry 依赖未引入

## 二、v7 24 项高危事实性误判纠正验证(E4-E27)

- [ ] E4: CrossReference 用 `HasOne<Product>()` 无参重载,FK 名 `fk_cross_references_products_product_id`
- [ ] E5: TRUNCATE 列表仅 8 张真实表(动态白名单)
- [ ] E6: IObjectStorage 新增 ListAllAsync + DeleteBatchAsync
- [ ] E7: ProductImage 字段统一 `ImageKey`
- [ ] E8: Brand 排序下沉到 XrefOemBrand.SortOrder,IsPublished 改用 `!IsDiscontinued`
- [ ] E9: ProductIndexDoc 扩展(Mr1/BrandSortOrder/OemListPublishedBrands/OemListPublishedOem3s),新增 BuildProductIndexDocAsync
- [ ] E10: ISearchProvider 不扩展,直接用 DeleteAsync 批量删除
- [ ] E11: cleanup_failures 表 + CleanupFailure 实体 + 状态机创建
- [ ] E12: PG SQL 改为 `U&'\uE000'` 单反斜杠
- [ ] E13: Product 用 `is_discontinued = false`,XrefOemBrand 用 `deleted_at IS NULL`
- [ ] E14: 保持 /product/:oem 单段路由,4 段 URL 列为可选 Pre-Task-V8-3
- [ ] E15: 废弃 CookiePolicy 修复方案
- [ ] E16: LoginView 统一参数名 `redirect` + isSafeRedirect 防护
- [ ] E17: http.ts 新增 isRedirecting 防并发
- [ ] E18: ErrorBoundary 集成 errorMonitor,废弃 localStorage 直写
- [ ] E19: 新建 utils/url.ts + utils/safeStorage.ts
- [ ] E20: CursorHmac V2 Ticks + V1 兼容(硬约束修复)
- [ ] E21: IndexReplayWorker 保持 Task.Delay 轮询,废弃 Channel
- [ ] E22: 使用 UnsafeRelaxedJsonEscaping,不新建 AllowPuaJavaScriptEncoder
- [ ] E23: 新增 Mr1Validator 静态工具,不新建 Controller
- [ ] E24: EtlAlertService 显式排除 `status != "cancelled"`
- [ ] E25: EtlEndpoints.cs 应用 RequireRateLimiting("etl")
- [ ] E26: 不新建 HistoryCursorService,直接用 CursorHmac
- [ ] E27: 不新建 MeiliHealthCheckService,复用 ResilientSearchProvider

## 三、第七轮 64 项衍生漏洞修复验证

### 数据关联维度(D7-1 ~ D7-20)

- [ ] D7-1 [高]: CrossReference 用 HasOne<Product>() 无参重载(同 E4)
- [ ] D7-2 [高]: TRUNCATE 列表仅 8 张真实表(同 E5)
- [ ] D7-3 [高]: IObjectStorage 扩展(同 E6)
- [ ] D7-4 [高]: ProductImage 字段名 ImageKey(同 E7)
- [ ] D7-5 [高]: CrossReference 不引用 IsPublished/OemBrandId/SortOrder(同 E8)
- [ ] D7-6 [高]: BuildProductIndexDocAsync 替代 BuildMr1DocumentAsync(同 E9)
- [ ] D7-7 [高]: ISearchProvider 不扩展(同 E10)
- [ ] D7-8 [高]: cleanup_failures 表创建(同 E11)
- [ ] D7-9 [中]: LoadExistingOem2MapAsync static 调用方式,传 NpgsqlConnection
- [ ] D7-10 [中]: cleanup_failures in_progress 5min 超时回收
- [ ] D7-11 [中]: ListAllAsync 返回 IAsyncEnumerable,流式处理避免 OOM
- [ ] D7-12 [中]: 不引入 brand_sort_order_min_or_max,Brand 排序 JOIN xref_oem_brand
- [ ] D7-13 [中]: StripControlChars NBSP 可选过滤
- [ ] D7-14 [中]: UpdateProductRedundantFieldsAsync 拆分独立事务
- [ ] D7-15 [中]: TRUNCATE 动态白名单(同 E5)
- [ ] D7-16 [中]: E1 显式 HasOne 用 InitialCreate FK 名(同 E4)
- [ ] D7-17 [中]: EtlImportService Singleton 用 _sp.CreateScope
- [ ] D7-18 [低]: 合并 D7-15
- [ ] D7-19 [低]: 合并 D7-16
- [ ] D7-20 [低]: ProductImage 字段统一(同 E7)

### 检索逻辑维度(S7-1 ~ S7-22)

- [ ] S7-1 [高]: 步骤 0 仅在写入索引时过滤 PUA,搜索时不过滤
- [ ] S7-2 [中]: 保持 Meilisearch SDK 0.15.4,SDK 升级延后
- [ ] S7-3 [高]: PG SQL `U&'\uE000'` 单反斜杠(同 E12)
- [ ] S7-4 [高]: CrossReference 不引用不存在字段(同 E8)
- [ ] S7-5 [中]: stopWords 仅配置 "the"/"a"/"an",不误过滤品牌名
- [ ] S7-6 [中]: Meilisearch filter 仅转义 `\\` 和 `"`,不转义 `'`/`[`/`]`
- [ ] S7-7 [中]: IndexReplayWorker retry_count 字段核实,缺失则新增列
- [ ] S7-8 [高]: SearchIndexPending 字段按真实引用
- [ ] S7-9 [中]: 使用 UnsafeRelaxedJsonEscaping(同 E22)
- [ ] S7-10 [中]: 不新建 MeiliHealthCheckService(同 E27)
- [ ] S7-11 [中]: 不新增 FallbackToDb,直接 UPDATE search_index_pending
- [ ] S7-12 [低]: 不新增 SanitizeFormatted,保持现状
- [ ] S7-13 [中]: 废弃 Channel(同 E21)
- [ ] S7-14 [低]: 新增 MeiliFilterEscapeExtensions(Pre-Task-V8-8)
- [ ] S7-15 [中]: keyset 索引基于真实字段(is_discontinued, updated_at, id)
- [ ] S7-16 [中]: 数组字段索引配置确认 SDK 0.15.4 支持
- [ ] S7-17 [高]: SQL U&E 全部改为 U&'(同 E12)
- [ ] S7-18 [高]: deleted_at 引用统一(同 E13)
- [ ] S7-19 [中]: DB 兜底 retry_count + 1(同 S7-11)
- [ ] S7-20 [低]: 不配置 separatorTokens
- [ ] S7-21 [中]: DeleteDocumentsAsync 不捕获 404(天然幂等)
- [ ] S7-22 [中]: IndexReplayWorker retry_count >= 5 标记 is_dead

### 前后端联动维度(F6-1 ~ F6-22)

- [ ] F6-1 [高]: buildProductUrl 返回单段 URL(同 E14)
- [ ] F6-2 [高]: Mr1Validator 静态工具(同 E23)
- [ ] F6-3 [低]: FormDraft 24h TTL
- [ ] F6-4 [中]: isRedirecting 用 router.isReady().finally 释放
- [ ] F6-5 [高]: 合并 F6-3
- [ ] F6-6 [中]: 合并 F6-4
- [ ] F6-7 [高]: 废弃 CookiePolicy(同 E15)
- [ ] F6-8 [中]: BroadcastChannelCompat 降级 localStorage storage
- [ ] F6-9 [中]: ErrorBoundary 集成 errorMonitor(同 E18)
- [ ] F6-10 [中]: errorMonitor 过滤 AbortError
- [ ] F6-11 [中]: 多标签页同步用 BroadcastChannelCompat
- [ ] F6-12 [高]: 保留自研 errorMonitor(同 E18)
- [ ] F6-13 [中]: initMonitor 不调用 setTimeout
- [ ] F6-14 [低]: 合并 F6-11
- [ ] F6-15 [中]: errorMonitor 绑定 window.onerror + unhandledrejection
- [ ] F6-16 [高]: 统一参数名 redirect(同 E16)
- [ ] F6-17 [高]: isSafeRedirect 防护(同 E16)
- [ ] F6-18 [中]: 合并 F6-13
- [ ] F6-19 [高]: 合并 F6-13
- [ ] F6-20 [高]: 新建 url.ts(同 E19)
- [ ] F6-21 [中]: 新建 safeStorage.ts(同 E19)
- [ ] F6-22 [中]: 合并 F6-11

## 四、27 个 v8 任务验证清单

### Phase 0: 前置任务验证(8 个)

#### Pre-Task-V8-1: cleanup_failures 表 + CleanupFailure 实体
- [ ] CleanupFailure.cs 实体创建,字段完整(Id/FileKey/Backend/FailureType/ErrorMessage/RetryCount/Status/LastAttemptAt/NextRetryAt/CreatedAt/UpdatedAt)
- [ ] CleanupFailureConfiguration.cs EF 配置创建,ToTable("cleanup_failures")
- [ ] 迁移文件创建,DDL 含 2 个索引(idx_cleanup_failures_status_next_retry / idx_cleanup_failures_file_key)
- [ ] ProductDbContext 注册 DbSet<CleanupFailure> CleanupFailures
- [ ] `dotnet ef migrations has-pending-model-changes` 无 diff
- [ ] `dotnet ef database update` 成功
- [ ] `SELECT 1 FROM cleanup_failures LIMIT 1` 成功

#### Pre-Task-V8-2: IObjectStorage 接口扩展
- [ ] IObjectStorage.cs 新增 ListAllAsync + DeleteBatchAsync 方法签名
- [ ] MinioStorage.cs 实现 ListAllAsync(IAsyncEnumerable 迭代器)
- [ ] MinioStorage.cs 实现 DeleteBatchAsync(1000 条/批)
- [ ] AliyunOssStorage.cs 实现 ListAllAsync
- [ ] AliyunOssStorage.cs 实现 DeleteBatchAsync
- [ ] 单元测试 `MinioStorage_ListAll_Pagination` 通过
- [ ] 单元测试 `MinioStorage_DeleteBatch_Idempotent` 通过(404 静默)
- [ ] 单元测试 `AliyunOssStorage_ListAll_Pagination` 通过
- [ ] 单元测试 `AliyunOssStorage_DeleteBatch_Idempotent` 通过

#### Pre-Task-V8-3: SEO 多段 URL 独立路由(可选)
- [ ] 新增路由 `/products/:pn1/:pn2/:brand/:mr1Suffix`
- [ ] PublicProductView.vue 兼容两种路由参数
- [ ] 现有 `/product/:oem` 路由仍可访问
- [ ] 新路由解析正确

#### Pre-Task-V8-4: utils/url.ts 创建
- [ ] buildProductUrl 实现并返回单段 URL
- [ ] getProductSlugFromRoute 实现
- [ ] isSafeRedirect 实现(拒绝 `//evil.com`)
- [ ] LoginView.vue 重构使用 isSafeRedirect
- [ ] http.ts redirectToLogin 重构使用 isSafeRedirect
- [ ] 单元测试 `isSafeRedirect_RejectsExternalUrl` 通过
- [ ] 单元测试 `isSafeRedirect_RejectsProtocolRelative` 通过
- [ ] 单元测试 `buildProductUrl_EncodesSpecialChars` 通过

#### Pre-Task-V8-5: utils/safeStorage.ts 创建
- [ ] safeLocalStorage 实现(getItem/setItem/removeItem,try-catch)
- [ ] safeSessionStorage 实现
- [ ] quota exceeded 静默失败返回 false
- [ ] errorMonitor.ts 重构使用 safeLocalStorage
- [ ] ErrorBoundary.vue 重构使用 safeLocalStorage
- [ ] 单元测试 `safeLocalStorage_HandlesQuotaExceeded` 通过
- [ ] 单元测试 `safeLocalStorage_GetItemReturnsNullOnException` 通过

#### Pre-Task-V8-6: Mr1Validator 静态工具
- [ ] Mr1Validator.cs 创建,实现 IsValid
- [ ] 长度校验 10 位
- [ ] 字符集校验 0-9 A-Z
- [ ] CHK 校验位算法(前 9 位加权求和取模 36)
- [ ] ETL 导入时调用 IsValid,失败记录错误日志
- [ ] Admin 创建/编辑产品时调用 IsValid,失败返回 400
- [ ] 单元测试 `Mr1Validator_ValidChk` 通过
- [ ] 单元测试 `Mr1Validator_InvalidLength` 通过
- [ ] 单元测试 `Mr1Validator_InvalidCharset` 通过
- [ ] 单元测试 `Mr1Validator_InvalidChk` 通过
- [ ] 单元测试 `Mr1Validator_NullOrEmpty` 通过

#### Pre-Task-V8-7: Meilisearch SDK 升级(延后)
- [ ] 评估升级风险
- [ ] v8 不强制执行,列入 v9 评估

#### Pre-Task-V8-8: MeiliFilterEscapeExtensions
- [ ] MeiliFilterEscapeExtensions.cs 创建
- [ ] EscapeMeiliFilterValue 实现(仅转义 `\\` 和 `"`,包裹双引号)
- [ ] MeiliSearchProvider.SearchAsync 使用此方法
- [ ] 单元测试 `EscapeMeiliFilter_Backslash` 通过
- [ ] 单元测试 `EscapeMeiliFilter_DoubleQuote` 通过
- [ ] 单元测试 `EscapeMeiliFilter_EmptyString` 通过
- [ ] 单元测试 `EscapeMeiliFilter_NormalString` 通过

### Phase 1: 数据关联修复任务验证(7 个)

#### Task V8-1.1: CrossReference FK 配置修正
- [ ] CrossReferenceConfiguration.cs 改用 `HasOne<Product>()` 无参重载
- [ ] HasConstraintName 与 InitialCreate 一致
- [ ] OnDelete(Cascade)
- [ ] `dotnet ef migrations has-pending-model-changes` 无 diff
- [ ] 现有 FK 不被重建

#### Task V8-1.2: TRUNCATE 列表修正
- [ ] EtlImportService.cs ResetAllDataAsync 改用动态白名单 SQL
- [ ] 移除对 cleanup_failures / partition6_placeholder 的硬编码引用
- [ ] 全量重置后 9 张表(含 cleanup_failures)均清空
- [ ] 不存在的表不报错

#### Task V8-1.3: ProductImage 字段名统一
- [ ] grep `pi\.ImageUrl` / `image\.ImageUrl` 全部改为 `ImageKey`
- [ ] `dotnet build` 无错误
- [ ] 单元测试 `ProductImage_ImageKey_FieldName` 通过

#### Task V8-1.4: CrossReference 字段引用修正
- [ ] grep `IsPublished`/`OemBrandId`/`SortOrder` 引用 CrossReference 处全部移除
- [ ] Brand 排序改用 JOIN xref_oem_brand
- [ ] IsPublished 改用 `!IsDiscontinued`
- [ ] `dotnet build` 无错误
- [ ] 搜索结果 Brand 排序正确

#### Task V8-1.5: ProductIndexDoc 扩展 + BuildProductIndexDocAsync
- [ ] ProductIndexDoc 新增字段:Mr1/BrandSortOrder/OemListPublishedBrands/OemListPublishedOem3s
- [ ] BuildProductIndexDocAsync 方法实现
- [ ] `dotnet build` 无错误
- [ ] 单元测试 `BuildProductIndexDocAsync_IncludesMr1AndBrandSort` 通过
- [ ] 单元测试 `BuildProductIndexDocAsync_NullBrandSortDefaultsToMax` 通过

#### Task V8-1.6: EtlImportService 调用方式修正
- [ ] LoadExistingOem2MapAsync 调用改为传 NpgsqlConnection
- [ ] ProductDbContext 调用改为 _sp.CreateScope()
- [ ] `dotnet build` 无错误
- [ ] ETL 全量导入测试通过

#### Task V8-1.7: CleanupOrphanImagesService 创建
- [ ] CleanupOrphanImagesService.cs 创建(BackgroundService)
- [ ] 5min 超时回收逻辑实现
- [ ] 孤儿文件检测实现(对比 product_images.image_key 与 ListAllAsync)
- [ ] 失败记录到 cleanup_failures 表
- [ ] 注册到 ServiceCollectionExtensions.AddHostedServices
- [ ] 单元测试 `CleanupOrphanImages_DetectsOrphans` 通过
- [ ] 单元测试 `CleanupFailures_StuckInProgressReset` 通过
- [ ] 单元测试 `CleanupFailures_RetryAfter5Min` 通过

### Phase 3: 检索逻辑修复任务验证(6 个)

#### Task V8-3.1: PG SQL Unicode 转义语法修正
- [ ] grep `U&E'\\\\uE000'` 全部改为 `U&'\uE000'`
- [ ] grep `U&E'\\\\uE001'` 全部改为 `U&'\uE001'`
- [ ] `dotnet build` 无错误
- [ ] 集成测试 `PgHighlight_UnicodeEscape_SingleBackslash` 通过

#### Task V8-3.2: Product 软删除字段统一
- [ ] grep `deleted_at IS NULL` 在 Product 查询中改为 `is_discontinued = false`
- [ ] XrefOemBrand 查询保持 `deleted_at IS NULL`
- [ ] `dotnet build` 无错误
- [ ] 单元测试 `ProductQuery_FiltersByIsDiscontinued` 通过

#### Task V8-3.3: IndexReplayWorker 死信判定 + 字段补充
- [ ] SearchIndexPending 实体字段核实
- [ ] 若无 retry_count/is_dead/last_error,新增迁移
- [ ] IndexReplayWorker 失败时 UPDATE retry_count = retry_count + 1
- [ ] retry_count >= 5 时标记 is_dead = true
- [ ] 单元测试 `IndexReplayWorker_RetryCountIncrement` 通过
- [ ] 单元测试 `IndexReplayWorker_MarkDeadAfter5Retries` 通过
- [ ] 集成测试 `SearchIndexPending_RetryCountFieldExists` 通过

#### Task V8-3.4: Meilisearch filter 转义修正
- [ ] 移除对 `'`/`[`/`]` 的转义
- [ ] 统一使用 Pre-Task-V8-8 的 EscapeMeiliFilterValue
- [ ] 单元测试 `MeiliFilter_NoSingleQuoteEscape` 通过
- [ ] 单元测试 `MeiliFilter_NoBracketEscape` 通过

#### Task V8-3.5: stopWords 配置修正
- [ ] 移除 stopWords 配置(或仅配置 "the"/"a"/"an")
- [ ] 添加 synonyms 配置(品牌名缩写映射)
- [ ] 单元测试 `Search_BrandWithSpace_Matched` 通过
- [ ] 单元测试 `Search_NoStopWordFiltering` 通过

#### Task V8-3.6: JavaScriptEncoder 全局配置
- [ ] Program.cs 注册全局 JsonSerializerOptions
- [ ] 使用 UnsafeRelaxedJsonEscaping
- [ ] PropertyNamingPolicy = CamelCase
- [ ] 移除 AllowPuaJavaScriptEncoder 引用(若存在)
- [ ] 集成测试 `ApiResponse_PuaCharsNotEscaped` 通过
- [ ] 集成测试 `ApiResponse_CamelCaseNaming` 通过

### Phase 4: 前后端联动修复任务验证(6 个)

#### Task V8-4.1: LoginView 开放重定向防护
- [ ] 引入 isSafeRedirect
- [ ] L46-47 改用 safeRedirect
- [ ] 单元测试 `LoginView_RejectsExternalRedirect` 通过
- [ ] 单元测试 `LoginView_AcceptsInternalRedirect` 通过

#### Task V8-4.2: http.ts isRedirecting 防并发
- [ ] 顶部新增 `let isRedirecting = false`
- [ ] redirectToLogin 加入并发锁
- [ ] 释放改为 router.isReady().finally
- [ ] 单元测试 `Http_NoConcurrentRedirect` 通过
- [ ] 单元测试 `Http_isRedirectingReleasesAfterRouteChange` 通过

#### Task V8-4.3: ErrorBoundary 集成 errorMonitor
- [ ] 移除 localStorage.setItem('sakura_error_log', ...) 代码
- [ ] 引入 captureException
- [ ] onErrorCaptured 内调用 captureException
- [ ] 单元测试 `ErrorBoundary_WritesToErrorMonitor` 通过
- [ ] 单元测试 `ErrorBoundary_NoLocalStorageDirectWrite` 通过

#### Task V8-4.4: errorMonitor AbortError 过滤 + window 事件绑定
- [ ] initMonitor 内绑定 window.onerror
- [ ] initMonitor 内绑定 unhandledrejection,过滤 AbortError
- [ ] initMonitor 内不调用 setTimeout
- [ ] shutdownMonitor 解绑 window 事件监听
- [ ] 单元测试 `ErrorMonitor_FiltersAbortError` 通过
- [ ] 单元测试 `ErrorMonitor_CapturesWindowError` 通过
- [ ] 单元测试 `ErrorMonitor_CapturesUnhandledRejection` 通过
- [ ] 单元测试 `ErrorMonitor_NoSetTimeoutInInit` 通过

#### Task V8-4.5: CursorHmac V2 Ticks 格式 + V1 兼容
- [ ] 新增 Sign(long ticks, long id) 方法
- [ ] VerifyAndExtract 支持 V2 优先 + V1 兼容
- [ ] 所有调用方改为传 ticks
- [ ] 旧 V1 cursor 仍可验证
- [ ] 单元测试 `CursorHmac_V2SignAndVerify` 通过
- [ ] 单元测试 `CursorHmac_V1BackwardCompat` 通过
- [ ] 单元测试 `CursorHmac_TicksFormat` 通过
- [ ] 单元测试 `CursorHmac_RejectsTamperedTicks` 通过

#### Task V8-4.6: BroadcastChannelCompat 多标签页同步
- [ ] BroadcastChannelCompat 类实现
- [ ] 降级到 localStorage storage 事件
- [ ] 应用到 auth-logout 频道
- [ ] 应用到 form-draft 频道(可选)
- [ ] 单元测试 `BroadcastChannelCompat_PostMessageWithChannel` 通过
- [ ] 单元测试 `BroadcastChannelCompat_FallbackToStorage` 通过
- [ ] 单元测试 `BroadcastChannelCompat_MultiTabLogoutSync` 通过

## 五、第八轮深度审查验证点

> 第八轮审查将验证 v8 修复方案是否引入新的衍生问题。审查维度:
> 1. 代码现状对齐审计 30 项基线是否准确
> 2. v7 24 项高危误判纠正是否引入新问题
> 3. 第七轮 64 项衍生漏洞修复方案是否完整
> 4. 8 项前置任务依赖链是否合理
> 5. 27 项 v8 任务是否覆盖所有漏洞
> 6. v8 修复方案是否再次"凭空假设"代码状态
> 7. v8 是否引入新的字段/方法/类型引用错误
> 8. v8 是否引入新的 SQL 语法错误
> 9. v8 是否引入新的 API 契约破坏
> 10. v8 是否引入新的硬约束违反

### 数据关联维度(D8)验证点(10 个)
- [ ] D8-1: v8 代码现状对齐审计 C1-C30 是否准确反映真实代码
- [ ] D8-2: E4 HasOne<Product>() 无参重载是否与 InitialCreate 产生模型 diff
- [ ] D8-3: E5 TRUNCATE 动态白名单是否漏列真实存在的表
- [ ] D8-4: E6 IObjectStorage 扩展方法在 MinIO/Aliyun OSS 实现是否正确
- [ ] D8-5: E8 Brand 排序 JOIN xref_oem_brand 是否引入 N+1 查询
- [ ] D8-6: E9 BuildProductIndexDocAsync 是否正确查询 XrefOemBrands
- [ ] D8-7: E11 cleanup_failures 状态机转换是否完整(pending→in_progress→success/failed)
- [ ] D8-8: D7-9 LoadExistingOem2MapAsync static 调用方式是否正确
- [ ] D8-9: D7-11 IAsyncEnumerable 在 0.15.4 SDK 兼容性
- [ ] D8-10: D7-17 EtlImportService Singleton 调用 Scoped 服务是否正确释放

### 检索逻辑维度(S8)验证点(10 个)
- [ ] S8-1: E12 PG SQL `U&'\uE000'` 语法在 PostgreSQL 16 是否正确
- [ ] S8-2: E13 Product is_discontinued 与 XrefOemBrand deleted_at 区分是否准确
- [ ] S8-3: S7-1 PUA 过滤时机(写入索引 vs 搜索)是否正确
- [ ] S8-4: S7-6 Meilisearch filter 转义规则是否与官方文档一致
- [ ] S8-5: S7-7 SearchIndexPending 实体真实字段名是否核实
- [ ] S8-6: S7-15 keyset 索引字段(is_discontinued, updated_at, id)是否真实存在
- [ ] S8-7: S7-16 数组字段索引在 Meilisearch 0.15.4 是否支持
- [ ] S8-8: E22 UnsafeRelaxedJsonEscaping 是否真的允许 PUA 字符
- [ ] S8-9: Task V8-3.3 retry_count 字段迁移是否与现有 schema 冲突
- [ ] S8-10: Task V8-3.5 synonyms 配置是否影响现有搜索行为

### 前后端联动维度(F7)验证点(10 个)
- [ ] F7-1: E14 保持单段路由是否影响 SEO 方案
- [ ] F7-2: E16 isSafeRedirect 正则 `^\/[^/].*` 是否正确
- [ ] F7-3: E17 isRedirecting router.isReady().finally 是否在所有浏览器支持
- [ ] F7-4: E18 ErrorBoundary 集成 errorMonitor 是否丢失原 sakura_error_log 数据
- [ ] F7-5: E19 url.ts isSafeRedirect 是否覆盖所有开放重定向场景
- [ ] F7-6: E20 CursorHmac V2 Ticks 与 V1 ISO8601 兼容期是否引入安全风险
- [ ] F7-7: E23 Mr1Validator CHK 算法(加权求和取模 36)是否符合业务规则
- [ ] F7-8: E24 EtlAlertService `&& l.Status != "cancelled"` 是否冗余(隐式已排除)
- [ ] F7-9: F6-8 BroadcastChannelCompat 在 IE/旧 Edge 降级是否正确
- [ ] F7-10: F6-10 AbortError 过滤是否漏过滤其他非错误 rejection

## 六、循环终止条件

- [ ] 第八轮审查无任何新漏洞检出 → 完成
- [ ] 第八轮审查发现新漏洞 → 进入 v9 修订,继续迭代

