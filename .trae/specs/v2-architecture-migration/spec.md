# SakuraFilter V2 架构迁移与 5 项客户需求落地 Spec

> 配套文件: `tasks.md` (有序任务清单) + `checklist.md` (验证检查点)
> 本 spec 基于 `项目规划V2.docx` (目标架构) + 客户 2026-07 线下沟通 5 项新需求 + 4 项确认点
> **本阶段只生成规划文档,不写业务代码**

---

## Why (背景与动机)

当前代码实现以 OEM 2 为产品主键(`products.oem_no_normalized` UNIQUE),与 `项目规划V2.docx` 规定的"MR.1 为内部主键、OEM 3 为对外展示主键"架构存在根本性偏差。客户在 2026-07 线下沟通中提出 5 项新需求,其中 3 项(OEM 3 优先排序、图片按 OEM 3 命名、聚合搜索)直接依赖 MR.1 主键化架构,2 项(MR.1 长度扩展、SEO 域名)为新增能力。本次改造需先补齐 MR.1 主键化前置架构,再按阶段落地 5 项需求,同时清空旧测试数据、按 V2 设计导入新模拟数据。

### SEO 技术选型决策(已定)

| 方案 | 改造量 | SEO 效果 | 维护成本 | 决策 |
|---|---|---|---|---|
| A. Nuxt 3 迁移 | 大(前端框架替换) | 最佳 | 高 | ❌ 不选 |
| B. vite-ssg 预渲染 | 中 | 好(热门产品) | 中 | ❌ 不选(1M 产品全量预渲染不现实) |
| **C. ASP.NET Razor + Vue 局部 hydration** | **中** | **最佳** | **中** | **✅ 选定** |

**选 C 的核心理由**:
1. 后端已是 ASP.NET Core 8,Razor Pages 零框架迁移成本
2. 复用现有 `ProductDbContext` + `AdminProductService`,无需重写数据层
3. 产品详情页内容相对静态(参数 + 适配机型),Razor 服务端渲染对 SEO 最友好(无需等 JS hydration)
4. 图片画廊/对比按钮等交互部分用 Vue 组件局部 hydration,保留现有 Vue 3 代码资产
5. 搜索页/对比页/管理后台保留纯 SPA,改动面最小

---

## What Changes (改动清单)

### 架构层改动

* **BREAKING**: `products` 表主键语义从 OEM 2 改为 MR.1。`oem_no_normalized` 字段保留但语义降级为"OEM 2 归一化值",新增 `mr_1` UNIQUE 约束作为内部主键
* **BREAKING**: `cross_references` 表 `oem_no_3` 字段升级为对外展示主键,新增 `sort_order` 字段支撑同 Brand 内优先级
* **BREAKING**: `product_images` 表新增 `oem_no_3` + `image_role` 字段,主图按 OEM 3 命名,详情图按 MR.1 共享
* **BREAKING**: Meilisearch 索引主键从 `oem_no_normalized` 改为 `mr_1`,文档结构改为嵌套(OEM 列表 + 机型列表 + 参数)
* **BREAKING**: 旧数据全部清空,按 V2 设计导入新模拟数据
* **BREAKING**: 公开产品详情 URL 从 `/product/:oem` 改为 SEO 友好结构 `/products/:pn1/:pn2/:brand/:oem3`

### 5 项客户需求落地

1. **MR.1 编码长度**: 7 位 → 10 位(字母+数字),全链路校验
2. **OEM 3 优先展示**: 后台按 Brand 维护 OEM 3 sort_order,前台排序规则"先 Brand 字典 sort_order,再 OEM 3 sort_order"
3. **SEO 域名格式**: `product-name1/product-name2/oem-brand/oem-no-3`,每个 OEM 3 一条独立 URL,Razor SSR 渲染
4. **图片命名可配置**: 主图按 OEM 3 命名,详情图按 MR.1 共享,后台可配置命名字段
5. **聚合搜索 + 高亮**: 单框全局搜索所有字段,Meilisearch `_formatted` 高亮,弱中文分词(外贸场景)

### V2.docx 3 项已确认方案落地

1. **Machine Type 双轨(方案 B)**: OEM3 携带 `machine_type` 标签 + 机型表同步存储 `machine_category` 字段,前端分类树读机型表
2. **图片方案 A**: Image1 为 OEM3 独立主图,Image2-6 为 MR.1 全局共享详情图
3. **分区 6 预留**: 后端空表占位,不参与查询,不展示前端

---

## Impact (影响面)

### 受影响的能力域

| 能力域 | 改动性质 | 说明 |
|---|---|---|
| 数据模型 | 重构 | products 主键化、cross_references 加 sort_order、product_images 加 oem_no_3 + image_role |
| ETL 导入 | 改造 | 适配 MR.1 主键、OEM 3 sort_order、新图片命名规则 |
| 检索引擎 | 重构 | Meilisearch 索引结构改为 MR.1 嵌套文档 |
| 公开搜索 | 新增 | 单框聚合搜索 + 高亮(保留 8 字段高级筛选) |
| 公开详情页 | 重写 | Razor SSR + Vue 局部 hydration,SEO URL |
| 后台管理 | 改造 | OEM 排序管理页、图片上传分区、MR.1 校验 |
| 字典管理 | 新增 | xref_oem_brand sort_order 已存在,新增 MR.1 字典?否(MR.1 手动录入不进字典) |
| 测试 | 全量重构 | 旧数据清空,新模拟数据,视觉回归基线重置 |

### 受影响的代码

