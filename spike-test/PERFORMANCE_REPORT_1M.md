# SakuraFilter 百万级性能测试报告

> 测试环境：隔离式 perf 栈（F:\sakurafilter-perf，独立 compose 项目 + 独立 `.env.perf`）
> 数据规模：products=1,000,000 / xrefs(cross_references)=12,497,804 / apps(machine_applications)=15,490,185
> 测试时间：2026-08-15
> 评判红线：搜索 **P95 < 200ms** / typeahead **P95 < 100ms**

> 📌 **修订说明（2026-08-15 补充验证）**：本报告基线数据（§1–§4、§7）经复核总体可信，前两轮结果作废的结论维持。
> 本轮在基线之上补充了三项验证，并据此修订了部分判断：
> 1. **资源证据（§5.1 机制修订）**：Meili 27.3GB 索引 > Docker(WSL2) VM 25.1GB 内存 → 索引无法常驻内存，瓶颈是**磁盘 I/O 吞吐/并发上限**，而非报告初稿所述的"内存/磁盘颠簸"（实测 major page fault 仅 +4、Meili RSS 稳定在 ~500MB，无颠簸）。
> 2. **Admin 检索根因纠正（§5.2 重写）**：原判断"machine_brand 慢 = 缺 trigram 索引"**不正确**。代码实际用 `=` 等值；慢的根因是 `products` 缺 `(updated_at DESC, id DESC)` 索引，导致每次 admin 检索先对 100 万 products 做 1M 行落盘排序、再嵌套循环探测。trigram 索引（迁移 017）对此路径**无效**；补 `(updated_at DESC, id DESC)` 索引（迁移 022）后，CUMMINS 查询 SQL 1481ms→0.27ms、API ~1.7s→5–25ms。
> 3. **限流开启测试（§九 新增）**：限流 ON 时 300/min/IP 严格生效，单 IP 400 req/min 实测 25% 429；并发现分区键用 `RemoteIpAddress`（非 `X-Forwarded-For`）的生产风险。

---

## 一、结论速览

| 路径 | c1 | c10 | c50 | c100 | 达标情况 |
|---|---|---|---|---|---|
| **用户前台搜索** `/api/search`（Meili） | P95 **53ms** ✅ | P95 **117ms** ✅ | P95 **482ms** ❌ | P95 **855ms** ❌ | 中低并发达标，高并发不达标 |
| **Typeahead** `/api/admin/dict/oem-brands/typeahead` | — | P95 **44ms** ✅ | — | — | **全部达标** |
| **Admin 搜索** `/api/admin/products/search`（PG） | P95 **~1.1s** ❌（基线）| — | — | — | 不达标（machine_brand 慢）→ **经迁移 022 已修复至 5–25ms** |

**总体判定**：
- ✅ **Typeahead 全量达标**（P95 44ms < 100ms）。
- ✅ **用户前台搜索在典型并发（1–10）下达标**（P95 53–117ms < 200ms）。
- ❌ **前台搜索在 50–100 并发下不达标**（P95 482–855ms）—— 根因是 **Meili 索引 25.4GB 远超可用内存**，高并发时内存/磁盘颠簸。
- ⚠️ **Admin PG 搜索（machine_brand/model）曾极慢（~1.1s）** —— 初判"缺 trigram 索引"不准确；真实根因是 `products` 缺 `(updated_at DESC, id DESC)` 索引（详见 §5.2）。**已于 2026-08-15 经迁移 022 修复，降至 5–25ms**，属内部/admin 路径，不影响用户前台搜索。

---

## 二、测试环境与数据一致性

| 组件 | 配置 | 状态 |
|---|---|---|
| PostgreSQL 16 | `/dev/shm` 4GB（修复后） | products=1,000,000 ✅ |
| Meilisearch v1.12 | products 索引 1,000,000 文档 | isIndexing=false ✅ |
| 一致性校验 | `verify_consistency.py` | PG(1,000,000) == Meili(1,000,000) ✅ 退出码 0 |

> 注：测试中途发生**电脑重启 + Docker 守护进程未自动拉起**，业务数据因 Docker 卷持久化**零丢失**；重启后触发 `reindex-all` 重建 Meili 索引至 100 万，并重建 8 张字典表。

---

## 三、压测方法学与踩坑（关键，影响结果可信度）

本次压测前发现并修复了 3 个会导致结果失真的环境/方法问题：

1. **PG 容器 `/dev/shm` 仅 64MB（Docker 默认）** → 百万级并发查询时 PG 并行排序/哈希共享内存段撑爆，报 `53100: No space left on device`，被后端转成 409 错误、P95 飙升。
   - 修复：`docker-compose.perf.yml` 的 `postgres` 加 `shm_size: "4gb"`，重建容器后解决。
