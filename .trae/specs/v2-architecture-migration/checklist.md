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

## 第二轮深度审查验证点(待启动)

> 启动 3 个并行子代理(数据/检索/联动)对 v2 修复后再次深度审查
> 验证目标: 修复后是否产生衍生问题、是否有遗漏场景

### 数据关联维度第二轮审查
- [ ] product_images 旧约束 DROP 后,历史数据是否有依赖该约束的业务代码(残留 SELECT)
- [ ] oem_no_normalized DROP NOT NULL 后,是否有 NOT NULL 校验残留代码
- [ ] cross_references 加 oem_2 后,是否有 OEM 2 在 products 表的残留读写
- [ ] system_settings ON CONFLICT 改造后,旧 INSERT 单条逻辑是否兼容
- [ ] partition6_placeholder 注册后,是否误进入 Meilisearch 索引构建逻辑
- [ ] mr_1 部分唯一索引(WHERE mr_1 IS NOT NULL)与 NULL 多行共存的边界
- [ ] cross_references.xmin 乐观锁在 ETL 全量导入场景下的行为(大批量更新冲突)

### 检索逻辑维度第二轮审查
- [ ] Meilisearch 嵌套文档 oem_list 排序后,搜索结果展示顺序是否一致
- [ ] LATERAL JOIN 兜底在 1M 数据量下的查询计划(EXPLAIN ANALYZE)
- [ ] filterableAttributes 补全后,Meilisearch 索引大小是否膨胀超阈值
- [ ] _formatted HTML escape 后,中文高亮是否仍正常(mark 标签位置正确)
- [ ] brand_sort_order_min 冗余字段在 OEM 3 上下架切换时是否及时更新
- [ ] cursor 分页 page > 100 抛错后,前端是否有友好提示
- [ ] typoTolerance minWordSizeForTypos=4 配置后,3 字短词(如"BMW")搜索是否失效

### 前后端联动维度第二轮审查
- [ ] nginx 路由 /products/ 与 SPA /products/:pn1/:pn2/:brand/:oem3 路由是否冲突
- [ ] Vue client mount 在 SSR HTML 含 `<mark>` 高亮场景下的渲染(画廊组件是否冲突)
- [ ] ProblemDetailsFactory 旧 ERR_* 映射的覆盖范围(是否有遗漏错误码)
- [ ] AdminProductImageService 签名改造后,旧单测是否全部更新
- [ ] CursorHmac string MR.1 改造后,旧 cursor 客户端兼容性
- [ ] RateLimit "public" 策略对 SEO 爬虫(Googlebot)是否误伤
- [ ] sitemap 缓存失效后,首次请求的响应时间(缓存击穿防护)
- [ ] product-detail-client.js defer 加载顺序(依赖 Vue 全局变量时)

---

## 持续迭代验证点(每轮审查后追加)

> 每完成一轮审查 + 修复后,在此追加下一轮验证点
> 循环终止条件: 连续一轮审查无任何新漏洞检出

### 第 N-1 轮(暂无,待审查后追加)
_待启动第二轮深度审查后追加_

### 第 N 轮(暂无,待审查后追加)
_待启动第二轮深度审查后追加_