**后端**:
- `SakuraFilter.Core/Entities/Product.cs` — Product 加 MR.1 UNIQUE、CrossReference 加 SortOrder、ProductImage 加 OemNo3 + ImageRole
- `SakuraFilter.Infrastructure/Data/ProductDbContext.cs` — 索引调整
- `SakuraFilter.Infrastructure/Data/Configurations/*` — 字段约束更新
- `SakuraFilter.Infrastructure/Data/Migrations/` — 新增 V2 迁移
- `SakuraFilter.Search/MeiliSearchProvider.cs` — 索引主键改 MR.1,文档结构嵌套化
- `SakuraFilter.Search/PostgresSearchProvider.cs` — 适配 MR.1 主键
- `SakuraFilter.Etl/EtlImportService.cs` — 适配新主键 + sort_order + 图片命名
- `SakuraFilter.Api/Services/AdminProductService.cs` — MR.1 主键化校验
- `SakuraFilter.Api/Services/AdminProductImageService.cs` — BuildKey 改可配置
- `SakuraFilter.Api/Endpoints/AdminProductEndpoints.cs` — 加 OEM 排序管理端点
- `SakuraFilter.Api/Controllers/PublicProductController.cs` — 改 Razor SSR 路由
- `SakuraFilter.Api/Controllers/PublicSearchController.cs` — 加聚合搜索端点
- `SakuraFilter.Api/Services/CursorHmac.cs` — cursor 主键改 MR.1

**前端**:
- `frontend/src/router/index.ts` — 加 SEO URL 路由
- `frontend/src/views/public/PublicProductView.vue` — 拆分为 Razor 主页 + Vue 局部组件
- `frontend/src/views/public/PublicSearchView.vue` — 加单框聚合搜索
- `frontend/src/views/admin/AdminProductsView.vue` — 适配 MR.1 主键
- `frontend/src/views/admin/AdminProductFormView.vue` — 图片上传分区改造
- `frontend/src/views/admin/AdminXrefReorderView.vue` — **新增** OEM 排序管理页
- `frontend/src/api/index.ts` — 加聚合搜索 + OEM 排序 API
- `frontend/src/api/types.ts` — 类型定义更新

---

## ADDED Requirements (新增需求)

### Requirement: MR.1 内部主键化

系统 SHALL 将 `products.mr_1` 作为内部主键,在数据库层加 UNIQUE 约束(部分索引 `WHERE mr_1 IS NOT NULL`),所有跨表关联(MR.1 ↔ OEM3、MR.1 ↔ 机型、MR.1 ↔ 尺寸/参数/详情图)以 MR.1 为锚点。

#### Scenario: MR.1 唯一性校验
- **WHEN** 管理员创建新产品时填写已存在的 MR.1
- **THEN** 系统返回 409 Conflict,错误码 `MR1_ALREADY_EXISTS`,提示"MR.1 编码已存在"

#### Scenario: MR.1 长度校验
- **WHEN** 管理员填写 MR.1 长度 > 10 字符或含非字母数字字符
- **THEN** 系统返回 400 BadRequest,错误码 `MR1_FORMAT_INVALID`,提示"MR.1 编码须为 1-10 位字母+数字"

### Requirement: OEM 3 对外展示主键

系统 SHALL 将 `cross_references.oem_no_3` 作为对外展示主键,每个 OEM 3 对应一条独立的产品详情 URL,前端全程隐藏 MR.1 编号。

#### Scenario: OEM 3 详情页访问
- **WHEN** 访客访问 `/products/oil-filter/spin-on/bosch/F000000001`
- **THEN** 系统返回该 OEM 3 对应的产品详情页,页面标题为"OEM 3 + Product Name 1 + Product Name 2"

#### Scenario: 同 MR.1 多 OEM 3 推荐
- **WHEN** 访客在详情页查看某 OEM 3 产品
- **THEN** 页面底部"同 MR.1 其他 OEM 3"区块按 `oem_brand sort_order → oem_no_3 sort_order → oem_no_3` 排序展示

### Requirement: OEM 3 优先展示排序(类竞价排名)

系统 SHALL 提供后台 OEM 排序管理界面,允许管理员按 OEM Brand 维护同 Brand 下 OEM 3 的 sort_order。前台展示规则:先按 `xref_oem_brand.sort_order`(Brand 字典排序)分组,组内按 `cross_references.sort_order`(OEM 3 排序)升序。

#### Scenario: 后台批量设置 OEM 3 排序
- **WHEN** 管理员在 `/admin/xrefs/reorder` 页面选择 Brand "BOSCH",拖拽 OEM 3 "F000000001" 到第 1 位
- **THEN** 系统更新 `cross_references.sort_order=1`,返回 200 OK

#### Scenario: 前台搜索结果排序
- **WHEN** 访客搜索 "BOSCH",返回 10 条命中(分属 3 个 MR.1)
- **THEN** 结果按 `xref_oem_brand.sort_order` 分组(BOSCH 组在前),组内按 `cross_references.sort_order` 升序

### Requirement: SEO 友好 URL 与 SSR 渲染

系统 SHALL 为每个 OEM 3 生成 SEO 友好 URL `/products/:pn1/:pn2/:brand/:oem3`,使用 ASP.NET Razor Pages 服务端渲染,关键内容(产品名、OEM、参数、适配机型)在 HTML 源码中直接可见(无需 JS 执行)。

#### Scenario: 搜索引擎抓取
- **WHEN** Googlebot 抓取 `/products/oil-filter/spin-on/bosch/F000000001`
- **THEN** 响应 HTML 源码包含 `<h1>F000000001 Oil Filter Spin-on</h1>` + 完整参数表格 + 适配机型列表 + canonical link

#### Scenario: 旧 URL 301 重定向
- **WHEN** 访客访问旧 URL `/product/F000000001`
- **THEN** 系统返回 301 永久重定向到新 SEO URL

#### Scenario: Vue 局部 hydration
- **WHEN** 详情页加载完成
- **THEN** 图片画廊、对比按钮、询盘表单等交互组件完成 Vue hydration,可正常交互

### Requirement: 图片命名可配置 + 主图/详情图分层

系统 SHALL 区分主图(Image1,按 OEM 3 命名)与详情图(Image2-6,按 MR.1 共享),后台 `system_settings` 表提供命名字段配置项 `image.primary_naming_field` 与 `image.detail_naming_field`。