2. **生产限流器干扰压测**：search 端点挂 `RateLimit["search"]`（FixedWindow 300/min）。高并发突发必返回 429，使高并发档几乎全是错误数据、毫无意义。
   - 修复：在 `docker-compose.yml` 的 backend environment 加 `RateLimit__Enabled=${RATELIMIT_ENABLED:-true}` 开关，压测时传 `RATELIMIT_ENABLED=false` 关闭（默认 true，生产安全）。
3. **压测查询集混入了 admin 内部接口**：原 48 条查询集中 `machine_brand`/`machine_model` 两类走 **admin PG 接口**（`/api/admin/products/search`），而非用户前台 Meili 接口。这两类查询本身 ~1s，把聚合 P95 从 ~50ms 拉到 ~1s，造成"搜索很慢"的**假象**。
   - 修复：按查询类型分拆探针（见 `probe_per_type.py`）定位后，生成 **仅 public 路径**的 `bench_queries_public.json` 重测，得到干净数据。

> 因此，前两次压测（`results_1m.json` / `results_1m_v2.json`）均**作废**，不用于评判。本报告基于干净的 `results_1m_public.json`。

---

## 四、详细结果

### 4.1 用户前台搜索 `/api/search`（Meili，限流已关，纯净数据）

| 并发档 | P50 | P95 | P99 | 样本 | 错误 | 达标(<200ms) |
|---|---|---|---|---|---|---|
| c1 | 34.9ms | 53.4ms | 62.9ms | 360 | 0 | ✅ |
| c10 | 74.0ms | 117.2ms | 132.9ms | 360 | 0 | ✅ |
| c50 | 312.2ms | 482.2ms | 550.0ms | 360 | 0 | ❌ |
| c100 | 611.3ms | 854.6ms | 954.9ms | 360 | 0 | ❌ |

### 4.2 Typeahead（并发 10，70 样本）

P50=12.9ms / **P95=43.9ms** / P99=45.3ms / errors=0 → ✅ 达标（<100ms）

### 4.3 按查询类型分拆（定位慢查询来源，`probe_per_type.py`，c1 顺序）

| 查询类型 | 实际路径 | P50 | P95 | 结论 |
|---|---|---|---|---|
| fulltext / oem_exact / oem_fuzzy / type_filter / size_h1_5mm / size_h1_10mm（6 类） | public `/api/search`（Meili） | ~46ms | ~50ms | 极快 |
| machine_model | admin `/api/admin/products/search`（PG） | 138.6ms | 141.3ms | 偏慢 |
| **machine_brand** | admin `/api/admin/products/search`（PG） | **1051.9ms** | **1114.0ms** | 极慢 |

### 4.4 直连 Meili 并发探针（绕过 backend，`probe_meili_direct.py`）

| 并发 | P50 | P95 |
|---|---|---|
| c1 | 50.5ms | 81.6ms |
| c10 | 38.0ms | 55.2ms |
| c50 | 170.9ms | 264.2ms |
| c100 | 310.4ms | 361.8ms |

→ 证明高并发退化是 **Meili 引擎自身容量限制**（直连同样退化）；backend 在其上叠加约 2x 网络+ASP.NET 开销。

---

## 五、根因分析

### 5.1 前台搜索高并发退化（c50/c100 超标）
- **主导根因：Meili `products` 索引体积 25.4–27.3GB**。文档内嵌了 `oem_list` / `machine_list` 大数组（平均每个 product 含 ~12 个 OEM 交叉引用 + ~15 条机型应用），导致索引极度膨胀。
- **瓶颈机制（已用运行时证据修订）**：测试栈跑在 Docker Desktop 的 WSL2 虚拟机内，VM 总内存仅 **25.1GB**（即容器可用 RAM 上限，≠ 宿主 47.9GB 物理内存）。27.3GB 的 Meili 索引**物理上无法常驻内存**，高并发搜索时 Meili 必须频繁从磁盘读取索引分片，P95 随并发攀升。
  - **关键证据（monitor_v4.log，c50 压测全程采样）**：`/proc/vmstat` 的 `pgmajfault`（major page fault，颠簸判据）全程仅 **+4**；Meili 容器 `MemUsage` 稳定 **~500MB（占 VM 2%~3%）**；宿主无 swap 写入。→ **不存在内存/磁盘颠簸（thrashing）**。
  - 因此准确措辞应为：**索引体积超过可用内存 → 索引无法常驻 → 高并发下受磁盘 I/O 吞吐与并发度上限制约**，而非"颠簸"。初稿"内存/磁盘颠簸"的定性偏口语化、机制不准确，结论方向（索引过大是瓶颈）不变。
