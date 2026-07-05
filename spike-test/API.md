# SakuraFilter API

**Version**: 1.0.0

工业/汽车滤清器产品管理平台 API

## 模块说明
- **认证**: JWT 登录/刷新/登出/用户管理
- **公开搜索**: 前台产品搜索/详情 (无需认证)
- **后台产品管理**: CRUD (需 admin 角色)
- **字典管理**: 8 类字典 CRUD (需认证)
- **ETL**: 数据导入/状态/进度 (X-Admin-Token)
- **运维**: 健康检查/指标/性能告警

## 认证方式
1. **JWT Bearer** (推荐): 登录 /api/auth/login 获取 token, 放入 Authorization: Bearer {token}
2. **X-Admin-Token** (ETL/CI 备用): 放入 X-Admin-Token: {token}

**Contact**: SakuraFilter Team <admin@sakurafilter.dev>

---

## 认证方式

### X-Admin-Token

- **Type**: apiKey
- **Description**: Day 8.4: dev 静态 token, 从 appsettings.json 的 Auth:DevStaticToken 读

### Bearer

- **Type**: http
- **Scheme**: bearer
- **Bearer Format**: JWT
- **Description**: JWT Bearer token. 格式: Bearer {token} (登录 /api/auth/login 获取)

## 端点索引

共 **89** 个路径, **112** 个端点, 分布在 **18** 个模块:

- [Admin Auth](#admin-auth) (1 端点)
- [Admin Dead-letter](#admin-dead-letter) (3 端点)
- [Admin Dict](#admin-dict) (57 端点)
- [Admin Etl](#admin-etl) (8 端点)
- [Admin Perf](#admin-perf) (1 端点)
- [Admin Products](#admin-products) (12 端点)
- [Auth](#auth) (5 端点)
- [Default](#default) (1 端点)
- [Etl](#etl) (4 端点)
- [Health](#health) (2 端点)
- [Perf](#perf) (2 端点)
- [Products](#products) (1 端点)
- [PublicCompare](#publiccompare) (1 端点)
- [PublicMachineBrands](#publicmachinebrands) (1 端点)
- [PublicProduct](#publicproduct) (2 端点)
- [PublicSearch](#publicsearch) (2 端点)
- [Search](#search) (2 端点)
- [Users](#users) (7 端点)

## Admin Auth

### GET /api/admin/auth/status

**Auth Token 轮转状态查询 (current/previous 长度 + 轮转时间, 不暴露完整 token)**

**Responses**:

- `200`: OK

---

## Admin Dead-letter

### GET /api/admin/dead-letter

**死信队列分页查询 (keyset cursor, 支持 operation/since/recovery_count 过滤)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `limit` | query | integer | ✗ |  |
| `operation` | query | string | ✗ |  |
| `since` | query | string | ✗ |  |
| `cursor` | query | string | ✗ |  |
| `min_recovery_count` | query | integer | ✗ |  |
| `max_recovery_count` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dead-letter/recover-batch

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `operation` | query | string | ✗ |  |
| `lastErrorContains` | query | string | ✗ |  |
| `maxRecoveryCount` | query | integer | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dead-letter/{id}/recover

**死信单条恢复 (移回 pending + advisory lock 串行化)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

## Admin Dict

### GET /api/admin/dict/_schema

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/engines

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/engines

**Request Body**:

- Content-Type: `application/json` (schema: `EngineCreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/engines/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `EngineReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/engines/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/engines/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/engines/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `EngineUpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/engines/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/machines

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/machines

**Request Body**:

- Content-Type: `application/json` (schema: `MachineCreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/machines/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `MachineReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/machines/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/machines/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/machines/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `MachineUpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/machines/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/medias

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/medias

**Request Body**:

- Content-Type: `application/json` (schema: `MediaCreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/medias/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `MediaReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/medias/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/medias/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/medias/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `MediaUpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/medias/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/oem-brands

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/oem-brands

**Request Body**:

- Content-Type: `application/json` (schema: `OemBrandCreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/oem-brands/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `OemBrandReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/oem-brands/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/oem-brands/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/oem-brands/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `OemBrandUpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/oem-brands/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/oem-no3s

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/oem-no3s

**Request Body**:

- Content-Type: `application/json` (schema: `OemNo3CreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/oem-no3s/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `OemNo3ReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/oem-no3s/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/oem-no3s/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/oem-no3s/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `OemNo3UpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/oem-no3s/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/product-name1s

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/product-name1s

**Request Body**:

- Content-Type: `application/json` (schema: `ProductName1CreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/product-name1s/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `ProductName1ReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/product-name1s/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/product-name1s/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/product-name1s/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `ProductName1UpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/product-name1s/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/product-name2s

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/product-name2s

**Request Body**:

- Content-Type: `application/json` (schema: `ProductName2CreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/product-name2s/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `ProductName2ReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/product-name2s/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/product-name2s/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/product-name2s/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `ProductName2UpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/product-name2s/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/types

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `includeDeleted` | query | boolean | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/types

**Request Body**:

- Content-Type: `application/json` (schema: `TypeCreateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/types/reorder

**Request Body**:

- Content-Type: `application/json` (schema: `TypeReorderRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/dict/types/typeahead

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `q` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/dict/types/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/dict/types/{id}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `TypeUpdateRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/dict/types/{id}/restore

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

## Admin Etl

### GET /api/admin/etl/history

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `limit` | query | integer | ✗ |  |
| `status` | query | string | ✗ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/etl/history/aggregate

**Responses**:

- `200`: OK

---

### POST /api/admin/etl/pause

**Responses**:

- `200`: OK

---

### GET /api/admin/etl/progress

**Responses**:

- `200`: OK

---

### GET /api/admin/etl/progress/stream

**Responses**:

- `200`: OK

---

### POST /api/admin/etl/resume

**Responses**:

- `200`: OK

---

### DELETE /api/admin/etl/task

**Request Body**:

- Content-Type: `application/json` (schema: `CancelRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/etl/trigger

**Request Body**:

- Content-Type: `application/json` (schema: `EtlTriggerRequest`)

**Responses**:

- `200`: OK

---

## Admin Perf

### GET /api/admin/perf/alerts

**性能告警列表 (按时间倒序, 运维面板用)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `limit` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

## Admin Products

### GET /api/admin/products

**后台产品列表 (支持搜索/筛选/排序/分页, 含 published/discontinued 状态)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `page` | query | integer | ✗ |  |
| `pageSize` | query | integer | ✗ |  |
| `type` | query | string | ✗ |  |
| `keyword` | query | string | ✗ |  |
| `includeDiscontinued` | query | boolean | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/products

**后台创建产品 (含 cross-references + machine-applications 嵌套创建)**

**Request Body**:

- Content-Type: `application/json` (schema: `ProductFormDto`)

**Responses**:

- `200`: OK

---

### POST /api/admin/products/compare

**后台产品对比 (admin 用, 不排除下架, 上限 6 个)**

**Request Body**:

- Content-Type: `application/json` (schema: `CompareRequest`)

**Responses**:

- `200`: OK

---

### GET /api/admin/products/search

**后台产品搜索 (admin 用, 含下架, 8 字段)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `Page` | query | integer | ✗ |  |
| `PageSize` | query | integer | ✗ |  |
| `IncludeDiscontinued` | query | boolean | ✗ |  |
| `IsPublished` | query | boolean | ✗ |  |
| `ProductName1` | query | string | ✗ |  |
| `ProductName2` | query | string | ✗ |  |
| `Type` | query | string | ✗ |  |
| `Mr1` | query | string | ✗ |  |
| `Oem2` | query | string | ✗ |  |
| `OemBrand` | query | string | ✗ |  |
| `MediaName` | query | string | ✗ |  |
| `MediaModel` | query | string | ✗ |  |
| `SealingMaterial` | query | string | ✗ |  |
| `Efficiency1` | query | string | ✗ |  |
| `Oem2Batch` | query | string | ✗ |  |
| `Oem3Batch` | query | string | ✗ |  |
| `D1Min` | query | number | ✗ |  |
| `D1Max` | query | number | ✗ |  |
| `D2Min` | query | number | ✗ |  |
| `D2Max` | query | number | ✗ |  |
| `D3Min` | query | number | ✗ |  |
| `D3Max` | query | number | ✗ |  |
| `D4Min` | query | number | ✗ |  |
| `D4Max` | query | number | ✗ |  |
| `H1Min` | query | number | ✗ |  |
| `H1Max` | query | number | ✗ |  |
| `H2Min` | query | number | ✗ |  |
| `H2Max` | query | number | ✗ |  |
| `H3Min` | query | number | ✗ |  |
| `H3Max` | query | number | ✗ |  |
| `H4Min` | query | number | ✗ |  |
| `H4Max` | query | number | ✗ |  |
| `D7Thread` | query | string | ✗ |  |
| `D8Thread` | query | string | ✗ |  |
| `SizeTolerance` | query | number | ✗ |  |
| `MachineBrand` | query | string | ✗ |  |
| `MachineModel` | query | string | ✗ |  |
| `ModelName` | query | string | ✗ |  |
| `EngineBrand` | query | string | ✗ |  |
| `EngineType` | query | string | ✗ |  |
| `SortBy` | query | string | ✗ |  |
| `SortDesc` | query | boolean | ✗ |  |
| `CountMode` | query | string | ✗ |  |
| `PagingMode` | query | string | ✗ |  |
| `Cursor` | query | string | ✗ |  |
| `CountTimeoutMs` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/products/{id}

**后台软删除产品 (is_discontinued=true, 保留历史)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/products/{id}

**后台产品详情 (含全部字段, 不排除下架)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PUT /api/admin/products/{id}

**后台更新产品 (xmin 乐观锁, 409 冲突)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `ProductFormDto`)

**Responses**:

- `200`: OK

---

### GET /api/admin/products/{id}/history

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |
| `changeType` | query | string | ✗ |  |
| `since` | query | string | ✗ |  |
| `until` | query | string | ✗ |  |
| `limit` | query | integer | ✗ |  |
| `cursor` | query | string | ✗ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/products/{id}/images

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### DELETE /api/admin/products/{id}/images/{slot}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |
| `slot` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/products/{id}/images/{slot}

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |
| `slot` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/products/{id}/restore

**后台恢复已下架产品 (is_discontinued=false)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

## Auth

### POST /api/auth/change-password

**修改密码
需登录: 从 JWT claims 取 userId
入参: { oldPassword, newPassword }
失败返回 400 (旧密码错误)**

**Request Body**:

- Content-Type: `application/json` (schema: `ChangePasswordRequest`)
- Content-Type: `text/json` (schema: `ChangePasswordRequest`)
- Content-Type: `application/*+json` (schema: `ChangePasswordRequest`)

**Responses**:

- `200`: OK

---

### POST /api/auth/login

**Request Body**:

- Content-Type: `application/json` (schema: `LoginRequest`)
- Content-Type: `text/json` (schema: `LoginRequest`)
- Content-Type: `application/*+json` (schema: `LoginRequest`)

**Responses**:

- `200`: OK
- `401`: Unauthorized
- `423`: Locked
- `429`: Too Many Requests

---

### POST /api/auth/logout

**登出 (撤销当前 refresh token)
需登录: 从 JWT claims 取 userId
入参: { refreshToken } (客户端传当前持有的 refresh token)**

**Request Body**:

- Content-Type: `application/json` (schema: `RefreshRequest`)
- Content-Type: `text/json` (schema: `RefreshRequest`)
- Content-Type: `application/*+json` (schema: `RefreshRequest`)

**Responses**:

- `200`: OK

---

### GET /api/auth/me

**获取当前登录用户信息
需登录: 从 JWT claims 取 userId**

**Responses**:

- `200`: OK
- `401`: Unauthorized

---

### POST /api/auth/refresh

**刷新 token
入参: { refreshToken }
出参: 新 { accessToken, refreshToken, expiresIn }
流程: 验证旧 token → 撤销旧 token + 签发新 token (一次性)
失败返回 401 (refresh token 无效/过期/已撤销)**

示例请求:
POST /api/auth/refresh
{ "refreshToken": "3F0fPsvbU4c05XvxVBU1..." }
成功响应 (200): 新 token 三件套 (同 login)
失败响应:
- 401: refresh token 无效/过期/已撤销

**Request Body**:

- Content-Type: `application/json` (schema: `RefreshRequest`)
- Content-Type: `text/json` (schema: `RefreshRequest`)
- Content-Type: `application/*+json` (schema: `RefreshRequest`)

**Responses**:

- `200`: OK
- `400`: Bad Request
- `401`: Unauthorized

---

## Default

### GET /

**Responses**:

- `200`: OK

---

## Etl

### POST /api/etl/import

**ETL 导入触发 (products/xrefs/apps, 统一入口, 路径白名单校验)**

**Request Body**:

- Content-Type: `application/json` (schema: `ImportRequest`)

**Responses**:

- `200`: OK

---

### POST /api/etl/import-apps

**ETL 导入 apps (兼容旧入口, 新调用走 /api/etl/import + entityType)**

**Request Body**:

- Content-Type: `application/json` (schema: `ImportRequest`)

**Responses**:

- `200`: OK

---

### POST /api/etl/import-xrefs

**ETL 导入 xrefs (兼容旧入口, 新调用走 /api/etl/import + entityType)**

**Request Body**:

- Content-Type: `application/json` (schema: `ImportRequest`)

**Responses**:

- `200`: OK

---

### GET /api/etl/status

**ETL 导入进度查询 (实时 JSON, 含 current/total/elapsed/eta)**

**Responses**:

- `200`: OK

---

## Health

### GET /health/live

**Liveness 探活 (进程是否存活, K8s/Docker 用)**

**Responses**:

- `200`: OK

---

### GET /health/ready

**Readiness 探活 (PG/Meili/BackgroundService 整体健康度)**

**Responses**:

- `200`: OK

---

## Perf

### GET /api/perf

**性能埋点快照 (P50/P95/P99, 最近 1000 条样本)**

**Responses**:

- `200`: OK

---

### POST /api/perf/ingest

**接收前端性能埋点批量上报 (上限 100 条/批)**

**Request Body**:

- Content-Type: `application/json` (schema: `FrontendPerfBatch`)

**Responses**:

- `200`: OK

---

## Products

### GET /api/products/{oem}

**产品详情 (按 OEM 精确/规范化查询, 含 cross-references + machine-applications)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `oem` | path | string | ✓ |  |

**Responses**:

- `200`: OK

---

## PublicCompare

### GET /api/public/compare

**批量对比 (公开) - 1-6 个产品
URL: GET /api/public/compare?ids=1,2,3**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `ids` | query | string | ✗ |  |

**Responses**:

- `200`: OK

---

## PublicMachineBrands

### GET /api/public/machine-brands/aggregated

**按 4 大类聚合 brand 列表
返: MachineBrandsAggregatedDto:
  { byCategory: { "Agriculture": [...], "Commercial": [...], ... }, totalCount: N }
4 大类 key 一定存在, 即使空列表也返 []**

**Responses**:

- `200`: OK

---

## PublicProduct

### GET /api/public/by-type

**按 type 分组聚合, 顺序按 dict_type.sort_order 升序
返: List<TypeGroupDto>:
  { type, sortOrder, productCount, products: [ProductSummaryDto...] }
限制:
  - 每个 type 至多 50 个 product (避免单 type 撑爆响应)
  - 仅返回 dict_type 已定义的 5 类 (oil/fuel/air/cabin/others), 未定义 type 的产品不展示**

**Responses**:

- `200`: OK

---

### GET /api/public/product/{slug}

**P3.3 (Task 11): 单产品详情 (公开)
URL 格式: /api/public/product/{slug}
slug 格式 (按 R1 规格): {name1}-{name2}-{oemBrand}-{oemNo}
解析策略: 取 slug 最后一段作为 oem (支持 OEM 自身含 - 的场景,如 "AB-123-X")
  - 1) OemNoDisplay 精确匹配
  - 2) Oem2 匹配 (alt OEM)
  - 3) Mr1 匹配
排除 is_discontinued=true (前台不展示下架产品)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `slug` | path | string | ✓ |  |

**Responses**:

- `200`: OK

---

## PublicSearch

### GET /api/public/search

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `oemBrand` | query | string | ✗ |  |
| `oemNo2` | query | string | ✗ |  |
| `oemNo3` | query | string | ✗ |  |
| `machineBrand` | query | string | ✗ |  |
| `machineModel` | query | string | ✗ |  |
| `modelName` | query | string | ✗ |  |
| `engineBrand` | query | string | ✗ |  |
| `engineType` | query | string | ✗ |  |
| `page` | query | integer | ✗ |  |
| `pageSize` | query | integer | ✗ |  |

**Responses**:

- `200`: OK
- `400`: Bad Request

---

### POST /api/public/search/batch-oem

**批量 OEM 查询 (Excel 多行粘贴)
入参: oems (1-500 个字符串, 自动 trim + 去重)
返: 每个 OEM 一条结果
  命中: {oem, hit=true, productId, oemBrand, productName1, oem2}
  未命中: {oem, hit=false}
匹配字段: oem_2 (与前台搜索一致)**

示例请求:
POST /api/public/search/batch-oem
{ "oems": ["P00050000", "11427622448", "C-204"] }
成功响应 (200):
{
"total": 3,
"hits": 2,
"miss": 1,
"results": [
{ "oem": "P00050000", "hit": true, "productId": 12345, "oemBrand": "Mann", "productName1": "OIL FILTER", "oem2": "P00050000" },
{ "oem": "11427622448", "hit": true, "productId": 67890, "oemBrand": "Bosch", "productName1": "AIR FILTER", "oem2": "11427622448" },
{ "oem": "C-204", "hit": false }
]
}

**Request Body**:

- Content-Type: `application/json` (schema: `BatchOemRequest`)
- Content-Type: `text/json` (schema: `BatchOemRequest`)
- Content-Type: `application/*+json` (schema: `BatchOemRequest`)

**Responses**:

- `200`: OK
- `400`: Bad Request

---

## Search

### POST /api/search

**产品搜索 (走 ISearchProvider 抽象, Resilient 主备自动切换)**

**Request Body**:

- Content-Type: `application/json` (schema: `SearchRequest`)

**Responses**:

- `200`: OK

---

### GET /api/search/health

**搜索健康检查 (主备状态)**

**Responses**:

- `200`: OK

---

## Users

### GET /api/admin/audit/login

**登录审计日志 (分页, 按时间倒序)
出参: { items: [...], total, page, pageSize }**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `page` | query | integer | ✗ |  |
| `pageSize` | query | integer | ✗ |  |
| `userId` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/users

**用户列表 (分页)
出参: { items: [...], total, page, pageSize }**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `page` | query | integer | ✗ |  |
| `pageSize` | query | integer | ✗ |  |

**Responses**:

- `200`: OK

---

### POST /api/admin/users

**创建用户
入参: { username, password, role, email?, fullName? }**

**Request Body**:

- Content-Type: `application/json` (schema: `CreateUserRequest`)
- Content-Type: `text/json` (schema: `CreateUserRequest`)
- Content-Type: `application/*+json` (schema: `CreateUserRequest`)

**Responses**:

- `200`: OK

---

### DELETE /api/admin/users/{id}

**软删除用户**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### GET /api/admin/users/{id}

**用户详情**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Responses**:

- `200`: OK

---

### PATCH /api/admin/users/{id}

**修改用户 (role/email/fullName/isActive)
入参: { role?, email?, fullName?, isActive? } (仅传需改字段)**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `UpdateUserRequest`)
- Content-Type: `text/json` (schema: `UpdateUserRequest`)
- Content-Type: `application/*+json` (schema: `UpdateUserRequest`)

**Responses**:

- `200`: OK

---

### POST /api/admin/users/{id}/reset-password

**管理员重置密码
入参: { newPassword }**

**Parameters**:

| Name | In | Type | Required | Description |
|------|----|------|----------|-------------|
| `id` | path | integer | ✓ |  |

**Request Body**:

- Content-Type: `application/json` (schema: `ResetPasswordRequest`)
- Content-Type: `text/json` (schema: `ResetPasswordRequest`)
- Content-Type: `application/*+json` (schema: `ResetPasswordRequest`)

**Responses**:

- `200`: OK

---

## 数据模型 (Schemas)

### BatchOemRequest

批量查询入参

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `oems` | array | ✗ |  |

### BatchOemResponse

批量查询响应

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `total` | integer (int32) | ✗ |  |
| `hits` | integer (int32) | ✗ |  |
| `miss` | integer (int32) | ✗ |  |
| `results` | array | ✗ |  |

### BatchOemResult

单条 OEM 结果

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `oem` | string | ✗ |  |
| `hit` | boolean | ✗ |  |
| `productId` | integer (int64) | ✗ |  |
| `oemBrand` | string | ✗ |  |
| `productName1` | string | ✗ |  |
| `oem2` | string | ✗ |  |

### CancelRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `reason` | string | ✗ |  |
| `reasonCode` | string | ✗ |  |

### ChangePasswordRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `oldPassword` | string | ✗ |  |
| `newPassword` | string | ✗ |  |

### CompareRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `ids` | array | ✗ |  |

### CreateUserRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `username` | string | ✗ |  |
| `password` | string | ✗ |  |
| `role` | string | ✗ |  |
| `email` | string | ✗ |  |
| `fullName` | string | ✗ |  |

### EngineCreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `engineBrand` | string | ✗ |  |
| `engineType` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### EngineReorderItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### EngineReorderRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### EngineUpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `engineBrand` | string | ✗ |  |
| `engineType` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### EtlTriggerRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `jsonlPath` | string | ✗ |  |
| `mode` | string | ✗ |  |
| `dryRun` | boolean | ✗ |  |
| `entityType` | string | ✗ |  |
| `cascade` | boolean | ✗ |  |

### FrontendPerfBatch

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `samples` | array | ✗ |  |

### FrontendPerfSample

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `path` | string | ✗ |  |
| `method` | string | ✗ |  |
| `statusCode` | integer (int32) | ✗ |  |
| `durationMs` | number (double) | ✗ |  |
| `ts` | string | ✗ |  |

### ImportRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `jsonlPath` | string | ✗ |  |
| `mode` | string | ✗ |  |
| `entityType` | string | ✗ |  |
| `cascade` | boolean | ✗ |  |

### LoginRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `username` | string | ✗ |  |
| `password` | string | ✗ |  |

### MachineAppInput

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `machineBrand` | string | ✗ |  |
| `machineModel` | string | ✗ |  |
| `modelName` | string | ✗ |  |
| `engineBrand` | string | ✗ |  |
| `engineType` | string | ✗ |  |
| `engineEnergy` | string | ✗ |  |
| `productionDateStart` | string (date-time) | ✗ |  |
| `productionDateEnd` | string (date-time) | ✗ |  |
| `power` | string | ✗ |  |
| `serialNumberFrom` | string | ✗ |  |
| `serialNumberTo` | string | ✗ |  |
| `carBodyType` | string | ✗ |  |
| `series` | string | ✗ |  |
| `co2EmissionStandard` | string | ✗ |  |
| `transmissionType` | string | ✗ |  |
| `engineDisplacement` | string | ✗ |  |
| `numberOfCylinders` | integer (int32) | ✗ |  |
| `gvwr` | string | ✗ |  |
| `tonnage` | string | ✗ |  |
| `geographicArea` | string | ✗ |  |
| `chassisType` | string | ✗ |  |
| `engineModel` | string | ✗ |  |
| `cabinType` | string | ✗ |  |
| `capacity` | string | ✗ |  |
| `engineSerialNumber` | string | ✗ |  |

### MachineCreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `machineBrand` | string | ✗ |  |
| `machineModel` | string | ✗ |  |
| `machineName` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |
| `machineCategory` | string | ✗ |  |

### MachineReorderItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### MachineReorderRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### MachineUpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `machineBrand` | string | ✗ |  |
| `machineModel` | string | ✗ |  |
| `machineName` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |
| `machineCategory` | string | ✗ |  |

### MediaCreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mediaName` | string | ✗ |  |
| `mediaModel` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### MediaReorderItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### MediaReorderRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### MediaUpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mediaName` | string | ✗ |  |
| `mediaModel` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### OemBrandCreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `brand` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### OemBrandReorderItem

OEM 品牌重排序输入项

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### OemBrandReorderRequest

OEM 品牌重排序请求体

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### OemBrandUpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `brand` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### OemNo3CreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `oemNo3` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### OemNo3ReorderItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### OemNo3ReorderRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### OemNo3UpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `oemNo3` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### ProblemDetails

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | ✗ |  |
| `title` | string | ✗ |  |
| `status` | integer (int32) | ✗ |  |
| `detail` | string | ✗ |  |
| `instance` | string | ✗ |  |

### ProductFormDto

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `oem2` | string | ✗ |  |
| `productName1` | string | ✗ |  |
| `productName2` | string | ✗ |  |
| `type` | string | ✗ |  |
| `mr1` | string | ✗ |  |
| `isPublished` | boolean | ✗ |  |
| `remark` | string | ✗ |  |
| `rowVersion` | integer (int32) | ✗ |  |
| `d1Mm` | number (double) | ✗ |  |
| `d2Mm` | number (double) | ✗ |  |
| `d3Mm` | number (double) | ✗ |  |
| `d4Mm` | number (double) | ✗ |  |
| `h1Mm` | number (double) | ✗ |  |
| `h2Mm` | number (double) | ✗ |  |
| `h3Mm` | number (double) | ✗ |  |
| `h4Mm` | number (double) | ✗ |  |
| `d7Thread` | string | ✗ |  |
| `d8Thread` | string | ✗ |  |
| `noCheckValves` | integer (int32) | ✗ |  |
| `noBypassValves` | integer (int32) | ✗ |  |
| `media` | string | ✗ |  |
| `mediaModel` | string | ✗ |  |
| `bypassValveLr` | number (double) | ✗ |  |
| `bypassValveHr` | number (double) | ✗ |  |
| `efficiency1` | string | ✗ |  |
| `efficiency2` | string | ✗ |  |
| `bypassPressure` | number (double) | ✗ |  |
| `collapsePressureBar` | number (double) | ✗ |  |
| `sealingMaterial` | string | ✗ |  |
| `tempRange` | string | ✗ |  |
| `qtyPerCarton` | integer (int32) | ✗ |  |
| `weightKgs` | number (double) | ✗ |  |
| `cartonLengthMm` | number (double) | ✗ |  |
| `cartonWidthMm` | number (double) | ✗ |  |
| `cartonHeightMm` | number (double) | ✗ |  |
| `masterBoxQty` | integer (int32) | ✗ |  |
| `masterBoxWeightKgs` | number (double) | ✗ |  |
| `masterBoxLengthMm` | number (double) | ✗ |  |
| `masterBoxWidthMm` | number (double) | ✗ |  |
| `masterBoxHeightMm` | number (double) | ✗ |  |
| `crossReferences` | array | ✗ |  |
| `machineApplications` | array | ✗ |  |

### ProductName1CreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `productName1` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### ProductName1ReorderItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### ProductName1ReorderRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### ProductName1UpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `productName1` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### ProductName2CreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `productName2` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### ProductName2ReorderItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### ProductName2ReorderRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### ProductName2UpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `productName2` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### PublicEightResponse

P3.4 (Task 11.5): 8 字段响应

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `total` | integer (int64) | ✗ |  |
| `page` | integer (int32) | ✗ |  |
| `pageSize` | integer (int32) | ✗ |  |
| `totalPages` | integer (int32) | ✗ |  |
| `elapsedMs` | integer (int32) | ✗ |  |
| `countMode` | string | ✗ |  |
| `items` | array | ✗ |  |

### PublicSearchHit

P3.4 (Task 11.5): 公开搜索单条结果

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `oemNoDisplay` | string | ✗ |  |
| `oem2` | string | ✗ |  |
| `productName1` | string | ✗ |  |
| `type` | string | ✗ |  |
| `d1Mm` | string | ✗ |  |
| `h1Mm` | string | ✗ |  |

### RefreshRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `refreshToken` | string | ✗ |  |

### ResetPasswordRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `newPassword` | string | ✗ |  |

### SearchRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `q` | string | ✗ |  |
| `type` | string | ✗ |  |
| `d1` | number (double) | ✗ |  |
| `d2` | number (double) | ✗ |  |
| `d3` | number (double) | ✗ |  |
| `h1` | number (double) | ✗ |  |
| `h2` | number (double) | ✗ |  |
| `h3` | number (double) | ✗ |  |
| `d7` | number (double) | ✗ |  |
| `d8` | number (double) | ✗ |  |
| `tolerance` | number (double) | ✗ |  |
| `includeDiscontinued` | boolean | ✗ |  |
| `page` | integer (int32) | ✗ |  |
| `pageSize` | integer (int32) | ✗ |  |

### TypeCreateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### TypeReorderItem

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | integer (int64) | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### TypeReorderRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `items` | array | ✗ |  |

### TypeUpdateRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | ✗ |  |
| `sortOrder` | integer (int32) | ✗ |  |

### UpdateUserRequest

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `role` | string | ✗ |  |
| `email` | string | ✗ |  |
| `fullName` | string | ✗ |  |
| `isActive` | boolean | ✗ |  |

### XrefInput

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `productName1` | string | ✗ |  |
| `oemBrand` | string | ✗ |  |
| `oemNo3` | string | ✗ |  |

---

> 文档由 `_export_openapi.py` 自动生成于 OpenAPI 3.0 schema。
> Swagger UI (开发环境): http://localhost:5148/swagger