#### Scenario: 主图按 OEM 3 命名
- **WHEN** 管理员为 OEM 3 "F000000001" 上传主图
- **THEN** 图片存储 key 为 `products/primary/F000000001/F000000001-1.jpg`(若配置 `image.primary_naming_field=oem_no_3`)

#### Scenario: 详情图按 MR.1 共享
- **WHEN** 管理员为 MR.1 "ABC1234567" 上传详情图 slot 2
- **THEN** 图片存储 key 为 `products/detail/ABC1234567/ABC1234567-2.jpg`,同 MR.1 下所有 OEM 3 详情页共享此图

#### Scenario: 命名字段配置切换
- **WHEN** 管理员在系统设置页将 `image.primary_naming_field` 从 `oem_no_3` 改为 `mr_1`
- **THEN** 新上传的主图按 MR.1 命名,旧图保留原 key 不迁移

### Requirement: 聚合搜索 + 高亮显示

系统 SHALL 提供单框聚合搜索端点 `POST /api/public/search/aggregate`,支持跨字段(OEM 2/OEM 3/OEM Brand/Product Name/Machine Brand/Model/Engine)模糊匹配,返回 Meilisearch `_formatted` 字段含 `<mark>` 高亮标签。

#### Scenario: 单框聚合搜索
- **WHEN** 访客在顶部搜索框输入 "CAT 320D"
- **THEN** 系统返回所有 machine_brand 或 machine_model 含 "CAT" 或 "320D" 的产品,命中字段用 `<mark>CAT</mark>` 高亮

#### Scenario: 模糊拼写容错
- **WHEN** 访客输入 "BOSHC"(拼写错误)
- **THEN** Meilisearch typo 容错仍能命中 "BOSCH",返回结果

#### Scenario: 中文分词弱支持
- **WHEN** 访客输入"滤芯"
- **THEN** 系统通过 Meilisearch `separatorTokens` 配置 + PG trgm 兜底,尽力返回含"滤芯"的产品;外贸场景中文搜索为次要需求,允许召回率较低

### Requirement: Machine Type 双轨(方案 B)

系统 SHALL 在 OEM3(`cross_references.machine_type`)与机型表(`machine_applications.machine_category`)双轨存储 Machine Type,前端分类树读机型表字段。

#### Scenario: OEM3 携带 Machine Type 标签
- **WHEN** ETL 导入或后台录入 OEM 3 时填写 `machine_type=construction`
- **THEN** `cross_references.machine_type` 存储 "construction"

#### Scenario: 机型表同步 Machine Type
- **WHEN** 后台维护机型适配时
- **THEN** `machine_applications.machine_category` 字段同步填写,前端左侧分类树读此字段级联

### Requirement: 图片分层(方案 A)

系统 SHALL 按 V2.docx 方案 A 落地图片分层:Image1 为 OEM 3 独立主图(每个 OEM 3 一张),Image2-6 为 MR.1 全局共享详情图(同 MR.1 下所有 OEM 3 共享)。

#### Scenario: OEM 3 无主图兜底
- **WHEN** 某 OEM 3 未上传主图
- **THEN** 详情页主图位置显示 logo 占位图,`image_status=missing`

### Requirement: 分区 6 预留空表

系统 SHALL 在数据库保留 `partition6_placeholder` 空表(仅 id + created_at 两列),不参与任何业务查询、不展示前端、不进 Meilisearch 索引。

#### Scenario: 分区 6 表存在但无业务读写
- **WHEN** 任何 API 调用
- **THEN** 不涉及 `partition6_placeholder` 表读写

### Requirement: 旧数据清空 + 新模拟数据导入

系统 SHALL 在 V2 迁移时清空所有旧业务数据(products / cross_references / machine_applications / product_images / product_history / search_index_pending / search_index_dead_letter),按 V2 设计生成新模拟数据导入。

#### Scenario: 旧数据清空
- **WHEN** 执行 V2 迁移脚本
- **THEN** 上述表 TRUNCATE RESTART IDENTITY,Meilisearch 索引 DELETE ALL 后重建

#### Scenario: 新模拟数据导入
- **WHEN** 迁移脚本执行完成
- **THEN** 系统含 100 个 MR.1、300 个 OEM 3、500 条机型应用、对应图片占位,数据关系符合 V2 设计

---

## MODIFIED Requirements (修改的现有需求)

### Requirement: 公开产品详情页

**修改前**: 路由 `/product/:oem`(oem 为 OEM 2),纯 Vue SPA 渲染,SEO 弱
**修改后**: 路由 `/products/:pn1/:pn2/:brand/:oem3`(oem3 为 OEM 3),Razor SSR + Vue 局部 hydration,SEO 强

### Requirement: 公开搜索页

**修改前**: 仅 8 字段分框 AND 搜索
**修改后**: 单框聚合搜索(默认入口) + 8 字段高级筛选(折叠展开),两种模式共用 MR.1 嵌套 Meilisearch 索引

### Requirement: Meilisearch 索引结构

**修改前**: 文档主键 `oem_no_normalized`(OEM 2),扁平结构
**修改后**: 文档主键 `mr_1`,嵌套结构
```json
{
  "mr_1": "ABC1234567",
  "product_name_1": "Oil Filter",
  "product_name_2": "Spin-on",
  "type": "oil",
  "oem_2": "P00050000",
  "is_published": true,
  "oem_list": [
    {"oem_brand": "BOSCH", "oem_no_3": "F000000001", "sort_order": 1, "machine_type": "construction", "is_published": true}
  ],
  "machine_list": [
    {"machine_brand": "CAT", "machine_model": "320D", "machine_category": "construction"}
  ],
  "d1_mm": 80, "h1_mm": 100,
  "image_primary_key": "products/primary/F000000001/F000000001-1.jpg",
  "image_detail_keys": ["products/detail/ABC1234567/ABC1234567-2.jpg", ...],
  "is_discontinued": false
}
```

