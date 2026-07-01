# SPIKE-REPORT-day8.1 — 后台产品管理 API (7 分区表单)

**日期**: 2026-07-01
**作者**: SakuraFilter spike
**范围**: Day 8.1 后台产品管理后端 (规格: 后台新增产品格式 7 分区表单)
**前置**: Day 7.x (ETL/搜索/死信) 全部就绪,Day 8 进入"运营录入 + 后台管理"阶段

---

## TL;DR

| 模块 | 端点 | 状态 | 关键证据 |
|------|------|------|---------|
| 1 | 新增产品 POST /api/admin/products | ✅ | 7 分区全字段持久化,201 |
| 2 | 列表分页 GET /api/admin/products | ✅ | keyword + type + includeDiscontinued 过滤 |
| 3 | 详情 GET /api/admin/products/{id} | ✅ | 含 xref + machine_application + images |
| 4 | 更新 PUT /api/admin/products/{id} | ✅ | 字段变更跟踪,history 留痕 |
| 5 | 软删除 DELETE | ✅ | is_discontinued=true 物理保留 |
| 6 | 恢复 POST /restore | ✅ | 反向操作 |
| 7 | 图片上传 POST /images/{slot} | ✅ | MinIO + 1-6 slot 范围校验 |
| 8 | 图片删除/列表 | ✅ | 主图同步 products.image_key |
| 9 | CORS preflight | ✅ | ACAO=http://localhost:5173 |

**核心决策**:
- **OEM 归一化** = 大写 + 去空格/分隔符 → 大小写不敏感的唯一性
- **跨表事务** = Product + CrossReference + MachineApplication 同事务
- **历史留痕** = product_history.change_type ∈ create/update/discontinue/restore
- **软删除** = is_discontinued flag 保留历史(规格硬约束)
- **图片存储抽象** = IObjectStorage 切 MinIO(MVP)/ AliyunOSS
- **覆盖上传 key 稳定** = products/{oem_norm}/{oem_norm}-{slot}.{ext} 避免废弃对象

---

## 1. 背景与目标

### 1.1 业务背景
- ETL 导入解决了"批量数据上线"(1949 产品 + 36 xrefs + 53 apps)
- 运营日常需**手工录入/补录/纠错**新产品(漏网 OEM/新机型适配)
- 规格 `后台新增产品格式 7 分区.xlsx` 明确 7 个功能分区,后台表单必须一次性提交

### 1.2 目标
- ✅ 一次性提交整个产品(7 分区全字段)
- ✅ OEM 唯一性 + 归一化(防重复录入)
- ✅ 软删除(规格要求:下架但保留历史)
- ✅ 图片 1-6 张槽位(slot)管理
- ✅ CORS 让前端 Vite dev server (localhost:5173) 直连

---

## 2. 7 分区表单设计

| 分区 | 字段 | 表 | 备注 |
|------|------|------|------|
| 1 主信息 | oem2*, productName1, mr1, isPublished, remark | products | OEM 2 = 主号 |
| 2 交叉引用 | productName1, oemBrand, oemNo3 | cross_references | 后台表单一次性全量提交 |
| 3 尺寸 | d1~d4, h1~h4, d7/d8Thread, noCheck/BypassValves | products | mm 单位 |
| 4 图片 | 1-6 张图, slot 1-6 | product_images (新表) | MinIO key: products/{oem_norm}/{oem_norm}-{slot}.{ext} |
| 5 技术参数 | media, mediaModel, bypassValveLr/Hr, efficiency1/2, bypassPressure, collapsePressureBar, sealingMaterial, tempRange | products | BypassPressure 改 NUMERIC |
| 6 包装 | qtyPerCarton, weightKgs, 箱尺寸, masterBox | products | volumePerCartonM3 自动派生 |
| 7 机型适配 | 25 字段(含 18 扩展: engineDisplacement, gvwr, tonnage, cabinType 等) | machine_applications | 18 字段由 migration 016 新增 |

**新增字段数**:
- products 表: +19 列(分区 1/3/5/6)
- machine_applications 表: +18 列(分区 7)
- 新表 product_images: 10 列(分区 4)