- 直连 Meili 在 c100 已达 P95=362ms，叠加 backend 开销后到 855ms，印证容量瓶颈在 Meili 侧。

### 5.2 Admin PG 搜索慢（machine_brand ~1.1s → 已修复）
> ⚠️ **原判断已纠正**：初稿认为"慢 = 前置通配符 `ILIKE` 缺 trigram 索引"，**不准确**。

- **实际代码路径**（`AdminProductService.SearchAsync`）：`machine_brand`/`machine_model` 过滤用的是 **`=` 等值**（`m.MachineBrand == mb`），封装在相关子查询 `EXISTS (... m.product_id = p.id AND m.machine_brand = @mb)` 内；最终结果按 **`updated_at DESC, id DESC`** 排序。
- **真实根因**：`products` 表**没有 `updated_at` 索引**。于是查询计划先对 100 万 products 做 `Parallel Seq Scan` + **external-merge Sort（排序集 16MB 落盘）**，得到按 `updated_at DESC` 的顺序后，再嵌套循环探测 `machine_applications`（每个 product 走 `product_id` 索引 + `machine_brand` 过滤）。排序发生在过滤**之前**，所以 `machine_brand` 上的索引（无论 btree 还是 trigram）都用不上。
  - 实测 `machineBrand=CUMMINS` 真实计划：`Execution Time = 1481ms`（其中排序 1M 行落盘占主导）。
- **迁移 017 的 trigram GIN 索引对此路径无效**：① 代码是 `=` 非 `ILIKE`，trgm 即便支持等值也是"索引优先"才受益，而本计划根本不先按 brand 过滤；② `machine_applications` 上本就有 btree `(machine_brand, machine_model)`，但同样因"排序先于过滤"未被采用。
- **修复（迁移 022）**：补 `CREATE INDEX ix_products_updated_at_id ON products (updated_at DESC, id DESC)`。外层 products 扫描改为**索引扫描**，省去全表排序 + 落盘。
  - 实测 `machineBrand=CUMMINS`：`Execution Time 1481ms → 0.27ms`；API `GET /api/admin/products/search?machineBrand=CUMMINS` **~1.7s → 5–25ms**（CUMMINS/DEUTZ/JCB/CASE 四品牌全部 5–25ms）。
  - 受益面：所有按 `updated_at` 默认排序的 admin 列表/检索页均提速，不止机型过滤。
- 注：`machine_model=PC200` 此前已快（~25ms），因其选择性高（~129 行），计划本就走 `ix_apps_machine_model_trgm` Bitmap Index Scan 先过滤；属特例，不代表高命中量品牌已快。

---

## 六、优化建议（按收益排序）

| 优先级 | 建议 | 预期效果 |
|---|---|---|
| P0 | **缩减 Meili 文档体积**：不在 Meili 内嵌完整 `oem_list`/`machine_list`，仅存可搜索字段 + product id，详情按需从 PG 取（分页）。目标索引降到数 GB。 | 索引可常驻内存，高并发 P95 回落至 <200ms |
| P0 | **给 Meili 专属内存预留**（compose `mem_limit` + 充足 RAM），或扩容主机内存至 64GB+ 以容纳 25GB 索引 | 使 27.3GB 索引可常驻内存，消除"索引无法常驻→磁盘 I/O 约束"瓶颈 |
| P1 | **Meili 只读副本 / 横向扩展**（v1.12 支持多实例），将搜索吞吐分摊 | 提升高并发承载 |
| P1 | **Admin 检索补 `(updated_at DESC, id DESC)` 联合索引**（迁移 022，已验证） | 高命中量品牌（CUMMINS 等）从 ~1.5–1.7s 降至 5–25ms；所有 admin 默认排序页受益。**注意：trgm 索引（迁移 017）对此路径无效，原"加 trigram"建议作废** |
| P2 | **搜索结果 Redis 缓存**（按 query 签名缓存热点结果） | 进一步压低 P95、抗突发 |
| P2 | **backend Meili HttpClient 连接池调优**（复用连接、提高 MaxConnectionsPerServer） | 削减 backend 叠加的 ~2x 网络开销 |

---

## 七、产出文件清单（F:\sakurafilter-perf\spike-test\）