### Requirement: 后台产品表单

**修改前**: 7 分区表单,图片 6 slot 统一挂 OEM 2
**修改后**: 7 分区表单,分区 4 图片区分主图区(选 OEM 3 上传 1 张) + 详情图区(上传 2-6 张,MR.1 共享)

---

## REMOVED Requirements (移除的现有需求)

### Requirement: 旧 URL `/product/:oem`

**Reason**: 改用 SEO 友好 URL `/products/:pn1/:pn2/:brand/:oem3`
**Migration**: 旧 URL 301 重定向到新 URL,重定向映射表预生成(全量 OEM 3 → 新 URL)

### Requirement: 旧图片 key 命名规则 `products/{oem2}/{oem2}-{slot}`

**Reason**: 改用主图按 OEM 3 / 详情图按 MR.1 分层命名
**Migration**: 旧数据全部清空,无迁移需求

---

## 数据库设计方案

### 新增/修改表结构

#### products 表(主表,MR.1 主键化)

```sql
-- 1. mr_1 加 UNIQUE 部分索引
CREATE UNIQUE INDEX idx_products_mr_1_unique
  ON products (mr_1) WHERE mr_1 IS NOT NULL;

-- 2. mr_1 加 CHECK 约束(1-10 位字母+数字)
ALTER TABLE products ADD CONSTRAINT chk_mr_1_format
  CHECK (mr_1 IS NULL OR mr_1 ~ '^[A-Za-z0-9]{1,10}$');

-- 3. oem_no_normalized 索引降级为普通索引(不再是 UNIQUE)
DROP INDEX IF EXISTS ix_products_oem_no_normalized_unique;
CREATE INDEX idx_products_oem_no_normalized ON products (oem_no_normalized);
-- 注意: oem_no_normalized 语义改为"OEM 2 归一化值",不再唯一(同 MR.1 可对应多个 OEM 2)
-- 实际: OEM 2 在 products 表主行,每个 MR.1 一行,OEM 2 唯一性由 MR.1 唯一性保证
```

**关键决策**: products 表保持"一行 = 一个 MR.1",`oem_2` 字段存该 MR.1 的代表 OEM 2(可能多个 OEM 2 对应同 MR.1,但主表只存一个代表值,其余走 cross_references)。`oem_no_normalized` 仍 UNIQUE(因为一个 MR.1 只有一个代表 OEM 2),但**业务主键语义让渡给 mr_1**。

#### cross_references 表(OEM 3 主键化 + 排序 + Machine Type)

```sql
ALTER TABLE cross_references
  ADD COLUMN sort_order int NOT NULL DEFAULT 0,
  ADD COLUMN machine_type varchar(50) DEFAULT 'others',
  ADD COLUMN is_published boolean DEFAULT true;  -- OEM 3 上架开关

-- 排序索引(支撑"先 Brand 字典 sort_order,再 OEM 3 sort_order"查询)
CREATE INDEX idx_xrefs_brand_oem3_sort
  ON cross_references (oem_brand, sort_order, oem_no_3)
  WHERE oem_brand IS NOT NULL AND is_discontinued = false AND is_published = true;

-- OEM 3 唯一性(同 Brand 下 OEM 3 唯一)
CREATE UNIQUE INDEX uq_xrefs_brand_oem3
  ON cross_references (oem_brand, oem_no_3)
  WHERE oem_brand IS NOT NULL AND oem_no_3 IS NOT NULL;
```

**machine_type 枚举值**: `agriculture` / `commercial` / `construction` / `industrial` / `others`(与 `dict_machine.machine_category` 一致)

#### product_images 表(主图/详情图分层)

```sql
ALTER TABLE product_images
  ADD COLUMN oem_no_3 varchar(200),     -- 主图关联的 OEM 3(详情图为 NULL)
  ADD COLUMN image_role varchar(20) NOT NULL DEFAULT 'detail';  -- primary/detail

-- 主图唯一约束(1 个 OEM 3 仅 1 张主图)
CREATE UNIQUE INDEX uq_product_images_primary
  ON product_images (oem_no_3) WHERE image_role = 'primary' AND oem_no_3 IS NOT NULL;

-- 详情图唯一约束(同 MR.1 下 slot 2-6 唯一)
-- 注意: product_images.product_id 已关联 products.id(即 MR.1 行)
CREATE UNIQUE INDEX uq_product_images_detail_slot
  ON product_images (product_id, slot) WHERE image_role = 'detail';
```

#### machine_applications 表(双轨 Machine Type)

```sql
ALTER TABLE machine_applications
  ADD COLUMN machine_category varchar(50) DEFAULT 'others';

CREATE INDEX idx_machine_apps_category
  ON machine_applications (machine_category, machine_brand, machine_model);
```

#### partition6_placeholder 表(预留空表)

```sql
CREATE TABLE IF NOT EXISTS partition6_placeholder (
  id bigserial PRIMARY KEY,
  created_at timestamptz NOT NULL DEFAULT now()
);
-- 无业务读写,不进 EF Core ModelSnapshot 之外的所有查询
```

#### system_settings 新增配置项

```sql
INSERT INTO system_settings (key, value, description) VALUES
  ('image.primary_naming_field', 'oem_no_3',
   '主图命名字段: oem_no_3 / mr_1 / oem_2'),
  ('image.detail_naming_field', 'mr_1',
   '详情图命名字段: mr_1 / oem_no_3 / oem_2'),
  ('search.aggregate_highlight_pre_tag', '<mark>',
   '聚合搜索高亮前置标签'),
  ('search.aggregate_highlight_post_tag', '</mark>',
   '聚合搜索高亮后置标签'),
  ('search.aggregate_typo_tolerance', '2',
   'Meilisearch typo 容错等级 0/1/2'),
  ('seo.url_legacy_redirect_enabled', 'true',
   '是否启用旧 URL 301 重定向'),
  ('seo.sitemap_shard_size', '50000',
   'sitemap.xml 单文件最大 URL 数');
```