**migration 文件**: `backend/migrations/016_add_product_form_fields.sql`

---

## 3. 关键代码

### 3.1 DTO 设计 — ProductFormDto
**文件**: [ProductFormDto.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Core/DTOs/ProductFormDto.cs)

设计要点:
- 单 DTO 对应整个产品(7 分区在 1 个 record),一次 POST 完成
- 所有字段 nullable,允许部分填写
- 嵌套 `XrefInput` / `MachineAppInput` 一次性提交子表
- `decimal? BypassPressure` 而非 `string?` — NUMERIC 列直接绑定,避免字符串转换

### 3.2 服务层 — AdminProductService
**文件**: [AdminProductService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/AdminProductService.cs)

**关键设计**:

```csharp
// OEM 归一化: 大小写不敏感 + 去分隔符
private static string NormalizeOem(string oem) =>
    oem.Trim().ToUpperInvariant()
       .Replace(" ", "").Replace("-", "").Replace("/", "")
       .Replace("(", "").Replace(")", "").Replace(".", "");

// 体积自动派生: L*W*H mm³ → m³
private static decimal? DeriveVolume(decimal? l, decimal? w, decimal? h)
{
    if (l is null || w is null || h is null) return null;
    return Math.Round((l.Value * w.Value * h.Value) / 1_000_000_000m, 6);
}

// Type 派生: 名称含 oil/fuel/air/cabin 自动归类, 否则 others
private static string DeriveTypeFromName(string? productName3) { ... }
```

**关键流程**:
1. `CreateAsync`: ValidateForm → 唯一性检查 → Product INSERT → xref/app BATCH INSERT → history INSERT
2. `UpdateAsync`: 字段变更跟踪(`Track<T>`) → xref/app 全量替换(后台表单语义) → history UPDATE 留痕
3. `DeleteAsync`: is_discontinued=true, 物理保留
4. `RestoreAsync`: is_discontinued=false

### 3.3 图片服务 — AdminProductImageService
**文件**: [AdminProductImageService.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs)

```csharp
// S3 key 命名: 稳定, 同 slot+ext 覆盖上传 key 不变
public static string BuildKey(string oemNormalized, short slot, string ext) =>
    $"products/{oemNormalized}/{oemNormalized}-{slot}.{ext}";
```

**主图(slot=1)同步策略**:
- 上传: product.image_key 同步, image_status='ready'
- 删除: 清 image_key, image_status='pending'
- WHY: 兼容 Day 4 之前的 search/detail 端点(ProductDetailDto.ImageKey)

### 3.4 API 端点
**文件**: [Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L480-L606)

| 端点 | 方法 | 用途 | 状态码 |
|------|------|------|--------|
| /api/admin/products | POST | 新增产品 | 201 / 400 / 409 |
| /api/admin/products | GET | 列表分页 | 200 |
| /api/admin/products/{id} | GET | 详情(含 xref+app+images) | 200 / 404 |
| /api/admin/products/{id} | PUT | 更新 | 200 / 400 / 404 |
| /api/admin/products/{id} | DELETE | 软删除 | 200 / 404 / 409 |
| /api/admin/products/{id}/restore | POST | 恢复 | 200 / 404 / 409 |
| /api/admin/products/{id}/images/{slot} | POST | 上传图 | 200 / 400 / 404 |
| /api/admin/products/{id}/images/{slot} | DELETE | 删图 | 200 / 400 / 404 |
| /api/admin/products/{id}/images | GET | 6 张图列表 | 200 |

**错误处理**:
- `KeyNotFoundException` → 404
- `ArgumentException` → 400 (参数错)
- `InvalidOperationException` → 409 (业务冲突,如重复 OEM/重复下架)

### 3.5 CORS 配置
**WHY 显式 AllowedOrigins 而非 AllowAnyOrigin**: `AllowAnyOrigin + AllowCredentials` 浏览器会拒绝,必须白名单

```csharp
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };
builder.Services.AddCors(o => o.AddPolicy("SakuraFilterCors", p =>
    p.WithOrigins(allowedOrigins)
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials()));
```