| 文件 | 说明 |
|---|---|
| `PERFORMANCE_REPORT_1M.md` | 本性能报告 |
| `results_1m_public.json` / `.log` | **干净有效**的 public 路径压测结果（限流关、剔除 admin 查询） |
| `results_1m.json` / `results_1m_v2.json` | 作废（环境干扰，仅供参考） |
| `bench_queries.json` | 完整 48 条压测查询集（含 admin 类，已证明会污染结果） |
| `bench_queries_public.json` | 仅 public 路径 36 条（用于干净压测） |
| `probe_per_type.py` | 按查询类型分拆延迟探针（定位慢查询来源） |
| `probe_meili_direct.py` | 直连 Meili 并发探针（隔离 backend 验证 Meili 容量） |
| `build_bench_queries.py` | 基于真实数据采样生成查询集 |
| `seed_dicts_perf.sql` | 字典表反向填充（已加固为幂等 DO 块） |
| `verify_consistency.py` | PG==Meili 文档数一致性校验 |

---

## 八、最终判定（对照用户红线）

- 🟢 **Typeahead P95 < 100ms**：**通过**（实测 44ms）。
- 🟡 **搜索 P95 < 200ms**：**中低并发（≤10）通过，高并发（50–100）不通过**。
  - 不通过的根因是 **Meili 25.4GB 超大索引无法常驻内存**，属**容量/架构问题**而非搜索引擎本身慢（纯查询延迟仅 ~50ms）。
  - 修复 P0 建议（缩减索引体积 + 内存预留）后，预期可在全并发档达标。
- 🟢 **Admin PG 搜索**：**已于 2026-08-15 经迁移 022 修复**（补 `products(updated_at DESC, id DESC)` 索引）。高命中量品牌（CUMMINS 等）从 ~1.5–1.7s 降至 5–25ms，所有按 `updated_at` 默认排序的 admin 页均受益。**不计入用户前台搜索红线**（属内部路径）。

---

## 九、补充验证与最终签字结论（2026-08-15 修订轮）

### 9.1 限流开启测试（T24）
- 方法：backend 以 `RATELIMIT_ENABLED=true` 重建（默认即开），用 `probe_ratelimit.py` 从单 IP 发 **400 req/min**，命中 `search` 策略（FixedWindow **300/min/IP**）。c50、c100 分置两个独立 1 分钟窗口，避免互相挤占预算。
- 结果：

| 并发档 | 成功(200) | 限流(429) | 429 占比 | 成功 P50 | 成功 P95 |
|---|---|---|---|---|---|
| c50 | 300 | 100 | **25.0%** | 440ms | 678ms |
| c100 | 300 | 100 | **25.0%** | 679ms | 878ms |

- 结论：限流器**严格按 300/min/IP 预算生效**，429 占比与并发度无关（固定 25%），机制正确。c100 下成功请求尾延迟仍达 12.2s，与 §4.1 容量结论一致（搜索引擎自身高并发退化，非限流引入）。

### 9.2 生产风险与待办（限流分区键）
- ⚠️ 限流器分区键用的是 `RemoteIpAddress`（容器内看到的客户端 IP），**不是** `X-Forwarded-For` / `X-Real-IP`。若部署在反向代理 / LB 之后，所有请求会被识别为同一 IP（proxy 的 IP），导致**全站共享一个 300/min 预算**——单客户端即可拖垮全局，或正常多用户被误限。生产前置 LB / 网关时**必须改为读 `X-Forwarded-For`**。
- 文档注释（`RateLimitOptions.cs`）称"global"限流，但代码按 per-IP 分区，存在**文档与实现不一致**，需同步修正。

### 9.3 最终签字结论（对照用户红线）
| 验收项 | 结论 |
|---|---|
| 百万级数据导入 | ✅ products=1,000,000 / xrefs=12,497,804 / apps=15,490,185，PG==Meili 一致 |
| 中低并发前台搜索（≤10） | ✅ P95 53–117ms < 200ms |
| Typeahead | ✅ P95 44ms < 100ms |
| 高并发前台搜索（50–100） | ❌ P95 482–855ms，根因 Meili 27.3GB 索引 > 25.1GB VM 内存，受磁盘 I/O 约束 |
| Admin PG 搜索 | ⚠️→✅ 经迁移 022 已修复（1.7s→5–25ms） |
| 限流机制 | ✅ 300/min/IP 严格生效；但分区键需改 XFF 方可上生产 |
| 基线数据可信度 | 前两轮作废；`results_1m_public.json` 为有效基线 |

**最终判定**：本测试**不足以**作为"百万级正式上线容量已确认"的签字依据。可确认：导入、中低并发搜索、typeahead、admin 检索均达标/已修复；**高并发前台搜索因 Meili 索引过大仍不达标**，需实施 P0（缩减索引体积 / 内存预留）后方可全并发达标。限流分区键需在部署前置代理前修正（`RemoteIpAddress`→`X-Forwarded-For`）。