### 索引设计汇总

| 索引名 | 表 | 字段 | 用途 |
|---|---|---|---|
| idx_products_mr_1_unique | products | mr_1 (WHERE NOT NULL) | MR.1 主键唯一性 |
| idx_xrefs_brand_oem3_sort | cross_references | (oem_brand, sort_order, oem_no_3) | OEM 3 优先排序查询 |
| uq_xrefs_brand_oem3 | cross_references | (oem_brand, oem_no_3) | OEM 3 唯一性 |
| idx_machine_apps_category | machine_applications | (machine_category, machine_brand, machine_model) | 机型分类树级联 |
| uq_product_images_primary | product_images | oem_no_3 (WHERE role=primary) | 主图唯一 |
| uq_product_images_detail_slot | product_images | (product_id, slot) (WHERE role=detail) | 详情图 slot 唯一 |

---

## 接口设计方案

### 新增端点

#### POST /api/public/search/aggregate(聚合搜索)

```json
// Request
{
  "q": "CAT 320D",
  "page": 1,
  "pageSize": 20,
  "tolerance": 5,
  "includeDiscontinued": false
}

// Response 200
{
  "total": 42,
  "hits": [
    {
      "mr1": "ABC1234567",
      "oemNo3": "F000000001",
      "oemBrand": "BOSCH",
      "productName1": "Oil Filter",
      "productName2": "Spin-on",
      "type": "oil",
      "imagePrimaryKey": "products/primary/F000000001/F000000001-1.jpg",
      "imagePrimaryUrl": "https://cdn.../F000000001-1.jpg",
      "_formatted": {
        "oemNo3": "F000000001",
        "oemBrand": "BOSCH",
        "productName1": "Oil <mark>Filter</mark>",
        "machineBrand": "<mark>CAT</mark>",
        "machineModel": "<mark>320D</mark>"
      },
      "_rankingScore": 0.95
    }
  ],
  "processingTimeMs": 12
}
```

#### GET/POST /api/admin/xrefs/reorder(OEM 3 排序管理)

```json
// GET /api/admin/xrefs/reorder?oemBrand=BOSCH
// Response 200
{
  "oemBrand": "BOSCH",
  "brandSortOrder": 1,
  "items": [
    {"oemNo3": "F000000001", "sortOrder": 1, "mr1": "ABC1234567", "isPublished": true},
    {"oemNo3": "F000000002", "sortOrder": 2, "mr1": "ABC1234567", "isPublished": true}
  ]
}

// POST /api/admin/xrefs/reorder
// Request
{
  "oemBrand": "BOSCH",
  "items": [
    {"oemNo3": "F000000001", "sortOrder": 1},
    {"oemNo3": "F000000002", "sortOrder": 2}
  ]
}
// Response 200
{"updated": 2, "oemBrand": "BOSCH"}
```

#### GET /api/admin/xrefs/reorder/brands(Brand 列表)

```json
// Response 200
{
  "brands": [
    {"brand": "BOSCH", "sortOrder": 1, "oem3Count": 15},
    {"brand": "MANN", "sortOrder": 2, "oem3Count": 8}
  ]
}
```

#### GET /sitemap.xml + /sitemaps/{shard}.xml(站点地图)

```xml
<!-- /sitemap.xml (索引) -->
<?xml version="1.0" encoding="UTF-8"?>
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <sitemap><loc>https://sakurafilter.com/sitemaps/products-1.xml</loc></sitemap>
  <sitemap><loc>https://sakurafilter.com/sitemaps/products-2.xml</loc></sitemap>
</sitemapindex>

<!-- /sitemaps/products-1.xml (分片) -->
<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <url>
    <loc>https://sakurafilter.com/products/oil-filter/spin-on/bosch/F000000001</loc>
    <lastmod>2026-07-17</lastmod>
    <changefreq>weekly</changefreq>
    <priority>0.8</priority>
  </url>
</urlset>
```

### 修改的端点

#### GET /products/:pn1/:pn2/:brand/:oem3(Razor SSR,新)

返回完整 HTML,关键内容服务端渲染:
- `<h1>{oem3} {productName1} {productName2}</h1>`
- 参数表格(HTML `<table>`)
- 适配机型列表(HTML `<ul>`)
- 同 MR.1 其他 OEM 3 推荐(HTML `<ul>`,带链接)
- canonical link
- OG meta tags
- Vue 局部 hydration 组件挂载点(`<div id="vue-gallery" data-product-id="...">`)

#### POST /api/admin/products(创建产品,MR.1 主键化)

Request 新增字段:
```json
{
  "mr1": "ABC1234567",  // 必填,1-10 位字母+数字
  "oem2": "P00050000",
  "oemList": [
    {"oemBrand": "BOSCH", "oemNo3": "F000000001", "sortOrder": 1, "machineType": "construction", "isPublished": true}
  ],
  "machineApplications": [...],
  "dimensions": {"d1Mm": 80, "h1Mm": 100, ...},
  ...
}
```

Response 409 错误码新增 `MR1_ALREADY_EXISTS`。

#### POST /api/admin/products/{id}/images(图片上传,分层)

```json
// 主图
POST /api/admin/products/{mr1}/images/primary?oemNo3=F000000001
Content-Type: multipart/form-data
file: <binary>

// 详情图
POST /api/admin/products/{mr1}/images/detail?slot=2
Content-Type: multipart/form-data
file: <binary>
```

### 错误码体系(新增)