### 3.6 Npgsql 兼容性修复
**问题**: migration 016 加了 `bypass_pressure NUMERIC` 列, 但 `DateTime Kind=Unspecified` (前端传 "2007-01-01") 写入 timestamptz 列时 Npgsql 8.x 抛异常:
```
Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'
```

**修复**: [Program.cs](file:///d:/projects/sakurafilter/backend/src/SakuraFilter.Api/Program.cs#L15-L19)
```csharp
// Npgsql 6+: 默认只接受 DateTime Kind=Utc
//   Day 8.1: machine_application.production_date_start 等字段是 DATE 类型,
//            前端 JSON 传 "2007-01-01" 被 .NET 当 Kind=Unspecified
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
```

---

## 4. 端到端测试

**测试文件**: [spike-test/_test_day81_admin_products.py](file:///d:/projects/sakurafilter/spike-test/_test_day81_admin_products.py)

**结果**: 9/9 全部通过

```
[1] POST /api/admin/products: 201
  - 分区 1-7 字段全字段持久化 ✓
  - machine_application 18 扩展字段全字段 ✓
  - product_history: [('create', 'system')]
  - 派生体积 volumePerCartonM3=0.0441 (自动计算)

[2] 重复 Oem2: 409  OEM 唯一性约束 ✓
    归一化检测: 409 (大写 vs 小写)  OEM 归一化 ✓

[3] GET /api/admin/products (keyword=DAY81): 200
    - 列表+关键词过滤 ✓
    - 按 type=oil: total=1  type 过滤 ✓

[4] GET /api/admin/products/{id}: 200
    - 详情含 xref + machine_application ✓

[5] PUT /api/admin/products/{id}: 200
    - mr1: OC-90 → OC-90-V2 ✓
    - isPublished: true → False ✓
    - xref: 2 → 1 (全量替换) ✓
    - history: ['create', 'update']  history 留痕 ✓

[6a] DELETE /api/admin/products/{id}: 200
    - 软删除(物理保留) ✓
    - 默认列表过滤已下架 ✓
    - includeDiscontinued=true 显示已下架 ✓
[6b] POST /api/admin/products/{id}/restore: 200
    - 恢复 ✓

[7] GET /api/admin/products/{id}/images (无图): 200
    - 空图片列表 ✓

[8a] slot=0 越界: 400  slot 范围 1-6 校验 ✓
[8b] 上传 slot=1: 200
    - image_key=products/DAY81TEST001/DAY81TEST001-1.png
    - product.image_key / image_status 同步 ✓
    - 覆盖上传保持 key 稳定 (设计预期: 避免废弃对象) ✓
    - 列表返回 1 张图 (slot=1) ✓
    - 删除 slot=1 ✓
    - product.image_key/image_status 清空 ✓

[9] CORS preflight: 204
    - ACAO=http://localhost:5173 ACAC=true  ✓
```

---

## 5. 关键决策与权衡

| 决策 | 选项 | 选择 | 理由 |
|------|------|------|------|
| 软删除 vs 硬删除 | 软 / 硬 | **软** | 规格硬约束, 历史产品保留, 影响搜索排序 |
| OEM 归一化 | 大小写敏感 / 不敏感 | **不敏感** | OEM 录入"ABC-123" vs "abc123" 应是同一产品 |
| xref 更新语义 | 增量 / 全量 | **全量替换** | 后台表单语义: 用户明确编辑"完整列表" |
| 图片 key 命名 | 时间戳 / 稳定 | **稳定** | 避免产生废弃对象(同 slot+ext 覆盖) |
| 跨表写入 | 单 SQL / 多次 SaveChanges | **多次** | 失败回滚粒度清晰, 历史 INSERT 与业务事务分离 |
| volumePerCartonM3 | 手填 / 自动 | **自动派生** | 减少录入错误, L*W*H mm³ → m³ |
| Type 派生 | 手动选 / 自动 | **可选**(默认派生) | 用户可手动覆盖, 否则从 productName 推断 |

---

## 6. 部署清单

- [ ] 应用 migration 016: `psql -f backend/migrations/016_add_product_form_fields.sql`
- [ ] MinIO 启动 + bucket `sakurafilter` 创建
- [ ] CORS 配置 `appsettings.json`:
  ```json
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173", "http://localhost:3000", "https://admin.sakurafilter.com"]
  }
  ```
- [ ] MinIO 配置 `appsettings.json`:
  ```json
  "Minio": {
    "Endpoint": "minio.example.com:9000",
    "AccessKey": "...",
    "SecretKey": "...",
    "BucketName": "sakurafilter",
    "ImageMaxBytes": 10485760
  }
  ```

---

## 7. 已知限制

1. **无权限控制**: 当前 X-User header 仅作 audit 字段, **未做角色鉴权**。生产环境必须接 OIDC/JWT。
2. **无并发锁**: 后台两个运营同时编辑同一产品,后者覆盖前者。需乐观锁(`updated_at` 检查)。
3. **图片未做格式/尺寸校验**: 仅 MIME + 大小限制, 需在前端/后端加 ImageSharp 处理(自动转 webp、缩放 2000px)。
4. **无产品批量导入**: 当前仅手动单条录入, Day 8.4 可考虑 Excel 模板导入。
5. **history 表膨胀**: create/update 每次都写, 高频编辑时增长快。已通过 `HistoryCleanupService` (默认永久) 控制。

---

## 8. 下一步 (Day 8.2-8.4 候选)

### Day 8.2: 后台图片管理增强
- 图片裁剪/缩略图(主图 + 缩略图)
- 批量上传(6 张一次拖拽)
- 图片审核流(未通过/已通过状态)

### Day 8.3: 后台产品搜索 + 高级筛选
- type 列表 + 多选
- 尺寸范围筛选(D1 >= 90 AND D1 <= 100)
- xref 反向搜索(找"哪些产品兼容某 OEM")
- 已下架 / 已发布 状态筛选

### Day 8.4: 批量导入后台版
- Excel 模板(7 分区)
- 解析后走 ETL upsert 路径(复用)
- 错误回显(行号 + 错误原因)

---

## 9. 改进建议

💡 **可抽取的公共逻辑**:
1. `Track<T>` 字段变更跟踪 已写好, 可抽到 `SakuraFilter.Core/Common/EntityTracker.cs` 给 MachineApplication/CrossReference 复用
2. `NormalizeOem` 在 ETL (etl_clean.py) 和 C# 重复, 可统一为 PostgreSQL 函数(`oem_no_normalize(text)`)
3. AdminProductImageService 的 key 生成策略 应放 `IObjectStorage` 接口(`BuildKey(productId, slot, ext)`), 让 MinIO/OSS 各自实现
4. 错误处理 `KeyNotFoundException → 404 / ArgumentException → 400 / InvalidOperationException → 409` 在每个端点重复, 可写全局 ExceptionHandler middleware

💡 **可优化项**:
1. ProductDetailDto 的 `Images` 字段在 GetByIdAsync 里始终返回 `new List<ProductImageInfo>()`, 实际由 `Program.cs` 端点合并。考虑让服务方法签名接受 optional image loader
2. `xref` / `apps` 全量替换时如某条无变化, 也会写 history, 可对比变化再决定是否写
3. 列表分页用 offset + limit, 1M 数据下需 keyset 升级(Day 7.8 已对死信用过此模式)

---

## 10. 文档与索引

- API 端点: `backend/src/SakuraFilter.Api/Program.cs` lines 480-606
- 服务层: `backend/src/SakuraFilter.Api/Services/AdminProductService.cs`
- 图片服务: `backend/src/SakuraFilter.Api/Services/AdminProductImageService.cs`
- DTO: `backend/src/SakuraFilter.Core/DTOs/ProductFormDto.cs`
- Migration: `backend/migrations/016_add_product_form_fields.sql`
- 测试: `spike-test/_test_day81_admin_products.py`
- E2E 验证: 9/9 全部通过, 含 MinIO 真实上传 + CORS preflight

