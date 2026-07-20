"""P1/P2/P3 + V2 修复回归测试 - 防止已修复问题再次引入

用法:
  python _test_regression.py          # 完整扫描 (代码层 grep + 健康检查)
  python _test_regression.py --scan   # 仅扫描代码层, 不调 API

设计:
  - 阶段 1 SCAN: grep 验证 P1/P2/P3/V2 修复模式存在 (14 + 47 = 61 个修复点)
  - 阶段 2 HEALTH: 系统健康检查 (4 个端点)
  - 阶段 3 REPORT: 汇总报告 + 退出码 (0=全绿, 1=有回归)
  - 与 _test_p0_fixes.py 互补: P0 用 API 验证, P1/P2/P3/V2 用 grep 验证 (大多无 API 表面)

V2 修复点来源: V2 架构迁移 47 项漏洞 (spec.md 第 23 轮审查 30 项 + 第 24 轮 17 项衍生)
  - 数据结构 (10): Product/CrossReference/MachineApplication/ProductImage V2 字段
  - 校验逻辑 (8): MR.1 校验 + 枚举校验 + 长度校验
  - 图片命名 (6): BuildKeyAsync + image_role + 路径穿越防御
  - 搜索 (5): Mr1IndexDoc + ProductIndexDoc + brand 优先
  - SEO URL (5): 301 重定向 + slug + Razor Pages
  - ETL (6): mr_1 主键 + 枚举 + IncrSkippedMissingMr1
  - 前端 (4): JSON 数据岛 + Vue mount + buildProductUrl + 多入口
  - 安全 (3): XSS 防御 + HMAC Ticks + 错误码
"""
import re
import sys
import time
import urllib.request
import urllib.error
from pathlib import Path

BASE = "http://localhost:5148"
TOKEN = "dev-admin-token-rotate-in-prod-MZK4R9P3X6V2N7Q1L5F0B8H3C"
HEADERS = {"Content-Type": "application/json", "X-Admin-Token": TOKEN}
REPO = Path(__file__).resolve().parent.parent