| 错误码 | HTTP | 含义 |
|---|---|---|
| `MR1_ALREADY_EXISTS` | 409 | MR.1 编码已存在 |
| `MR1_FORMAT_INVALID` | 400 | MR.1 格式不符(1-10 位字母+数字) |
| `OEM3_ALREADY_EXISTS` | 409 | 同 Brand 下 OEM 3 已存在 |
| `IMAGE_PRIMARY_DUPLICATE` | 409 | 该 OEM 3 已有主图 |
| `IMAGE_DETAIL_SLOT_DUPLICATE` | 409 | 同 MR.1 该 slot 详情图已存在 |
| `SEO_URL_SLUG_EMPTY` | 400 | SEO URL slug 字段缺失 |

---

## 检索逻辑设计

### Meilisearch 索引配置

```
主键: mr_1
可搜索字段: product_name_1, product_name_2, oem_2, oem_list.oem_brand, oem_list.oem_no_3,
            machine_list.machine_brand, machine_list.machine_model, machine_list.engine_brand
可过滤字段: type, is_discontinued, oem_list.is_published, machine_list.machine_category,
            d1_mm, d2_mm, h1_mm (范围)
可排序字段: oem_list.sort_order (但嵌套排序受限,见下方决策)
高亮字段: 全部可搜索字段
 typo 容错: 2(配置项可调)
 separatorTokens: [" ", "-", "/", ",", "."]  (弱中文分词支持)
```

**嵌套字段排序决策**: Meilisearch 对嵌套数组字段排序支持有限。采用方案:
- 文档主层级按 `_rankingScore` 降序(相关性)
- 同分时按 `oem_list` 中最小 `sort_order` 升序(Brand 优先级传递到文档级)
- 前端展示时,文档内 `oem_list` 按 `oem_brand.sort_order → oem_no_3.sort_order` 排序

### 聚合搜索查询流程

```
1. 前端 POST /api/public/search/aggregate {q: "CAT 320D"}
2. 后端 MeiliSearchProvider.SearchAsync:
   - 构建 SearchQuery { Q: "CAT 320D", Limit: 20, Offset: 0,
       AttributesToHighlight: [...], HighlightPreTag: "<mark>", HighlightPostTag: "</mark>",
       ShowRankingScore: true }
   - filters: is_discontinued = false AND oem_list.is_published = true(任一 OEM 3 上架)
3. Meilisearch 返回 hits + _formatted + _rankingScore
4. 后端 PostgresSearchProvider 兜底(Meili 离线时):
   - SELECT * FROM products p JOIN cross_references x ON ...
   - WHERE p.is_discontinued = false AND x.is_published = true
     AND (p.product_name_1 ILIKE '%CAT%' OR x.oem_brand ILIKE '%CAT%' OR ...)
   - ORDER BY x.oem_brand, x.sort_order, x.oem_no_3
5. 返回统一结构
```

### ±范围检索逻辑(尺寸双存储)

- 数据库: `d1_mm` numeric(原始值) + `d1_mm_raw` text(原始字符串,如"80.5±0.1")
- ETL/后台录入时: 解析原始字符串提取数值存 `d1_mm`,原始串存 `d1_mm_raw`
- 检索时: 用户输入 D1=80,容差 ±5 → 查询 `d1_mm BETWEEN 75 AND 85`
- 展示时: 详情页显示 `d1_mm_raw`(原始字符串)

---

## 数据导入方案

### 旧数据清空脚本(一次性,不可逆)

```sql
BEGIN;
TRUNCATE product_images, product_history, cross_references, machine_applications
  RESTART IDENTITY CASCADE;
TRUNCATE products RESTART IDENTITY;
TRUNCATE search_index_pending, search_index_dead_letter RESTART IDENTITY;
TRUNCATE etl_progress_log RESTART IDENTITY;
-- 字典表保留(dict_*, xref_oem_brand)
-- 用户表保留(users, refresh_tokens)
-- 系统配置保留(system_settings)
COMMIT;
```

Meilisearch: `DELETE /indexes/products/documents` 清空,然后重建索引。

### 新模拟数据生成规则

| 实体 | 数量 | 规则 |
|---|---|---|
| MR.1(products) | 100 | `MR000001` ~ `MR000100`,1-10 位字母+数字格式 |
| OEM 3(cross_references) | 300 | 每 MR.1 对应 2-5 个 OEM 3,Brand 取 BOSCH/MANN/MAN/CAT/PERKINS 等 10 个 |
| 机型(machine_applications) | 500 | 每 MR.1 对应 3-8 个机型,machine_category 覆盖 5 类 |
| 主图 | 300 | 占位图(logo),key 按 `products/primary/{oem3}/{oem3}-1.png` |
| 详情图 | 200 | 占位图,key 按 `products/detail/{mr1}/{mr1}-{slot}.png`,slot 2-6 |

### ETL 适配改造

`EtlImportService.cs` products 导入:
- 解析 `mr_1` 字段,校验格式(1-10 位字母+数字)
- 校验 `mr_1` 唯一性(冲突时按 mode 处理:full-load 覆盖、insert-only 跳过、upsert 更新)
- `oem_no_normalized` 从 `mr_1` 派生(临时方案,后续可移除)

`EtlImportService.cs` xrefs 导入:
- 解析 `sort_order`(默认 0)、`machine_type`(默认 others)、`is_published`(默认 true)
- 校验 `(oem_brand, oem_no_3)` 唯一性
- 关联 `mr_1` 到 `products.id`(找不着时 `IncrSkippedMissingOem` 改为 `IncrSkippedMissingMr1`)

`AdminProductImageService.BuildKey` 改造:
```csharp
public async Task<string> BuildKeyAsync(string namingValue, short slot, string role, CancellationToken ct)
{
    var settings = await _db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
    var fieldKey = role == "primary" ? "image.primary_naming_field" : "image.detail_naming_field";
    var field = settings.GetValueOrDefault(fieldKey) ?? "oem_no_3";
    var prefix = role == "primary" ? "primary" : "detail";
    var ext = "jpg";  // 默认,实际从上传文件取
    return $"products/{prefix}/{namingValue}/{namingValue}-{slot}.{ext}";
}
```

---

## 测试用例设计

### 后端单元测试