# P1/P2/P3 修复点清单 (id, level, title, file, fix_pattern)
#   fix_pattern: 修复后代码应包含的正则 (匹配 = 已修复, 不匹配 = 回归)
REGRESSION_CHECKS = [
    # ===== P1 严重级 (6 项) =====
    {
        "id": "P1-1a", "level": "P1",
        "title": "IndexReplayWorker N+1 查询 (批量预拉候选)",
        "file": "backend/src/SakuraFilter.Api/Services/IndexReplayWorker.cs",
        "fix_pattern": r"批量预拉候选.*existingDict",
    },
    {
        "id": "P1-1b", "level": "P1",
        "title": "OemBrandDictService N+1 查询 (GroupBy 聚合)",
        "file": "backend/src/SakuraFilter.Api/Services/OemBrandDictService.cs",
        "fix_pattern": r"GroupBy\(x => x\.OemBrand\)",
    },
    {
        "id": "P1-2", "level": "P1",
        "title": "Token 比较用 FixedTimeEquals (时序攻击防护)",
        "file": "backend/src/SakuraFilter.Api/Services/DevTokenAuthMiddleware.cs",
        "fix_pattern": r"CryptographicOperations\.FixedTimeEquals",
    },
    {
        "id": "P1-3", "level": "P1",
        "title": "限流 ForwardedHeaders 中间件",
        # v28-4 P0 修复: 中间件管道代码实际在 MiddlewarePipelineExtensions.cs (Program.cs L38 调用扩展方法)
        "file": "backend/src/SakuraFilter.Api/Extensions/MiddlewarePipelineExtensions.cs",
        "fix_pattern": r"app\.UseForwardedHeaders",
    },
    {
        "id": "P1-4", "level": "P1",
        "title": "前端 debounceTimer onUnmounted 清理",
        "file": "frontend/src/views/public/PublicSearchView.vue",
        "fix_pattern": r"onUnmounted\(\(\) => \{[^}]*clearTimeout\(debounceTimer\)",
    },
    {
        "id": "P1-5", "level": "P1",
        "title": "生产环境 UseExceptionHandler",
        # v28-4 P0 修复: 中间件管道代码实际在 MiddlewarePipelineExtensions.cs
        "file": "backend/src/SakuraFilter.Api/Extensions/MiddlewarePipelineExtensions.cs",
        "fix_pattern": r"app\.UseExceptionHandler",
    },
    {
        "id": "P1-6", "level": "P1",
        "title": "Swagger 仅 Development 暴露",
        # v28-4 P0 修复: 中间件管道代码实际在 MiddlewarePipelineExtensions.cs
        #   正则放宽: 接受 env.IsDevelopment() 或 app.Environment.IsDevelopment() 两种写法
        "file": "backend/src/SakuraFilter.Api/Extensions/MiddlewarePipelineExtensions.cs",
        "fix_pattern": r"if\s*\((env|app\.Environment)\.IsDevelopment\(\)\)\s*\{[^}]*UseSwagger",
    },
    # ===== P2 中等级 (6 项) =====
    {
        "id": "P2-1", "level": "P2",
        "title": "AdminProductService Oem2 搜索用 ILike + EscapeLikePattern",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r"EF\.Functions\.ILike\(p\.OemNoDisplay.*EscapeLikePattern",
    },
    {
        "id": "P2-2", "level": "P2",
        "title": "ValidateForm 13 字段长度校验",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r"var checks = new \(string Label, string\? Value, int Max\)\[\]",
    },
    {
        "id": "P2-3", "level": "P2",
        "title": "PublicProductController slug 长度校验 (200 字符)",
        "file": "backend/src/SakuraFilter.Api/Controllers/PublicProductController.cs",
        "fix_pattern": r"slug\.Length > 200",
    },
    {
        "id": "P2-4", "level": "P2",
        "title": "perf.ts fetch 调用注释说明 keepalive",
        "file": "frontend/src/utils/perf.ts",
        "fix_pattern": r"keepalive: true.*ingest.*已豁免",
    },
    {
        "id": "P2-5", "level": "P2",
        "title": "AuthTokenBroadcaster Dispose 后置 null",
        "file": "backend/src/SakuraFilter.Api/Services/AuthTokenBroadcaster.cs",
        "fix_pattern": r"Dispose 后置 null.*防止外部访问已 Dispose 对象",
    },
    {
        "id": "P2-7", "level": "P2",
        "title": "perf.ts uninstallPerfInterceptor 导出",
        "file": "frontend/src/utils/perf.ts",
        "fix_pattern": r"export function uninstallPerfInterceptor",
    },
    # ===== P3 轻微级 (2 项, P3-3/4/5 跳过) =====
    {
        "id": "P3-1", "level": "P3",
        "title": "EtlProgressBroadcaster 参数化 pg_notify",
        "file": "backend/src/SakuraFilter.Api/Services/EtlProgressBroadcaster.cs",
        "fix_pattern": r"SELECT pg_notify\(@channel, @payload\)",
    },
    {
        "id": "P3-2", "level": "P3",
        "title": "GetBySlug 3 次 fallback 合并为 1 次 OR 查询",
        # v28-4 P0 修复: P3-2 修复实际在 IProductDetailService.cs L78 (PublicProductController 调用 IProductDetailService.GetByOemAsync)
        "file": "backend/src/SakuraFilter.Api/Services/IProductDetailService.cs",
        "fix_pattern": r"3 次 fallback 合并为 1 次 OR 查询",
    },
    # ===== V2 架构迁移 (47 项) — 8 个子类别 =====
    # ----- V2-DS 数据结构 (10 项): Product/CrossReference/MachineApplication/ProductImage V2 字段全集 -----
    {
        "id": "V2-DS-1", "level": "V2",
        "title": "Product.Mr1 字段 (V2 主键)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'\[Column\("mr_1"\)\]\s*public string\?\s*Mr1',
    },
    {
        "id": "V2-DS-2", "level": "V2",
        "title": "Product.Oem2 字段 (OEM 2 全量收纳)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'\[Column\("oem_2"\)\]\s*public string\?\s*Oem2',
    },
    {
        "id": "V2-DS-3", "level": "V2",
        "title": "Product.IsPublished 字段 (上架状态)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'\[Column\("is_published"\)\]\s*public bool\s*IsPublished',
    },
    {
        "id": "V2-DS-4", "level": "V2",
        "title": "Product D4/H4 + 8 个 _raw 字段 (V2 尺寸原始值溯源)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'\[Column\("d4_mm"\)\].*\[Column\("h4_mm_raw"\)\]',
    },
    {
        "id": "V2-DS-5", "level": "V2",
        "title": "CrossReference.Oem2/SortOrder/MachineType/IsPublished (V2 全集)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'\[Column\("sort_order"\)\]\s*public int\s*SortOrder',
    },
    {
        "id": "V2-DS-6", "level": "V2",
        "title": "CrossReference.RowVersion (xmin 乐观锁)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'CrossReference.*\[Column\("xmin"\)\]\s*public uint\s*RowVersion',
    },
    {
        "id": "V2-DS-7", "level": "V2",
        "title": "MachineApplication.MachineCategory (V2 机型分类双轨)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'\[Column\("machine_category"\)\]\s*public string\?\s*MachineCategory',
    },
    {
        "id": "V2-DS-8", "level": "V2",
        "title": "ProductImage.OemNo3 + ImageRole (V2 主图/详情图分层)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'\[Column\("oem_no_3"\)\]\s*public string\?\s*OemNo3.*\[Column\("image_role"\)\]',
    },
    {
        "id": "V2-DS-9", "level": "V2",
        "title": "CrossReference/MachineApplication 导航属性 Product (约定式, v3 任务 1.1.1 待改 HasOne)",
        "file": "backend/src/SakuraFilter.Core/Entities/Product.cs",
        "fix_pattern": r'public\s+Product\?\s+Product\s*\{\s*get;\s*set;\s*\}',
    },
    {
        "id": "V2-DS-10", "level": "V2",
        "title": "products 表部分唯一索引 (WHERE mr_1 IS NOT NULL)",
        "file": "backend/src/SakuraFilter.Infrastructure/Data/ProductDbContext.cs",
        "fix_pattern": r'idx_products_mr_1_unique.*HasFilter\("mr_1 IS NOT NULL"\)',
    },
    # ----- V2-VL 校验逻辑 (8 项): MR.1 + 枚举 + 长度 -----
    {
        "id": "V2-VL-1", "level": "V2",
        "title": "MR1_REQUIRED 必填校验",
        # v28-4 P0 修复: MR1 校验实际在 SakuraFilter.Core/Validation/Mr1Validator.cs (AdminProductService L1200 调用 Mr1Validator.Normalize)
        "file": "backend/src/SakuraFilter.Core/Validation/Mr1Validator.cs",
        "fix_pattern": r'MR1_REQUIRED:\s*MR\.1\s*必填',
    },
    {
        "id": "V2-VL-2", "level": "V2",
        "title": "MR1_FORMAT_INVALID 格式校验 (1-10 位字母数字)",
        # v28-4 P0 修复: MR1 校验实际在 SakuraFilter.Core/Validation/Mr1Validator.cs
        "file": "backend/src/SakuraFilter.Core/Validation/Mr1Validator.cs",
        "fix_pattern": r'MR1_FORMAT_INVALID.*1-10\s*位字母数字',
    },
    {
        "id": "V2-VL-3", "level": "V2",
        "title": "MR1_ALREADY_EXISTS 唯一性检查 (部分唯一索引)",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r'MR1_ALREADY_EXISTS.*MR\.1\s*已存在',
    },
    {
        "id": "V2-VL-4", "level": "V2",
        "title": "MR1 最大长度 10 (非 100)",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r'\("Mr1",\s*form\.Mr1,\s*10\)',
    },
    {
        "id": "V2-VL-5", "level": "V2",
        "title": "AdminProductService machine_type 枚举校验 (5 类)",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r'MACHINE_TYPE_INVALID.*machine_type\s*必须为',
    },
    {
        "id": "V2-VL-6", "level": "V2",
        "title": "Oem3 长度校验 (3 位)",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductService.cs",
        "fix_pattern": r'OEM3_LENGTH_INVALID|oem_no_3.*长度.*3',
    },
    {
        "id": "V2-VL-7", "level": "V2",
        "title": "PublicProductController slug 长度 200 校验",
        "file": "backend/src/SakuraFilter.Api/Controllers/PublicProductController.cs",
        "fix_pattern": r'slug\.Length\s*>\s*200',
    },
    {
        "id": "V2-VL-8", "level": "V2",
        "title": "ProblemDetailsFactory V2 统一错误码 (MR1_*/IMAGE_*/MACHINE_*)",
        "file": "backend/src/SakuraFilter.Api/Services/ProblemDetailsFactory.cs",
        "fix_pattern": r'MR1_REQUIRED|MR1_FORMAT_INVALID|IMAGE_ROLE_SLOT_MISMATCH',
    },
    # ----- V2-IMG 图片命名 (6 项): BuildKeyAsync + image_role + 路径穿越 -----
    {
        "id": "V2-IMG-1", "level": "V2",
        "title": "BuildKeyAsync 方法 (V2 主图/详情图分层命名)",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs",
        "fix_pattern": r'public\s+async\s+Task<string>\s+BuildKeyAsync',
    },
    {
        "id": "V2-IMG-2", "level": "V2",
        "title": "BuildKeyAsync 路径穿越防御 (字符白名单)",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs",
        "fix_pattern": r'char\.IsLetterOrDigit\(c\)\s*\|\|\s*c\s*==\s*\'-\'\s*\|\|\s*c\s*==\s*\'_\'',
    },
    {
        "id": "V2-IMG-3", "level": "V2",
        "title": "imageRole 校验 (primary/detail 二选一)",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs",
        "fix_pattern": r'imageRole\s*!=\s*"primary"\s*&&\s*imageRole\s*!=\s*"detail"',
    },
    {
        "id": "V2-IMG-4", "level": "V2",
        "title": "IMAGE_ROLE_SLOT_MISMATCH slot 一致性校验",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs",
        "fix_pattern": r'IMAGE_ROLE_SLOT_MISMATCH.*主图\s*slot\s*必须为\s*1',
    },
    {
        "id": "V2-IMG-5", "level": "V2",
        "title": "IMAGE_PRIMARY_DUPLICATE 唯一约束软校验",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs",
        "fix_pattern": r'IMAGE_PRIMARY_DUPLICATE.*uq_product_images_primary',
    },
    {
        "id": "V2-IMG-6", "level": "V2",
        "title": "GetNamingFieldAsync system_settings + IMemoryCache 5min",
        "file": "backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs",
        "fix_pattern": r'GetNamingFieldAsync.*_cache\.Set\(cacheKey,\s*result,\s*CacheTtl\)',
    },
    # ----- V2-SRCH 搜索 (5 项): Mr1IndexDoc + ProductIndexDoc + brand 优先 -----
    {
        "id": "V2-SRCH-1", "level": "V2",
        "title": "Mr1IndexDoc record (V2 主搜索索引文档, 嵌套结构)",
        "file": "backend/src/SakuraFilter.Search/ISearchProvider.cs",
        "fix_pattern": r'public\s+record\s+Mr1IndexDoc\(',
    },
    {
        "id": "V2-SRCH-2", "level": "V2",
        "title": "Mr1IndexDoc 嵌套结构 (OemList + MachineList 数组, 替代扁平 ProductIndexDoc)",
        "file": "backend/src/SakuraFilter.Search/ISearchProvider.cs",
        "fix_pattern": r'List<OemListItem>\s+OemList.*List<MachineListItem>\s+MachineList',
    },
    {
        "id": "V2-SRCH-3", "level": "V2",
        "title": "BrandSortOrder 字段 (类竞价排名, brand 优先)",
        "file": "backend/src/SakuraFilter.Search/ISearchProvider.cs",
        "fix_pattern": r'BrandSortOrder',
    },
    {
        "id": "V2-SRCH-4", "level": "V2",
        "title": "Mr1IndexDoc OemListPublishedBrands/No3s 扁平化冗余字段",
        "file": "backend/src/SakuraFilter.Search/ISearchProvider.cs",
        "fix_pattern": r'OemListPublishedBrands.*OemListPublishedNo3s',
    },
    {
        "id": "V2-SRCH-5", "level": "V2",
        "title": "SyncSearchIndexAsync 调用 BuildMr1DocumentAsync 构建嵌套文档",
        "file": "backend/src/SakuraFilter.Etl/EtlImportService.cs",
        "fix_pattern": r'meili\.BuildMr1DocumentAsync',
    },
    # ----- V2-SEO SEO URL (5 项): 301 + slug + Razor Pages -----
    {
        "id": "V2-SEO-1", "level": "V2",
        "title": "PublicProductController 301 永久重定向 (旧 URL → SEO URL)",
        "file": "backend/src/SakuraFilter.Api/Controllers/PublicProductController.cs",
        "fix_pattern": r'Status301MovedPermanently|RedirectPermanent',
    },
    {
        "id": "V2-SEO-2", "level": "V2",
        "title": "Razor Pages Detail.cshtml (SSR 详情页 @page /products/{pn1}/{pn2}/{brand}/{oem3})",
        "file": "backend/src/SakuraFilter.Api/Pages/Products/Detail.cshtml",
        "fix_pattern": r'@page\s+"/products/\{pn1\}/\{pn2\}/\{brand\}/\{oem3\}"',
    },
    {
        "id": "V2-SEO-3", "level": "V2",
        "title": "SitemapEndpoints.xml 生成 (SEO URL 落地)",
        "file": "backend/src/SakuraFilter.Api/Endpoints/SitemapEndpoints.cs",
        "fix_pattern": r'sitemap\.xml|BuildProductUrl',
    },
    {
        "id": "V2-SEO-4", "level": "V2",
        "title": "Detail.cshtml.cs OnGetAsync 404 友好页",
        "file": "backend/src/SakuraFilter.Api/Pages/Products/Detail.cshtml.cs",
        "fix_pattern": r'StatusCode\s*=\s*404|NotFound',
    },
    {
        "id": "V2-SEO-5", "level": "V2",
        "title": "buildProductUrl 前端工具 (与后端逻辑对齐, 降级走 /product/{oem} 301)",
        "file": "frontend/src/utils/build-product-url.ts",
        "fix_pattern": r'export\s+function\s+buildProductUrl',
    },
    # ----- V2-ETL ETL (6 项): mr_1 主键 + 枚举 + 计数器 -----
    {
        "id": "V2-ETL-1", "level": "V2",
        "title": "LoadExistingOemMapAsync 改用 mr_1 (替代 oem_no_normalized)",
        "file": "backend/src/SakuraFilter.Etl/EtlImportService.cs",
        "fix_pattern": r'SELECT id, mr_1 FROM products WHERE mr_1 IS NOT NULL',
    },
    {
        "id": "V2-ETL-2", "level": "V2",
        "title": "IncrSkippedMissingMr1 计数器 (V2 替代 IncrSkippedMissingOem)",
        "file": "backend/src/SakuraFilter.Etl/EtlImportService.cs",
        "fix_pattern": r'public\s+void\s+IncrSkippedMissingMr1',
    },
    {
        "id": "V2-ETL-3", "level": "V2",
        "title": "ProcessXrefBatchAsync mr_1 关联 + machine_type 枚举预检",
        "file": "backend/src/SakuraFilter.Etl/EtlImportService.cs",
        "fix_pattern": r'MACHINE_TYPE_INVALID.*不在白名单',
    },
    {
        "id": "V2-ETL-4", "level": "V2",
        "title": "ImportAppsAsync mr_1 关联 + machine_category 枚举预检",
        "file": "backend/src/SakuraFilter.Etl/EtlImportService.cs",
        "fix_pattern": r'MACHINE_CATEGORY_INVALID.*不在白名单',
    },
    {
        "id": "V2-ETL-5", "level": "V2",
        "title": "ImportProductsAsync mr_1 必填校验 + oem_no_normalized 派生",
        "file": "backend/src/SakuraFilter.Etl/EtlImportService.cs",
        "fix_pattern": r'NormalizeMr1ToOemNo',
    },
    {
        "id": "V2-ETL-6", "level": "V2",
        "title": "AllowedMachineCategories 枚举白名单常量 (5 类)",
        "file": "backend/src/SakuraFilter.Etl/EtlImportService.cs",
        "fix_pattern": r'AllowedMachineCategories\s*=\s*\{[^}]*"agriculture"[^}]*"commercial"[^}]*"construction"[^}]*"industrial"[^}]*"others"',
    },
    # ----- V2-FE 前端 (4 项): JSON 数据岛 + Vue mount + buildProductUrl + 多入口 -----
    {
        "id": "V2-FE-1", "level": "V2",
        "title": "Detail.cshtml JSON 数据岛 (替代 window.__PRODUCT__, XSS 防御)",
        "file": "backend/src/SakuraFilter.Api/Pages/Products/Detail.cshtml",
        "fix_pattern": r'<script\s+type="application/json"\s+id="product-data">',
    },
    {
        "id": "V2-FE-2", "level": "V2",
        "title": "Detail.cshtml Vue 挂载点独立 (#seo-content 与 #gallery-app 分离)",
        "file": "backend/src/SakuraFilter.Api/Pages/Products/Detail.cshtml",
        "fix_pattern": r'<div\s+id="seo-content">.*<div\s+id="gallery-app">',
    },
    {
        "id": "V2-FE-3", "level": "V2",
        "title": "product-detail-client.ts (多入口 Vue client mount, 非 hydration)",
        "file": "frontend/src/product-detail-client.ts",
        "fix_pattern": r'createApp.*\.mount|safeMount',
    },
    {
        "id": "V2-FE-4", "level": "V2",
        "title": "vite.config.ts 多入口 + manualChunks vue 拆分",
        "file": "frontend/vite.config.ts",
        "fix_pattern": r'product-detail-client.*manualChunks.*vue',
    },
    # ----- V2-SEC 安全 (3 项): XSS + HMAC Ticks + 错误码 -----
    {
        "id": "V2-SEC-1", "level": "V2",
        "title": "Detail.cshtml 禁用 @Html.Raw (XSS 防御, 用 @Json.Serialize 自动 HTML 编码)",
        "file": "backend/src/SakuraFilter.Api/Pages/Products/Detail.cshtml",
        "fix_pattern": r'禁止使用\s*@Html\.Raw,\s*防止',
    },
    {
        "id": "V2-SEC-2", "level": "V2",
        "title": "CursorHmac 签名载荷改用 mr_1 (替代 long Id, 不暴露内部自增 Id)",
        "file": "backend/src/SakuraFilter.Api/Services/CursorHmac.cs",
        "fix_pattern": r'mr1 载荷不能为空|string\s+mr1',
    },
    {
        "id": "V2-SEC-3", "level": "V2",
        "title": "XssSanitizer (产品 Remark/ProductName1 富文本字段清洗)",
        "file": "backend/src/SakuraFilter.Api/Services/XssSanitizer.cs",
        "fix_pattern": r'class\s+XssSanitizer|Sanitize',
    },
]