| 用例 | 验证点 |
|---|---|
| `Mr1_ValidateFormat_Valid` | MR.1 "ABC123" 通过校验 |
| `Mr1_ValidateFormat_TooLong` | MR.1 "ABCDEFGHIJK"(11 位) 抛 `MR1_FORMAT_INVALID` |
| `Mr1_ValidateFormat_InvalidChar` | MR.1 "ABC-123" 抛 `MR1_FORMAT_INVALID` |
| `Mr1_Create_Duplicate` | 重复 MR.1 抛 `MR1_ALREADY_EXISTS` |
| `Oem3_Reorder_BrandGrouping` | 同 Brand 内 OEM 3 sort_order 正确更新 |
| `Oem3_Search_OrderByBrandThenOem3` | 搜索结果按 Brand sort_order → OEM 3 sort_order 排序 |
| `Image_BuildKey_Primary_OemNo3` | 主图 key = `products/primary/{oem3}/{oem3}-1.jpg` |
| `Image_BuildKey_Detail_Mr1` | 详情图 key = `products/detail/{mr1}/{mr1}-{slot}.jpg` |
| `Image_BuildKey_ConfigSwitch` | 切换配置后新图按新规则命名 |
| `Search_Aggregate_Highlight` | 聚合搜索返回 `_formatted` 含 `<mark>` |
| `Search_Aggregate_TypoTolerance` | "BOSHC" 命中 "BOSCH" |
| `Search_Fallback_Pg` | Meili 离线时降级 PG 兜底 |
| `MachineType_DualTrack` | OEM3.machine_type 与 machine_apps.machine_category 双写一致 |

### 前端单元/契约测试

| 用例 | 验证点 |
|---|---|
| `Search_Aggregate_HighlightRender` | `<mark>` 标签正确渲染(v-html + XSS 白名单) |
| `Search_Aggregate_Debounce` | 500ms 防抖,AbortController 取消前序请求 |
| `XrefReorder_DragDrop` | 拖拽排序后调 API,顺序更新 |
| `ProductForm_ImageUpload_Primary` | 选 OEM 3 后上传主图,key 含 OEM 3 |
| `ProductForm_ImageUpload_Detail` | 上传详情图,key 含 MR.1 |
| `ProductForm_Mr1_Validation` | MR.1 输入超 10 位时前端校验拦截 |

### E2E 测试

| 用例 | 验证点 |
|---|---|
| `Public_AggregateSearch_Flow` | 顶部搜索框输入 → 结果列表 → 点击进入详情页 |
| `Public_ProductDetail_SeoUrl` | 访问 `/products/oil-filter/spin-on/bosch/F000000001` → 页面正常渲染 |
| `Public_ProductDetail_LegacyRedirect` | 访问 `/product/F000000001` → 301 重定向到新 URL |
| `Public_ProductDetail_SeoMeta` | 查看页面源码 → 含 `<h1>`、canonical、OG meta |
| `Public_MachineType_FilterCascade` | 左侧分类树点击 construction → 右侧产品列表过滤 |
| `Admin_XrefReorder_Flow` | 后台进入 OEM 排序管理 → 拖拽 → 保存 → 前台搜索验证顺序 |
| `Admin_ProductForm_ImageLayered` | 新增产品 → 上传主图(选 OEM 3) + 详情图 → 保存 → 详情页验证 |
| `Admin_ProductForm_Mr1_Duplicate` | 输入重复 MR.1 → 提示错误 |

### 视觉回归

重置所有 baseline(旧数据清空,UI 大改):
- `public-product-seo.spec.ts` — SEO 详情页
- `public-aggregate-search.spec.ts` — 聚合搜索页
- `admin-xref-reorder.spec.ts` — OEM 排序管理页

---

## 风险清单

| 风险 | 概率 | 影响 | 缓解措施 |
|---|---|---|---|
| MR.1 主键化改造触及全链路,可能遗漏关联点 | 高 | 高 | Phase 0 充分测试,4 轮自查覆盖所有关联 |
| Meilisearch 嵌套文档排序能力不足 | 中 | 中 | 文档级按 Brand sort_order 排序,文档内 OEM 列表前端排序 |
| Razor SSR 首屏性能(1M 产品) | 中 | 中 | 详情页走 PG 索引查询,< 50ms;热门产品可加内存缓存 |
| 中文分词弱(外贸场景) | 低 | 低 | Meilisearch `separatorTokens` 配置 + PG trgm 兜底,客户已确认可接受 |
| 旧 URL 301 重定向映射表过大 | 低 | 低 | 按 OEM 3 唯一,1M 条映射走 DB 查询而非内存表 |
| 视觉回归基线全量重置成本 | 中 | 低 | 仅重置受影响页面,保留字典页等不变页面基线 |
| 并发编辑 MR.1 主键冲突 | 低 | 中 | 复用现有 xmin 乐观锁,409 提示刷新重试 |
| ETL 适配改造可能引入新 bug | 中 | 高 | 旧数据清空后重新导入,ETL 失败有死信队列兜底 |
| Razor + Vue hydration 时序问题 | 中 | 中 | 局部 hydration 组件避免依赖服务端数据,通过 `data-*` 属性传递 |
| OEM 3 sort_order 默认值 0 导致无序 | 中 | 低 | 后台排序管理页强制设置,前端默认按 oem_no_3 字典序兜底 |

---

## 4 轮自查成果

### 第 1 轮:数据关联关系核对

✅ **MR.1 一对多 OEM 3**: `cross_references.product_id` → `products.id`,每个 MR.1(products 行)对应多个 OEM 3(cross_references 行)。无冲突。
✅ **同 MR.1 共享尺寸/参数/详情图**: 尺寸/参数在 products 表(一行 = 一套),详情图在 product_images 表(`product_id` 关联 + `image_role='detail'`)。无冲突。
✅ **OEM Brand 与 Machine Brand 隔离**: `cross_references.oem_brand` 与 `machine_applications.machine_brand` 分表分字段,索引独立。无冲突。
✅ **上下架双重校验**: `products.is_published`(MR.1 上架) + `cross_references.is_published`(OEM 3 上架),Meilisearch 过滤 `is_discontinued = false AND oem_list.is_published = true`。无遗漏。
✅ **尺寸双存储**: products 表加 `d1_mm_raw` / `d2_mm_raw` / `h1_mm_raw` 等原始字符串列,检索走数值列,展示走原始列。闭环。
✅ **Meilisearch MR.1 嵌套文档**: 文档主键 mr_1,oem_list/machine_list 嵌套,一次查询返回完整关联。无跨文档查询。

### 第 2 轮:业务边界漏洞排查

⚠️ **修复 1**: 多 OEM 3 共用一套参数 → 已通过"products 一行 = 一个 MR.1 = 一套参数"保证,无需额外处理
⚠️ **修复 2**: 同一机型绑定多个 MR.1 → `machine_applications.product_id` 多对一,多个 MR.1 可关联同 `(machine_brand, machine_model)`,无冲突
⚠️ **修复 3**: 尺寸带特殊符号 → 双存储方案,`d1_mm_raw` 存"80.5±0.1",`d1_mm` 存 80.5,检索用数值列
⚠️ **修复 4**: Excel 批量导入重复 MR.1/OEM 3 → ETL `ON CONFLICT (mr_1) DO UPDATE/NOTHING` + `(oem_brand, oem_no_3)` 唯一约束,重复行计入 `skipped_duplicate`
⚠️ **修复 5**: 未设置 Machine Type 的 OEM 3 → `cross_references.machine_type` DEFAULT 'others',前端分类树"others"类展示
⚠️ **修复 6**: 无主图的 OEM 3 → 详情页主图位置显示 logo 占位,`image_status='missing'`
⚠️ **修复 7**: 参数缺失 → products 表字段均 nullable,详情页缺失字段显示"—"
⚠️ **修复 8**: 多条件叠加筛选 → Meilisearch filters AND 拼接,PG 兜底 WHERE AND 拼接
⚠️ **修复 9**: 全局搜索模糊拼写容错 → Meilisearch typo=2,PG 兜底 ILIKE

### 第 3 轮:前后端联动链路校验

⚠️ **修复 10**: 后台 6 大管理模块与 7 张表读写同步 → 改造后:
- 模块 1(产品基础信息) → products 表(MR.1 主键化)
- 模块 2(OEM 体系管理) → cross_references 表(加 sort_order/machine_type/is_published) + xref_oem_brand 字典
- 模块 3(尺寸参数管理) → products 表(d1_mm 等 + d1_mm_raw 等)
- 模块 4(图片资源管理) → product_images 表(加 oem_no_3/image_role)
- 模块 5(技术参数管理) → products 表(media 等)
- 模块 6(机型适配管理) → machine_applications 表(加 machine_category)
- 模块 7(Excel 批量导入) → ETL 适配新主键 + 新字段

⚠️ **修复 11**: 数据变更同步 Meilisearch 索引 → 复用现有 IndexReplayWorker + search_index_pending 队列,MR.1 级别同步(任一 OEM 3 变更触发整 MR.1 文档重建)
⚠️ **修复 12**: 前端双维度筛选(左侧机型分类 + 顶部产品类型标签) → Meilisearch filters: `machine_list.machine_category = "construction" AND type = "oil"`,PG 兜底 JOIN 查询
⚠️ **修复 13**: 详情页同 MR.1 其他 OEM 3 推荐 → 后端 API `/api/public/products/{mr1}/sibling-oem3` 返回排序后列表
⚠️ **修复 14**: 主图/详情图展示优先级 → 详情页主图位置显示当前 OEM 3 主图,详情图轮播显示 MR.1 共享图(slot 2-6)

### 第 4 轮:已确认需求落地校验

✅ **Machine Type 双轨**: OEM3 携带 `cross_references.machine_type` + 机型表 `machine_applications.machine_category`,前端分类树读机型表字段。完整落地。
✅ **图片方案 A**: Image1 为 OEM 3 独立主图(`product_images.image_role='primary'` + `oem_no_3`),Image2-6 为 MR.1 共享详情图(`image_role='detail'` + `product_id`)。完整落地。
✅ **分区 6 预留**: `partition6_placeholder` 空表,不进 EF Core 业务查询,不展示前端。完整落地。

### 本轮排查修复的逻辑漏洞清单

1. products 表 `oem_no_normalized` 语义降级处理(原 UNIQUE 保留,但业务主键让渡给 mr_1)
2. cross_references 表新增 `is_published` 字段(OEM 3 独立上下架,与 MR.1 上下架双重校验)
3. cross_references 表 `(oem_brand, oem_no_3)` 唯一约束(防止同 Brand 下 OEM 3 重复)
4. product_images 表 `image_role` + `oem_no_3` 字段(主图/详情图分层)
5. product_images 表主图唯一约束 + 详情图 slot 唯一约束
6. machine_applications 表新增 `machine_category` 字段(双轨 Machine Type)
7. system_settings 表新增 7 项配置(图片命名 + 搜索高亮 + SEO)
8. Meilisearch 索引主键改 mr_1 + 嵌套文档结构 + 嵌套字段过滤策略
9. 聚合搜索端点 + Meilisearch `_formatted` 高亮 + PG 兜底
10. Razor SSR 详情页 + 旧 URL 301 重定向 + sitemap 分片
11. ETL 适配 MR.1 主键 + sort_order + machine_type + is_published + 图片分层
12. 旧数据清空脚本 + 新模拟数据生成规则
13. 4 项客户确认点全部落地(Brand/OEM3 双层排序、OEM 3 URL、新数据规则、弱中文分词)
14. 3 项 V2.docx 已确认方案全部落地(Machine Type 双轨、图片方案 A、分区 6 预留)