def grep_file(file_path, pattern):
    """在文件中搜索正则, 返回匹配数 (-1 = 文件不存在)"""
    try:
        content = Path(REPO / file_path).read_text(encoding="utf-8")
        return len(re.findall(pattern, content, re.MULTILINE | re.DOTALL))
    except FileNotFoundError:
        return -1


def scan_phase():
    """阶段 1: 扫描代码层, 验证修复模式存在"""
    print("\n【阶段 1】扫描修复模式 (代码层 grep)")
    print("-" * 70)
    results = []
    for check in REGRESSION_CHECKS:
        cnt = grep_file(check["file"], check["fix_pattern"])
        ok = cnt > 0
        results.append((check, ok, cnt))
        status = "[OK]  " if ok else "[FAIL]"
        print(f"  {status} {check['id']:<6} [{check['level']}] {check['title']}")
        if ok:
            print(f"         修复模式匹配 {cnt} 处")
        else:
            print(f"         ⚠ 回归! 文件: {check['file']}")
    return results


def health_phase():
    """阶段 2: 系统健康检查"""
    print("\n【阶段 2】系统健康检查")
    print("-" * 70)
    endpoints = [
        ("/health/live", "存活探针"),
        ("/health/ready", "就绪探针"),
        ("/api/search/health", "Meilisearch 健康"),
        ("/api/admin/dict/oem-no3s?limit=5", "dict_oem_no3 (P0-1 验证)"),
    ]
    ok_count = 0
    for path, desc in endpoints:
        url = f"{BASE}{path}"
        req = urllib.request.Request(url, headers=HEADERS, method="GET")
        start = time.time()
        try:
            with urllib.request.urlopen(req, timeout=5) as r:
                elapsed = time.time() - start
                print(f"  [OK]  GET  {path:<40} HTTP {r.status} time={elapsed:.2f}s  {desc}")
                ok_count += 1
        except urllib.error.HTTPError as e:
            elapsed = time.time() - start
            print(f"  [FAIL] GET {path:<40} HTTP {e.code} time={elapsed:.2f}s  {desc}")
        except Exception as e:
            print(f"  [FAIL] GET {path:<40} ERR {type(e).__name__}: {e}")
    return ok_count, len(endpoints)


def report_phase(scan_results, health_ok, health_total):
    """阶段 3: 汇总报告"""
    print("\n" + "=" * 70)
    print("【汇总报告】")
    print("=" * 70)
    scan_ok = sum(1 for _, ok, _ in scan_results if ok)
    scan_fail = len(scan_results) - scan_ok
    by_level = {}
    for check, ok, _ in scan_results:
        level = check["level"]
        if level not in by_level:
            by_level[level] = {"ok": 0, "fail": 0}
        if ok:
            by_level[level]["ok"] += 1
        else:
            by_level[level]["fail"] += 1

    print(f"  扫描: {scan_ok}/{len(scan_results)} 修复模式有效, {scan_fail} 回归")
    for level in ["P1", "P2", "P3", "V2"]:
        if level in by_level:
            s = by_level[level]
            print(f"    {level}: {s['ok']} 通过 / {s['fail']} 回归", end="")
            if s["fail"] > 0:
                print("  ⚠ 需修复")
            else:
                print()
    print(f"  健康: {health_ok}/{health_total} 端点正常")
    print()
    if scan_fail == 0 and health_ok == health_total:
        print("  [RESULT] 全部回归测试通过, P1/P2/P3/V2 修复有效")
        return 0
    else:
        print("  [RESULT] ⚠ 存在回归, 需立即修复!")
        return 1


def main():
    scan_only = "--scan" in sys.argv
    print("=" * 70)
    print("P1/P2/P3 + V2 修复回归测试 (防再次引入)")
    print("=" * 70)

    scan_results = scan_phase()
    if scan_only:
        health_ok = health_total = 0
        print("\n  [--scan 模式] 跳过健康检查")
    else:
        health_ok, health_total = health_phase()

    exit_code = report_phase(scan_results, health_ok, health_total)
    sys.exit(exit_code)


if __name__ == "__main__":
    main()
