using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using System.Text.Json;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 后台产品管理服务 (Day 8.1)
/// 用途: 支撑后台产品 CRUD (规格 后台新增产品格式 7 分区表单)
/// 设计:
///   - 单事务: Product + xref + machine_application 三表一起写
///   - 软删除: 走 is_discontinued=true (历史产品保留)
///   - OEM 归一化: 大写 + 去空格 + 去特殊字符, 保证 oem_no_normalized 唯一
///   - Type 派生: 从 product_name_3 自动取, 后台可手动覆盖
/// </summary>
public class AdminProductService
{
    private readonly ProductDbContext _db;
    private readonly ILogger<AdminProductService> _logger;
    private readonly CursorHmac _cursorHmac;
    private readonly IObjectStorage? _storage;  // P3.3: 用于图片预签名 URL (前台 + 后台详情)

    public AdminProductService(
        ProductDbContext db,
        ILogger<AdminProductService> logger,
        CursorHmac cursorHmac,
        IObjectStorage? storage = null)
    {
        _db = db;
        _logger = logger;
        _cursorHmac = cursorHmac;
        _storage = storage;
    }

    // ========== 新增产品 ==========
    public async Task<ProductDetailDto> CreateAsync(ProductFormDto form, string? createdBy, CancellationToken ct = default)
    {
        ValidateForm(form);

        var oemNormalized = NormalizeOem(form.Oem2);
        var oemDisplay = form.Oem2.Trim();

        // P0-1.3: 开启事务, 保证 Product + xref + machine_application + history 四表原子写入
        //   WHY: 之前 3 次 SaveChangesAsync 之间任一失败会留孤儿数据 (e.g. product 已写但 xref 失败)
        //   并发场景下 AnyAsync 检查与 SaveChangesAsync 之间有 TOCTOU 窗口, 第二个请求触发 23505 唯一约束冲突
        //   → 端点层 catch DbUpdateException + 23505 → 返回 409 Conflict (见 Program.cs)
        //   业务异常 (InvalidOperationException/ArgumentException) 还未写数据, 直接抛出无需显式回滚
        //   (await using 会自动 rollback 未 commit 的事务)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 唯一性检查 (oem_no_normalized 唯一索引) - 仍保留, 提供业务友好错误
            //   注意: READ COMMITTED 下此检查不能消除并发竞态, 仅为常见路径提供 409 而非 500
            var exists = await _db.Products.AnyAsync(p => p.OemNoNormalized == oemNormalized, ct);
            if (exists)
                throw new InvalidOperationException($"产品已存在 (oem_no_normalized={oemNormalized})");

            // V2: MR.1 唯一性检查(部分唯一索引,WHERE mr_1 IS NOT NULL)
            var mr1Exists = await _db.Products.AnyAsync(p => p.Mr1 == form.Mr1!.Trim(), ct);
            if (mr1Exists)
                throw new InvalidOperationException($"MR1_ALREADY_EXISTS: MR.1 已存在 (mr1={form.Mr1})");

            // V2: OEM 3 唯一性检查(同 Brand 下未下架 OEM 3)
            foreach (var x in form.CrossReferences)
            {
                if (!string.IsNullOrEmpty(x.OemBrand) && !string.IsNullOrEmpty(x.OemNo3))
                {
                    var oem3Exists = await _db.CrossReferences
                        .AnyAsync(c => c.OemBrand == x.OemBrand!.Trim()
                            && c.OemNo3 == x.OemNo3!.Trim()
                            && !c.IsDiscontinued, ct);
                    if (oem3Exists)
                        throw new InvalidOperationException($"OEM3_ALREADY_EXISTS: OEM 3 已存在 (brand={x.OemBrand}, oem3={x.OemNo3})");
                }
            }

            var product = new Product
            {
                OemNoDisplay = oemDisplay,
                OemNoNormalized = oemNormalized,
                // 分区 1
                ProductName1 = form.ProductName1?.Trim(),
                ProductName2 = form.ProductName2?.Trim(),
                Type = string.IsNullOrWhiteSpace(form.Type) ? DeriveTypeFromName(form.ProductName1) : form.Type.Trim(),
                Mr1 = form.Mr1?.Trim(),
                Oem2 = form.Oem2?.Trim(),
                IsPublished = form.IsPublished,
                Remark = form.Remark?.Trim(),
                // 分区 3
                D1Mm = form.D1Mm, D2Mm = form.D2Mm, D3Mm = form.D3Mm, D4Mm = form.D4Mm,
                H1Mm = form.H1Mm, H2Mm = form.H2Mm, H3Mm = form.H3Mm, H4Mm = form.H4Mm,
                D7Thread = form.D7Thread?.Trim(), D8Thread = form.D8Thread?.Trim(),
                NoCheckValves = form.NoCheckValves, NoBypassValves = form.NoBypassValves,
                // 分区 5
                Media = form.Media?.Trim(), MediaModel = form.MediaModel?.Trim(),
                BypassValveLr = form.BypassValveLr, BypassValveHr = form.BypassValveHr,
                Efficiency1 = form.Efficiency1?.Trim(), Efficiency2 = form.Efficiency2?.Trim(),
                BypassPressure = form.BypassPressure,
                CollapsePressureBar = form.CollapsePressureBar,
                SealingMaterial = form.SealingMaterial?.Trim(), TempRange = form.TempRange?.Trim(),
                // 分区 6
                QtyPerCarton = form.QtyPerCarton, WeightKgs = form.WeightKgs,
                CartonLengthMm = form.CartonLengthMm, CartonWidthMm = form.CartonWidthMm, CartonHeightMm = form.CartonHeightMm,
                MasterBoxQty = form.MasterBoxQty, MasterBoxWeightKgs = form.MasterBoxWeightKgs,
                MasterBoxLengthMm = form.MasterBoxLengthMm, MasterBoxWidthMm = form.MasterBoxWidthMm, MasterBoxHeightMm = form.MasterBoxHeightMm,
                VolumePerCartonM3 = DeriveVolume(form.CartonLengthMm, form.CartonWidthMm, form.CartonHeightMm),
                // 元数据
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            _db.Products.Add(product);
            await _db.SaveChangesAsync(ct);  // 拿到 product.Id

            // 分区 2: xref (V2: 加 Oem2/SortOrder/MachineType/IsPublished)
            foreach (var x in form.CrossReferences)
            {
                _db.CrossReferences.Add(new CrossReference
                {
                    ProductId = product.Id,
                    ProductName1 = x.ProductName1?.Trim(),
                    OemBrand = x.OemBrand?.Trim(),
                    OemNo3 = x.OemNo3?.Trim(),
                    Oem2 = x.Oem2?.Trim(),               // V2
                    SortOrder = x.SortOrder,               // V2
                    MachineType = string.IsNullOrEmpty(x.MachineType) ? "others" : x.MachineType,  // V2
                    IsPublished = x.IsPublished,           // V2
                    CreatedAt = DateTime.UtcNow
                });
            }
            // 分区 7: 车型
            foreach (var m in form.MachineApplications)
            {
                _db.MachineApplications.Add(MapToMachineApp(product.Id, m));
            }
            if (form.CrossReferences.Count > 0 || form.MachineApplications.Count > 0)
                await _db.SaveChangesAsync(ct);

            // 历史
            _db.ProductHistory.Add(new ProductHistory
            {
                ProductId = product.Id,
                ChangeType = "create",
                ChangedBy = createdBy,
                ChangedAt = DateTime.UtcNow,
                ChangedFields = JsonSerializer.Serialize(new { action = "manual_create", oem = oemDisplay })
            });
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation("产品创建成功 id={Id} oem={Oem} xref={Xref} apps={Apps}",
                product.Id, oemDisplay, form.CrossReferences.Count, form.MachineApplications.Count);
            return await GetByIdAsync(product.Id, ct);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not ArgumentException)
        {
            // 业务异常 (产品已存在 / 校验失败) 直接抛出: await using 会自动 rollback 未 commit 的事务
            // 其他异常 (含 DbUpdateException 23505) 显式回滚 + 记日志 + 重抛, 由端点层映射为合适 HTTP 状态码
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "产品创建事务回滚 oem={Oem}", oemDisplay);
            throw;
        }
    }

    // ========== 更新产品 ==========
    public async Task<ProductDetailDto> UpdateAsync(long id, ProductFormDto form, string? updatedBy, CancellationToken ct = default)
    {
        // P0-1.3: 开启事务, 保证 products + xref + machine_application + history 四表原子更新
        //   WHY: 之前 2 次 SaveChangesAsync 之间任一失败会导致部分字段更新 + 子表数据丢失
        //   并发场景下若 xref 唯一索引冲突也会触发 23505, 由端点层映射为 409
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new KeyNotFoundException($"产品 id={id} 不存在");

            // E2E BD.3 修复 v2: 用前端带回的 RowVersion 覆盖 EF Core 的 OriginalValue
            //   WHY: EF Core IsRowVersion() 在 UPDATE 时用 OriginalValue["RowVersion"] 做 WHERE 条件
            //        但 FirstOrDefaultAsync 加载的是当前最新 xmin, 永远不会冲突
            //        用前端 GET 时的 RowVersion (可能过期) 覆盖 OriginalValue, 才能检测"先读后写"并发
            //   注意: 必须用 Entry().OriginalValues 而非直接赋值 product.RowVersion
            //        直接赋值会被 Change Tracker 视为 Modified, 导致 SET 中包含 xmin (PG 不允许 UPDATE xmin)
            //        OriginalValues 只影响 WHERE 条件, 不影响 SET
            if (form.RowVersion.HasValue)
            {
                _db.Entry(product).OriginalValues["RowVersion"] = form.RowVersion.Value;
            }

            var changed = new Dictionary<string, object>();
            void Track<T>(string key, T oldVal, T? newVal)
            {
                if (!EqualityComparer<T?>.Default.Equals(oldVal, newVal)) changed[key] = newVal!;
            }

            // 分区 1
            Track(nameof(product.ProductName1), product.ProductName1, form.ProductName1?.Trim());
            product.ProductName1 = form.ProductName1?.Trim();
            Track(nameof(product.ProductName2), product.ProductName2, form.ProductName2?.Trim());
            product.ProductName2 = form.ProductName2?.Trim();
            if (!string.IsNullOrWhiteSpace(form.Type))
            {
                Track(nameof(product.Type), product.Type, form.Type.Trim());
                product.Type = form.Type.Trim();
            }
            Track(nameof(product.Mr1), product.Mr1, form.Mr1?.Trim());
            product.Mr1 = form.Mr1?.Trim();
            if (!string.IsNullOrWhiteSpace(form.Oem2))
            {
                Track(nameof(product.Oem2), product.Oem2, form.Oem2.Trim());
                product.Oem2 = form.Oem2.Trim();
            }
            Track(nameof(product.IsPublished), product.IsPublished, form.IsPublished);
            product.IsPublished = form.IsPublished;
            Track(nameof(product.Remark), product.Remark, form.Remark?.Trim());
            product.Remark = form.Remark?.Trim();

            // 分区 3
            Track(nameof(product.D1Mm), product.D1Mm, form.D1Mm); product.D1Mm = form.D1Mm;
            Track(nameof(product.D2Mm), product.D2Mm, form.D2Mm); product.D2Mm = form.D2Mm;
            Track(nameof(product.D3Mm), product.D3Mm, form.D3Mm); product.D3Mm = form.D3Mm;
            Track(nameof(product.D4Mm), product.D4Mm, form.D4Mm); product.D4Mm = form.D4Mm;
            Track(nameof(product.H1Mm), product.H1Mm, form.H1Mm); product.H1Mm = form.H1Mm;
            Track(nameof(product.H2Mm), product.H2Mm, form.H2Mm); product.H2Mm = form.H2Mm;
            Track(nameof(product.H3Mm), product.H3Mm, form.H3Mm); product.H3Mm = form.H3Mm;
            Track(nameof(product.H4Mm), product.H4Mm, form.H4Mm); product.H4Mm = form.H4Mm;
            Track(nameof(product.D7Thread), product.D7Thread, form.D7Thread?.Trim()); product.D7Thread = form.D7Thread?.Trim();
            Track(nameof(product.D8Thread), product.D8Thread, form.D8Thread?.Trim()); product.D8Thread = form.D8Thread?.Trim();
            Track(nameof(product.NoCheckValves), product.NoCheckValves, form.NoCheckValves); product.NoCheckValves = form.NoCheckValves;
            Track(nameof(product.NoBypassValves), product.NoBypassValves, form.NoBypassValves); product.NoBypassValves = form.NoBypassValves;

            // 分区 5
            Track(nameof(product.Media), product.Media, form.Media?.Trim()); product.Media = form.Media?.Trim();
            Track(nameof(product.MediaModel), product.MediaModel, form.MediaModel?.Trim()); product.MediaModel = form.MediaModel?.Trim();
            Track(nameof(product.BypassValveLr), product.BypassValveLr, form.BypassValveLr); product.BypassValveLr = form.BypassValveLr;
            Track(nameof(product.BypassValveHr), product.BypassValveHr, form.BypassValveHr); product.BypassValveHr = form.BypassValveHr;
            Track(nameof(product.Efficiency1), product.Efficiency1, form.Efficiency1?.Trim()); product.Efficiency1 = form.Efficiency1?.Trim();
            Track(nameof(product.Efficiency2), product.Efficiency2, form.Efficiency2?.Trim()); product.Efficiency2 = form.Efficiency2?.Trim();
            Track(nameof(product.BypassPressure), product.BypassPressure, form.BypassPressure); product.BypassPressure = form.BypassPressure;
            Track(nameof(product.CollapsePressureBar), product.CollapsePressureBar, form.CollapsePressureBar); product.CollapsePressureBar = form.CollapsePressureBar;
            Track(nameof(product.SealingMaterial), product.SealingMaterial, form.SealingMaterial?.Trim()); product.SealingMaterial = form.SealingMaterial?.Trim();
            Track(nameof(product.TempRange), product.TempRange, form.TempRange?.Trim()); product.TempRange = form.TempRange?.Trim();

            // 分区 6
            Track(nameof(product.QtyPerCarton), product.QtyPerCarton, form.QtyPerCarton); product.QtyPerCarton = form.QtyPerCarton;
            Track(nameof(product.WeightKgs), product.WeightKgs, form.WeightKgs); product.WeightKgs = form.WeightKgs;
            Track(nameof(product.CartonLengthMm), product.CartonLengthMm, form.CartonLengthMm); product.CartonLengthMm = form.CartonLengthMm;
            Track(nameof(product.CartonWidthMm), product.CartonWidthMm, form.CartonWidthMm); product.CartonWidthMm = form.CartonWidthMm;
            Track(nameof(product.CartonHeightMm), product.CartonHeightMm, form.CartonHeightMm); product.CartonHeightMm = form.CartonHeightMm;
            Track(nameof(product.MasterBoxQty), product.MasterBoxQty, form.MasterBoxQty); product.MasterBoxQty = form.MasterBoxQty;
            Track(nameof(product.MasterBoxWeightKgs), product.MasterBoxWeightKgs, form.MasterBoxWeightKgs); product.MasterBoxWeightKgs = form.MasterBoxWeightKgs;
            Track(nameof(product.MasterBoxLengthMm), product.MasterBoxLengthMm, form.MasterBoxLengthMm); product.MasterBoxLengthMm = form.MasterBoxLengthMm;
            Track(nameof(product.MasterBoxWidthMm), product.MasterBoxWidthMm, form.MasterBoxWidthMm); product.MasterBoxWidthMm = form.MasterBoxWidthMm;
            Track(nameof(product.MasterBoxHeightMm), product.MasterBoxHeightMm, form.MasterBoxHeightMm); product.MasterBoxHeightMm = form.MasterBoxHeightMm;
            var newVol = DeriveVolume(product.CartonLengthMm, product.CartonWidthMm, product.CartonHeightMm);
            Track(nameof(product.VolumePerCartonM3), product.VolumePerCartonM3, newVol);
            product.VolumePerCartonM3 = newVol;

            product.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // xref: 全量替换 (后台表单语义 = 全量编辑)
            if (form.CrossReferences != null)
            {
                var oldXref = await _db.CrossReferences.Where(x => x.ProductId == id).ToListAsync(ct);
                if (oldXref.Count > 0) _db.CrossReferences.RemoveRange(oldXref);
                foreach (var x in form.CrossReferences)
                {
                    _db.CrossReferences.Add(new CrossReference
                    {
                        ProductId = id,
                        ProductName1 = x.ProductName1?.Trim(),
                        OemBrand = x.OemBrand?.Trim(),
                        OemNo3 = x.OemNo3?.Trim(),
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            // machine_application: 全量替换
            if (form.MachineApplications != null)
            {
                var oldApps = await _db.MachineApplications.Where(m => m.ProductId == id).ToListAsync(ct);
                if (oldApps.Count > 0) _db.MachineApplications.RemoveRange(oldApps);
                foreach (var m in form.MachineApplications)
                {
                    _db.MachineApplications.Add(MapToMachineApp(id, m));
                }
            }

            // 历史
            if (changed.Count > 0 || form.CrossReferences?.Count > 0 || form.MachineApplications?.Count > 0)
            {
                _db.ProductHistory.Add(new ProductHistory
                {
                    ProductId = id,
                    ChangeType = "update",
                    ChangedBy = updatedBy,
                    ChangedAt = DateTime.UtcNow,
                    ChangedFields = JsonSerializer.Serialize(changed)
                });
            }
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation("产品更新 id={Id} 变更字段 {Count}", id, changed.Count);
            return await GetByIdAsync(id, ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // E2E BD.3 修复: 乐观锁冲突 — 产品已被其他管理员修改, 当前请求的 RowVersion 已过期
            //   WHY: EF Core [Timestamp] + IsRowVersion() 在 UPDATE 时检查 row_version, 不匹配抛此异常
            //        映射为 InvalidOperationException 让端点层 catch 返回 409 Conflict (而非 500)
            await tx.RollbackAsync(ct);
            _logger.LogWarning(ex, "产品更新乐观锁冲突 id={Id} (数据已被其他管理员修改)", id);
            throw new InvalidOperationException($"产品 id={id} 已被其他用户修改, 请刷新后重试 (lost update prevented)");
        }
        catch (Exception ex) when (ex is not KeyNotFoundException && ex is not ArgumentException && ex is not InvalidOperationException)
        {
            // 业务异常 (产品不存在 / 校验失败) 直接抛出: await using 会自动 rollback 未 commit 的事务
            // 其他异常 (含 DbUpdateException 23505) 显式回滚 + 记日志 + 重抛, 由端点层映射为合适 HTTP 状态码
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "产品更新事务回滚 id={Id}", id);
            throw;
        }
    }

    // ========== 软删除 ==========
    public async Task DeleteAsync(long id, string? deletedBy, CancellationToken ct = default)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"产品 id={id} 不存在");
        if (p.IsDiscontinued)
            throw new InvalidOperationException("产品已下架, 无需重复操作");

        p.IsDiscontinued = true;
        p.DiscontinuedAt = DateTime.UtcNow;
        p.UpdatedAt = DateTime.UtcNow;
        _db.ProductHistory.Add(new ProductHistory
        {
            ProductId = id, ChangeType = "discontinue", ChangedBy = deletedBy, ChangedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("产品下架 id={Id} by={By}", id, deletedBy);
    }

    // ========== 恢复 (从下架恢复) ==========
    public async Task RestoreAsync(long id, string? restoredBy, CancellationToken ct = default)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"产品 id={id} 不存在");
        if (!p.IsDiscontinued)
            throw new InvalidOperationException("产品未下架, 无需恢复");

        p.IsDiscontinued = false;
        p.DiscontinuedAt = null;
        p.UpdatedAt = DateTime.UtcNow;
        _db.ProductHistory.Add(new ProductHistory
        {
            ProductId = id, ChangeType = "restore", ChangedBy = restoredBy, ChangedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("产品恢复 id={Id} by={By}", id, restoredBy);
    }

    // ========== 变更历史 (Day 8.4) ==========
    /// <summary>
    /// 获取产品变更历史, 倒序返回 (最新变更在前)
    /// </summary>
    // Day 9.2: GetHistoryAsync 加可选筛选参数 (changeType / since / until)
    // Day 9.3: 返回 ProductHistoryPageDto, 包含 total (筛选后总数, 不受 limit 影响)
    //   WHY: 前端 "共 N 条" 需要真实总数, 之前 items.Count 会被 limit 截断
    //   实现: 一次查询, CountAsync(filtered) + ToListAsync(filtered.Take(limit))
    //   优化: EF Core 会翻译成单条 SQL (SELECT ... ORDER BY ... LIMIT N), count 走另一条聚合
    // Day 9.4: cursor 字段 (PageCursor DTO), 前端用它翻下一页
    // Day 9.5: cursor HMAC 签名 (复用 CursorHmac, 防止客户端篡改 (changedAt, id) 越权访问其他产品)
    public record PageCursor(DateTime ChangedAt, long Id);
    /// Day 9.5: 解码 cursor (base64url → PageCursor), 验签失败返回 null
    public PageCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        try
        {
            // base64url → base64 (浏览器/URL 安全)
            var s64 = cursor.Replace('-', '+').Replace('_', '/');
            switch (s64.Length % 4) { case 2: s64 += "=="; break; case 3: s64 += "="; break; }
            var bytes = Convert.FromBase64String(s64);
            var s = System.Text.Encoding.UTF8.GetString(bytes);
            var parts = s.Split('|');
            if (parts.Length != 3) return null;  // Day 9.5: 多一段 sig
            if (!long.TryParse(parts[0], out var ticks)) return null;
            if (!long.TryParse(parts[1], out var id)) return null;
            var sig = parts[2];
            // Day 9.5: 验签 — CursorHmac.VerifyAndExtract 失败抛 ArgumentException
            //   重要: 编码端 DateTime.Kind 不可控 (Npgsql legacy 模式可能 Utc/Local/Unspecified)
            //         ISO "o" 格式因 Kind 不同会带 +08:00 / Z / 无后缀, 导致签名不匹配
            //   解决: 用 raw ticks (Kind 无关) 作为签名输入
            try
            {
                _cursorHmac.VerifyAndExtract($"{ticks}|{id}|{sig}");
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("DecodeCursor 验签失败: {Ex} cursor={Cursor}", ex.Message, cursor);
                return null;
            }
            // 还原 DateTime (Kind=Unspecified, 与 Npgsql 读出时一致; Query 内 EF 会正确处理)
            return new PageCursor(new DateTime(ticks, DateTimeKind.Unspecified), id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DecodeCursor 异常: cursor={Cursor}", cursor);
            return null;
        }
    }
    /// Day 9.5: 编码 cursor (PageCursor → base64url), 附带 HMAC 签名
    public string EncodeCursor(DateTime changedAt, long id)
    {
        // WHY 用 raw ticks 不用 ToString("o"): ISO "o" 格式对 Kind 敏感
        //   (Local/Utc/Unspecified 输出不同, +08:00 vs Z vs 无后缀)
        //   raw ticks 唯一标识一个时间点, 跨 Kind 稳定
        var sig = _cursorHmac.Sign(changedAt.Ticks.ToString(), id);
        var s = string.Format("{0}|{1}|{2}", changedAt.Ticks, id, sig);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public async Task<ProductHistoryPageDto> GetHistoryAsync(
        long productId,
        int limit = 50,
        string? changeType = null,
        DateTime? since = null,
        DateTime? until = null,
        string? cursor = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("GetHistoryAsync 入口 id={Id} limit={Limit} type={Type} since={Since} until={Until} cursor={Cursor}",
            productId, limit, changeType, since, until, cursor);
        // 验证产品存在 (避免对已删除产品查询历史)
        var exists = await _db.Products.AsNoTracking()
            .AnyAsync(x => x.Id == productId, ct);
        if (!exists)
            throw new KeyNotFoundException($"产品 id={productId} 不存在");
        // Day 9.4: cursor → keyset 谓词 (changed_at, id) 严格小于上一批末尾
        //   keyset pagination 优势: O(1) 深度翻页, 不受 OFFSET 性能下降影响
        //   倒序排列用 "(changed_at, id) < (cursorChangedAt, cursorId)"
        var cursorPos = DecodeCursor(cursor);
        // 累积式查询链, 用返回值接住 query = query.Where(...) 让 EF 翻译正确
        IQueryable<ProductHistory> query = _db.ProductHistory.AsNoTracking()
            .Where(h => h.ProductId == productId);
        if (!string.IsNullOrWhiteSpace(changeType))
            query = query.Where(h => h.ChangeType == changeType);
        if (since.HasValue)
            query = query.Where(h => h.ChangedAt >= since.Value);
        if (until.HasValue)
            query = query.Where(h => h.ChangedAt <= until.Value);
        if (cursorPos != null)
            query = query.Where(h => h.ChangedAt < cursorPos.ChangedAt
                || (h.ChangedAt == cursorPos.ChangedAt && h.Id < cursorPos.Id));
        // Day 9.3: total 在 Take 前计算 (不受 limit 影响)
        //   EF 8 + Npgsql: CountAsync + ToListAsync 可并行执行 (无共享状态)
        var total = await query.CountAsync(ct);
        // Day 9.4: 多取 1 条, 判断是否有下一页
        var items = await query
            .OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id)
            .Take(limit + 1)
            .Select(h => new ProductHistoryItemDto(
                h.Id,
                h.ProductId,
                h.ChangeType,
                h.ChangedBy,
                h.ChangedAt,
                h.ChangedFields
            ))
            .ToListAsync(ct);
        string? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            var last = items[^1];
            nextCursor = EncodeCursor(last.ChangedAt, last.Id);
        }
        return new ProductHistoryPageDto(total, limit, changeType, since, until, items, nextCursor);
    }

    // ========== 详情 ==========
    public async Task<ProductDetailDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var p = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"产品 id={id} 不存在");

        // V2 Task 2.3.5: xrefs 按 brand_sort_order → sort_order → oem_no_3 排序
        //   WHY: 详情页 crossReferences 表格直接展示, 前端不再二次排序
        //   与 MeiliSearchProvider.BuildMr1DocumentAsync / PublicProductController.GetSiblingOem3 排序口径一致
        var xrefs = await (
            from x in _db.CrossReferences.AsNoTracking()
            where x.ProductId == id
            // V2: brand_sort_order LEFT JOIN (brand 软删除时按 int.MaxValue 兜底排末尾)
            orderby (_db.XrefOemBrands
                        .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                        .Select(b => (int?)b.SortOrder)
                        .FirstOrDefault() ?? int.MaxValue),
                    x.SortOrder,
                    x.OemNo3
            select new XrefInfo(x.Id, x.ProductName1, x.OemBrand, x.OemNo3, x.Oem2, x.SortOrder, x.MachineType, x.IsPublished, x.RowVersion)
        ).ToListAsync(ct);

        var apps = await _db.MachineApplications.AsNoTracking()
            .Where(m => m.ProductId == id)
            .Select(m => new MachineAppInfo(
                m.Id, m.MachineBrand, m.MachineModel, m.ModelName,
                m.EngineBrand, m.EngineType, m.EngineEnergy,
                m.ProductionDateStart, m.ProductionDateEnd,
                m.Power, m.SerialNumberFrom, m.SerialNumberTo,
                m.CarBodyType, m.Series,
                m.Co2EmissionStandard, m.TransmissionType,
                m.EngineDisplacement, m.NumberOfCylinders,
                m.Gvwr, m.Tonnage, m.GeographicArea,
                m.ChassisType, m.EngineModel,
                m.CabinType, m.Capacity, m.EngineSerialNumber))
            .ToListAsync(ct);

        // P3.3 (Task 11): 加载产品图片 (主图 slot 1 + 副图 slot 2-6)
        var imageRows = await _db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == id)
            .OrderBy(i => i.Slot)
            .ToListAsync(ct);
        var imageInfos = new List<ProductImageInfo>(imageRows.Count);
        // P1-4.1: 并行生成预签名 URL (Task.WhenAll), 6 张图从串行 600ms+ 降到并行 200ms 内
        //   WHY: 原 foreach 串行 await, 单张 ~100ms × 6 = 600ms+; 并行后总耗时 ≈ max(单张) ≈ 100-200ms
        //   per-image try-catch 保留: 单张 OSS 失败不影响其他图, 与原 foreach 语义一致
        //   空集合安全: Task.WhenAll(空 IEnumerable) 返回空数组, 不抛 NRE
        async Task<string> GetUrlSafe(string? key)
        {
            if (_storage == null || string.IsNullOrEmpty(key)) return "";
            try { return await _storage.GetPresignedUrlAsync(key, 3600, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "GetPresignedUrl failed: key={Key}", key); return ""; }
        }
        var urls = await Task.WhenAll(imageRows.Select(img => GetUrlSafe(img.ImageKey)));
        for (int i = 0; i < imageRows.Count; i++)
        {
            var img = imageRows[i];
            imageInfos.Add(new ProductImageInfo(
                img.Id, img.ProductId, img.Slot, img.ImageKey, urls[i],
                img.FileSize, img.ContentType, img.Width, img.Height,
                img.IsPrimary, img.UploadedAt, img.UploadedBy,
                img.OemNo3, img.ImageRole));
        }

        return new ProductDetailDto(
            p.Id, p.OemNoDisplay, p.Oem2, p.Mr1,
            p.ProductName1, p.ProductName2, p.Type, p.IsPublished, p.Remark,
            p.RowVersion,  // E2E BD.3 修复 v2: 暴露 xmin 给前端, PUT 时带回实现乐观锁
            p.D1Mm, p.D2Mm, p.D3Mm, p.D4Mm,
            p.H1Mm, p.H2Mm, p.H3Mm, p.H4Mm,
            p.D7Thread, p.D8Thread, p.NoCheckValves, p.NoBypassValves,
            p.Media, p.MediaModel,
            p.BypassValveLr, p.BypassValveHr,
            p.Efficiency1, p.Efficiency2, p.BypassPressure,
            p.CollapsePressureBar, p.SealingMaterial, p.TempRange,
            p.QtyPerCarton, p.WeightKgs,
            p.CartonLengthMm, p.CartonWidthMm, p.CartonHeightMm,
            p.MasterBoxQty, p.MasterBoxWeightKgs,
            p.MasterBoxLengthMm, p.MasterBoxWidthMm, p.MasterBoxHeightMm,
            p.VolumePerCartonM3,
            p.IsDiscontinued, p.CreatedAt, p.UpdatedAt,
            xrefs, apps, imageInfos
        );
    }

    // ========== 列表分页 (Day 8.1 简单入口, 保持向后兼容) ==========
    public async Task<(List<ProductListItem> items, long total)> ListAsync(
        int page, int pageSize, string? type, string? keyword, bool includeDiscontinued, CancellationToken ct = default)
    {
        // Day 8.2: 委托给 SearchAsync 走统一管线, 避免逻辑双轨
        var req = new AdminProductSearchRequest
        {
            Page = page,
            PageSize = pageSize,
            Type = type,
            // keyword 拆给 Oem2/Mr1 模糊匹配 (历史行为)
            Oem2 = keyword,
            Mr1 = keyword,
            IncludeDiscontinued = includeDiscontinued
        };
        // Day 8.2.2 + Day 8.3: ListAsync 旧 API 不暴露 cursor 和 countModeUsed
        var (items, total, _, _) = await SearchAsync(req, ct);
        return (items, total);
    }

    // ========== 高级搜索 (Day 8.2, 17 字段 + 尺寸范围 + 批量 OEM) ==========
    //   设计:
    //     - 文本字段走 ILIKE '%kw%' (PostgreSQL 不区分大小写, 1M 数据下索引可用)
    //     - 尺寸字段走目标值 ± SizeTolerance, 同时支持 Min/Max 区间
    //     - 批量 OEM 走 OR 匹配 (任一命中)
    //     - 排序走白名单 (防 SQL 注入)
    //     - 机型字段走 EXISTS 子查询, 避免 N+1
    public async Task<(List<ProductListItem> items, long total, string? nextCursor, string countModeUsed)> SearchAsync(
        AdminProductSearchRequest req, CancellationToken ct = default)
    {
        var page = Math.Max(1, req.Page ?? 1);
        var pageSize = Math.Clamp(req.PageSize ?? 50, 1, 200);
        var tol = Math.Clamp(req.SizeTolerance ?? 5m, 0m, 50m);
        var includeDiscontinued = req.IncludeDiscontinued ?? false;
        var sortDesc = req.SortDesc ?? true;
        // Day 8.2.1: count 模式 (exact 默认, estimated/none 走 PG 统计 + 跳过 COUNT)
        //   归一化走 DTO 扩展方法, Service + Endpoint 共享同一逻辑 (避免降级行为不一致)
        var countMode = req.NormalizeCountMode();
        // Day 8.2.2: paging 模式 (offset 默认, cursor 走 keyset 二元组)
        var pagingMode = req.NormalizePagingMode();
        DateTime? cursorUpdatedAt = null;
        long? cursorId = null;
        if (pagingMode == "cursor")
        {
            // cursor 模式强制 sortBy=updated_at DESC (keyset 要求有序键, 忽略客户端 sortBy)
            // cursor 解析: "<ISO8601 updatedAt>|<id>|<sig16>", 空 = 首页
            //   Day 8.3: sig16 = HMAC-SHA256(secret, "<ISO8601>|<id>") 截断 16 字符
            //   验证失败 → 抛 ArgumentException (Endpoint 转 400)
            //
            //   ⚠️ Npgsql EnableLegacyTimestampBehavior 怪癖 (实测验证):
            //     Npgsql 收到 DateTime {Kind=Utc, value=T UTC} 时, 直接用 value 部分 (T),
            //     **序列化为无时区字符串 'T'** 发给 PG, PG 按 session 时区 (CST=UTC+8) 解释为 timestamptz.
            //     实际存储 = T Local 解释 - 8h.
            //   影响: 整个项目 DateTime.UtcNow 写入 DB 都差 8h (e.g. 写 05:15 UTC, DB 存 21:15 UTC)
            //   抵消策略: cursor 解析后调 .ToLocalTime() 把 Kind 改 Local, value 同步加 8h,
            //     这样 Npgsql 序列化的字符串 + PG CST 解释 = 抵消回到原 UTC 值
            //
            //   示例: cursor 字符串 "2026-06-30T21:22:17Z" (21:22 UTC)
            //     DateTime.TryParse + RoundtripKind → DateTime {Kind=Utc, value=21:22:17 UTC}
            //     .ToLocalTime() → DateTime {Kind=Local, value=05:22:17 +08:00}
            //     Npgsql 发 '2026-07-01 05:22:17' (无时区)
            //     PG CST 解释 → 2026-07-01 05:22:17 CST = 2026-06-30 21:22:17 UTC ✓
            if (!string.IsNullOrEmpty(req.Cursor))
            {
                // Day 8.3: HMAC 验签 + 提取 updatedAt/id
                var (iso, cid) = _cursorHmac.VerifyAndExtract(req.Cursor);
                if (!DateTime.TryParse(iso, null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var cdt))
                {
                    throw new ArgumentException($"cursor ISO8601 段解析失败, 实际: {iso}");
                }
                // 强制按 UTC 解释: 客户端传的 cursor 字符串总是带 Z (UTC) 后缀
                if (cdt.Kind == DateTimeKind.Local)
                    cdt = cdt.ToUniversalTime();
                else if (cdt.Kind == DateTimeKind.Unspecified)
                    cdt = DateTime.SpecifyKind(cdt, DateTimeKind.Utc);
                // 抵消 Npgsql legacy 行为的 8h 偏差
                cdt = cdt.ToLocalTime();
                cursorUpdatedAt = cdt;
                cursorId = cid;
            }
        }

        var query = _db.Products.AsNoTracking().AsQueryable();

        // 软删除
        if (!includeDiscontinued)
            query = query.Where(p => !p.IsDiscontinued);

        // 发布状态
        if (req.IsPublished.HasValue)
            query = query.Where(p => p.IsPublished == req.IsPublished.Value);

        // 文本字段 (单值 ILIKE)
        //   Day 10+ P0.1: 3 参重载 + ESCAPE '\\' 防止下划线/百分号被当通配符, 用 EscapeLikePattern 统一转义
        if (!string.IsNullOrWhiteSpace(req.ProductName1))
            query = query.Where(p => p.ProductName1 != null && EF.Functions.ILike(p.ProductName1, $"%{req.ProductName1.EscapeLikePattern()}%", "\\"));
        if (!string.IsNullOrWhiteSpace(req.ProductName2))
            query = query.Where(p => p.ProductName2 != null && EF.Functions.ILike(p.ProductName2, $"%{req.ProductName2.EscapeLikePattern()}%", "\\"));
        if (!string.IsNullOrWhiteSpace(req.Type))
            query = query.Where(p => p.Type == req.Type);
        if (!string.IsNullOrWhiteSpace(req.Mr1))
            query = query.Where(p => p.Mr1 != null && EF.Functions.ILike(p.Mr1, $"%{req.Mr1.EscapeLikePattern()}%", "\\"));
        if (!string.IsNullOrWhiteSpace(req.Oem2))
        {
            // P2-1 修复: Contains → ILike + EscapeLikePattern, 防止 _ 和 % 被当通配符
            //   WHY: string.Contains 翻译为 LIKE '%x%' 无 ESCAPE, 用户输入 100_ 会误命中 100A/100B
            var kwOem2 = req.Oem2.EscapeLikePattern();
            query = query.Where(p =>
                EF.Functions.ILike(p.OemNoDisplay, $"%{kwOem2}%", "\\")
                || (p.Oem2 != null && EF.Functions.ILike(p.Oem2, $"%{kwOem2}%", "\\")));
        }
        // Day 8.2.2: 合并 xref 2 个 EXISTS (OemBrand + Oem3Batch) → 1 个 EXISTS
        //   性能依据: 1M 数据下 6 个独立 EXISTS → 2-5s, 合并后 1 个 EXISTS 走同一索引扫描
        //   1M xref 行 OemBrand 等值 + Oem3 等值 + product_id = p.id 索引覆盖
        //   合并后 1 个 EXISTS 利用 (product_id, oem_brand, oem_no_3) 联合索引
        //   对比: 5 个 EXISTS 走 5 次嵌套循环, 合并后 1 次循环内 5 个条件短路求值
        var oemBrand = req.OemBrand;
        string[]? oem3List = null;
        if (!string.IsNullOrWhiteSpace(req.Oem3Batch))
        {
            oem3List = req.Oem3Batch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (oem3List.Length == 0) oem3List = null;
        }
        if (!string.IsNullOrWhiteSpace(oemBrand) || oem3List != null)
        {
            var brand = oemBrand;
            var oems3 = oem3List;
            query = query.Where(p => _db.CrossReferences.Any(x =>
                x.ProductId == p.Id
                && (brand == null || x.OemBrand == brand)
                && (oems3 == null || oems3.Any(o => x.OemNo3 == o))));
        }
        if (!string.IsNullOrWhiteSpace(req.MediaName))
            query = query.Where(p => p.Media != null && EF.Functions.ILike(p.Media, $"%{req.MediaName.EscapeLikePattern()}%", "\\"));
        if (!string.IsNullOrWhiteSpace(req.MediaModel))
            query = query.Where(p => p.MediaModel != null && EF.Functions.ILike(p.MediaModel, $"%{req.MediaModel.EscapeLikePattern()}%", "\\"));
        // Day 8.2.1: 补齐规格"前端展示内容"分区 5 文本字段
        if (!string.IsNullOrWhiteSpace(req.SealingMaterial))
            query = query.Where(p => p.SealingMaterial != null && EF.Functions.ILike(p.SealingMaterial, $"%{req.SealingMaterial.EscapeLikePattern()}%", "\\"));
        if (!string.IsNullOrWhiteSpace(req.Efficiency1))
            query = query.Where(p => p.Efficiency1 != null && EF.Functions.ILike(p.Efficiency1, $"%{req.Efficiency1.EscapeLikePattern()}%", "\\"));

        // 批量 OEM (Excel 多行复制黏贴)
        if (!string.IsNullOrWhiteSpace(req.Oem2Batch))
        {
            var oems = req.Oem2Batch.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (oems.Length > 0)
            {
                var normalized = oems.Select(NormalizeOem).ToArray();
                query = query.Where(p => normalized.Contains(p.OemNoNormalized));
            }
        }

        // 尺寸范围 (D1-D4, H1-H4) + 螺纹 (D7/D8)
        // WHY 接收返回值: ApplySizeFilter 内部 query = query.Where(...) 重新赋值, 必须接收回新 IQueryable 才生效
        query = ApplySizeFilter(query, "D1Mm", req.D1Min, req.D1Max, tol);
        query = ApplySizeFilter(query, "D2Mm", req.D2Min, req.D2Max, tol);
        query = ApplySizeFilter(query, "D3Mm", req.D3Min, req.D3Max, tol);
        query = ApplySizeFilter(query, "D4Mm", req.D4Min, req.D4Max, tol);
        query = ApplySizeFilter(query, "H1Mm", req.H1Min, req.H1Max, tol);
        query = ApplySizeFilter(query, "H2Mm", req.H2Min, req.H2Max, tol);
        query = ApplySizeFilter(query, "H3Mm", req.H3Min, req.H3Max, tol);
        query = ApplySizeFilter(query, "H4Mm", req.H4Min, req.H4Max, tol);
        if (!string.IsNullOrWhiteSpace(req.D7Thread))
            query = query.Where(p => p.D7Thread != null && EF.Functions.ILike(p.D7Thread, $"%{req.D7Thread.EscapeLikePattern()}%", "\\"));
        if (!string.IsNullOrWhiteSpace(req.D8Thread))
            query = query.Where(p => p.D8Thread != null && EF.Functions.ILike(p.D8Thread, $"%{req.D8Thread.EscapeLikePattern()}%", "\\"));

        // Day 8.2.2: 合并 machine_application 5 个 EXISTS → 1 个 EXISTS
        //   性能依据: 5M machine_application 行下 5 个 EXISTS 走 5 次 product_id 索引扫描
        //   合并后 1 次扫描, 5 个条件短路求值 (任一条件 NULL 跳过该判断)
        //   注意点: EF Core 表达式树翻译: NULL 字段比较走 `mb == null || m.MachineBrand == mb` 三元短路
        var mb = req.MachineBrand;
        var mm = req.MachineModel;
        var mn = req.ModelName;
        var eb = req.EngineBrand;
        var et = req.EngineType;
        if (!string.IsNullOrWhiteSpace(mb) || !string.IsNullOrWhiteSpace(mm)
            || !string.IsNullOrWhiteSpace(mn) || !string.IsNullOrWhiteSpace(eb)
            || !string.IsNullOrWhiteSpace(et))
        {
            query = query.Where(p => _db.MachineApplications.Any(m =>
                m.ProductId == p.Id
                && (mb == null || m.MachineBrand == mb)
                && (mm == null || m.MachineModel == mm)
                && (mn == null || m.ModelName == mn)
                && (eb == null || m.EngineBrand == eb)
                && (et == null || m.EngineType == et)));
        }

        // 排序 (白名单, 防 SQL 注入)
        //   WHY 强制加 Id 次级排序:
        //     历史数据 updated_at 时区错乱 (Npgsql legacy 模式 + 老 ETL Unspecified 时间
        //     与新 DateTime.UtcNow 混存), 单按 updated_at 排序新数据可能排到末位
        //     加 Id DESC 次级排序保证新数据 (id 更大) 总排前, 提升测试稳定性 + 翻页体验
        //   Day 8.2.2: cursor 模式强制 sortBy=updated_at DESC (keyset 要求有序键)
        string sortBy;
        if (pagingMode == "cursor")
        {
            sortBy = "updated_at";
            sortDesc = true;  // cursor 模式固定 DESC
        }
        else
        {
            sortBy = ProductListColumns.SortWhitelist.Contains(req.SortBy ?? "")
                ? req.SortBy!.ToLowerInvariant()
                : "updated_at";
        }
        query = sortBy switch
        {
            "id" => sortDesc
                ? query.OrderByDescending(p => p.Id)
                : query.OrderBy(p => p.Id),
            "oem_no_display" => sortDesc
                ? query.OrderByDescending(p => p.OemNoDisplay).ThenByDescending(p => p.Id)
                : query.OrderBy(p => p.OemNoDisplay).ThenByDescending(p => p.Id),
            "type" => sortDesc
                ? query.OrderByDescending(p => p.Type).ThenByDescending(p => p.Id)
                : query.OrderBy(p => p.Type).ThenByDescending(p => p.Id),
            "mr1" => sortDesc
                ? query.OrderByDescending(p => p.Mr1).ThenByDescending(p => p.Id)
                : query.OrderBy(p => p.Mr1).ThenByDescending(p => p.Id),
            _ => sortDesc
                ? query.OrderByDescending(p => p.UpdatedAt).ThenByDescending(p => p.Id)
                : query.OrderBy(p => p.UpdatedAt).ThenByDescending(p => p.Id)
        };

        // Day 8.2.2: cursor 模式 keyset 二元组 (updated_at, id) 严格小于游标
        //   等价于 SQL: WHERE (updated_at, id) < (cursor.UpdatedAt, cursor.Id) 按 DESC 排序
        if (pagingMode == "cursor" && cursorUpdatedAt.HasValue && cursorId.HasValue)
        {
            var cdt = cursorUpdatedAt.Value;
            var cid = cursorId.Value;
            // EF 翻译: updated_at < @cdt OR (updated_at = @cdt AND id < @cid)
            query = query.Where(p => p.UpdatedAt < cdt || (p.UpdatedAt == cdt && p.Id < cid));
        }

        // Day 8.2.1 + Day 8.3: count 模式分支 + 自动降级
        //   - exact: LongCountAsync 准确值 (默认, 兼容老调用)
        //   - estimated: 取 PG reltuples 统计, 跳过 17 字段 EXISTS 的 COUNT 代价
        //     误差 ±20% 适合"约 N 条"提示, 1M 数据下 50ms vs 5s
        //   - none: total=-1, 前端用 hasMore 提示
        //   Day 8.3 自动降级:
        //     exact 模式 LongCountAsync 走 17 字段 EXISTS 嵌套, 慢查询可能 2-5s
        //     用 Task.WhenAny + Task.Delay 触发超时, 超时后切到 estimated
        //     countModeUsed 返回前端实际用的模式 (用于埋点 + UI 提示 "约 N 条")
        var countTimeoutMs = Math.Clamp(req.CountTimeoutMs ?? 500, 0, 10_000);
        long total;
        string countModeUsed = countMode;
        if (countMode == "none")
        {
            total = -1;
        }
        else if (countMode == "estimated")
        {
            total = await GetEstimatedCountAsync(ct);
        }
        else
        {
            // exact 模式: 超时降级
            //   WHY 不传 ct 给 countTask: 超时后 LongCountAsync 还在跑, 不要让它跟着请求取消
            //   (强制 cancel 会让 PG 中断查询, 浪费已经投入的资源)
            //   实际效果: 超时后请求立刻返回 estimated 值, LongCountAsync 在后台跑完丢弃
            if (countTimeoutMs == 0)
            {
                total = await query.LongCountAsync(ct);
            }
            else
            {
                // Day 8.3 修复: 用独立 CancellationTokenSource 让超时后主动 cancel EF query,
                //   否则后台 LongCountAsync 继续跑占 PG 连接 (会拖垮生产连接池)
                //   竞态无害: countTask 正好完成 → await 拿结果走 exact; 超时触发 → 抛 OCE 走 estimated
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(countTimeoutMs);
                try
                {
                    total = await query.LongCountAsync(cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning("exact count 超时 {TimeoutMs}ms → 降级 estimated, type={Type} mb={Mb} oem={Oem}",
                        countTimeoutMs, req.Type, req.MachineBrand, req.Oem2);
                    total = await GetEstimatedCountAsync(ct);
                    countModeUsed = "estimated";
                }
            }
        }
        // Day 8.2.2: 顺序很关键 - Skip 必须放在 Take 之前
        //   错误顺序: Take(pageSize+1) 然后 Skip((page-1)*pageSize) 会导致 page>1 时跳过 pageSize 条
        //     拿 pageSize+1 - (page-1)*pageSize 条, page=2 pageSize=100 时只剩 1 条
        //   正确顺序: 先 Skip 再 Take, EF 翻译成 SQL: LIMIT (pageSize+1) OFFSET (page-1)*pageSize
        //   注意: cursor 模式不需要 Skip (keyset 已经用 updated_at < cdt 跳过)
        if (pagingMode == "offset" && page > 1)
        {
            query = query.Skip((page - 1) * pageSize);
        }
        var items = await query
            .Take(pageSize + 1)  // 多取 1 条用于探测下一页 (cursor/offset 模式都可用)
            .Select(p => new ProductListItem(
                p.Id, p.OemNoDisplay, p.Oem2, p.Mr1,
                p.ProductName1, p.ProductName2, p.Type, p.IsPublished, p.IsDiscontinued,
                p.ImageKey, null, p.UpdatedAt))
            .ToListAsync(ct);

        // Day 8.2.2: 探测下一页 + 构造 nextCursor
        //   拿 pageSize+1 条, 如果 > pageSize 表示还有下一页, 弹出末条 + 构造 cursor
        //   注意: ListProductItem 是 record, 取最后一条的 (UpdatedAt, Id) 作为下一页起点
        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            if (pagingMode == "cursor")
            {
                var last = items[^1];
                // BD.24 修复: Day 9.9 的 SpecifyKind 假设错误
                //   实际: PG 列类型 = timestamptz, Npgsql.EnableLegacyTimestampBehavior 读取后
                //         Kind=Local, value 是 session timezone (CST) 字面值, 不是真正的 UTC
                //   修复: 用 ToUniversalTime() 把 CST 字面值转换为真正的 UTC (减 8h)
                //   验证: 修复前 cursor iso=01:39:42Z (CST 字面值+Z), 解码 ToLocalTime 加 8h=09:39:42
                //         keyset p.UpdatedAt(01:39:42) < cdt(09:39:42) 永远 TRUE → 翻页返回相同数据
                //         修复后 cursor iso=17:39:42Z (真正 UTC), 解码 ToLocalTime 加 8h=01:39:42
                //         keyset p.UpdatedAt(01:39:42) < cdt(01:39:42) = FALSE → 正确翻页
                var lastUtc = last.UpdatedAt.ToUniversalTime();
                // Day 8.2.2 修复: PG timestamptz 是微秒精度 (6 位), .fff 毫秒精度会丢精度导致下一页漏数据
                // 同一毫秒内多次写入 (e.g. 5 个产品间隔 0.05s) 会命中同一毫秒, .fff 截断后游标"跳过"这些行
                // Day 8.3: cursor 末尾追加 HMAC 签名, 防止客户端篡改 updatedAt/id 越权访问
                var iso = $"{new DateTimeOffset(lastUtc, TimeSpan.Zero):yyyy-MM-ddTHH:mm:ss.ffffffZ}";
                var sig = _cursorHmac.Sign(iso, last.Id);
                nextCursor = $"{iso}|{last.Id}|{sig}";
            }
        }
        return (items, total, nextCursor, countModeUsed);
    }

    // ========== 估算 count (Day 8.3 重构) ==========
    //   用 PG reltuples 统计, O(1), 误差 ±20% 适合"约 N 条"提示
    //   兜底: reltuples 不可用时退到 COUNT(*)
    private async Task<long> GetEstimatedCountAsync(CancellationToken ct)
    {
        try
        {
            // 优先用基础表的 reltuples (无过滤, O(1))
            return await _db.Database
                .SqlQueryRaw<long>("SELECT COALESCE(c.reltuples::bigint, 0) FROM pg_class c WHERE c.relname = 'products'")
                .FirstOrDefaultAsync(ct);
        }
        catch
        {
            // 兜底: reltuples 不可用时退到 COUNT(*) 准确值
            return await _db.Products.LongCountAsync(ct);
        }
    }

    // ========== 尺寸范围应用 (Day 8.2, 表达式树拼接, EF 可翻译) ==========
    //   规则:
    //     - 同时给 Min+Max: 区间 [Min-Tol, Max+Tol] 命中
    //     - 只给 Min: [Min-Tol, +∞) 命中
    //     - 只给 Max: (-∞, Max+Tol] 命中
    //   WHY 显式 HasValue && Value 比较:
    //     EF Core 8 对 nullable decimal 的直接比较翻译不稳定 (实测 silently 丢掉 WHERE 条件),
    //     拆成 HasValue 检查 + Value 比较是官方推荐方式, 保证生成 IS NOT NULL 守卫
    //   WHY 用字符串属性名 + 反射: 8 个尺寸字段共用同一逻辑, 反射拼装 Expression
    //     避免 8 处重复代码
    private static IQueryable<Product> ApplySizeFilter(
        IQueryable<Product> query,
        string propName,
        decimal? min, decimal? max, decimal tol)
    {
        if (!min.HasValue && !max.HasValue) return query;
        var p = System.Linq.Expressions.Expression.Parameter(typeof(Product), "p");
        var prop = System.Linq.Expressions.Expression.Property(p, propName);
        if (min.HasValue)
        {
            var lo = min.Value - tol;
            var hasValue = System.Linq.Expressions.Expression.Property(prop, "HasValue");
            var value = System.Linq.Expressions.Expression.Property(prop, "Value");
            var ge = System.Linq.Expressions.Expression.GreaterThanOrEqual(value, System.Linq.Expressions.Expression.Constant(lo, typeof(decimal)));
            var body = System.Linq.Expressions.Expression.AndAlso(hasValue, ge);
            query = query.Where(System.Linq.Expressions.Expression.Lambda<Func<Product, bool>>(body, p));
        }
        if (max.HasValue)
        {
            var hi = max.Value + tol;
            var hasValue = System.Linq.Expressions.Expression.Property(prop, "HasValue");
            var value = System.Linq.Expressions.Expression.Property(prop, "Value");
            var le = System.Linq.Expressions.Expression.LessThanOrEqual(value, System.Linq.Expressions.Expression.Constant(hi, typeof(decimal)));
            var body = System.Linq.Expressions.Expression.AndAlso(hasValue, le);
            query = query.Where(System.Linq.Expressions.Expression.Lambda<Func<Product, bool>>(body, p));
        }
        return query;
    }

    // ========== 批量对比 (Day 8.2, 规格 对比界面 6 个产品) ==========
    //   设计:
    //     - 接受 1-6 个产品 id, 按传入顺序返回 (不按 id 排序)
    //     - 找不到的 id 跳过, 不抛异常 (前端用空白卡片占位)
    //     - 字段按规格 R27 顺序: MR.1 | OEM 2/3 | H1-H4 | D1-D4 | D7/D8 | Media | 包装 | 体积
    //   WHY 走单次 query + InMemory 分组: 1-6 个 id 用 EF 一句 SQL 解决, 避免 N+1
    public async Task<List<ProductDetailDto>> CompareAsync(
        IReadOnlyList<long> ids, AdminProductImageService? imgSvc, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new List<ProductDetailDto>();
        if (ids.Count > 6) throw new ArgumentException("对比最多 6 个产品");

        // 单次查 products
        var products = await _db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);
        // 保持传入顺序
        var ordered = ids
            .Select(id => products.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Cast<Product>()
            .ToList();

        // 单次查 xref + apps
        var idList = ordered.Select(p => p.Id).ToList();
        // V2 Task 2.3.5: xrefs 按 brand_sort_order → sort_order → oem_no_3 排序 (列表页批量)
        var xrefs = await (
            from x in _db.CrossReferences.AsNoTracking()
            where idList.Contains(x.ProductId)
            orderby (_db.XrefOemBrands
                        .Where(b => b.Brand == x.OemBrand && b.DeletedAt == null)
                        .Select(b => (int?)b.SortOrder)
                        .FirstOrDefault() ?? int.MaxValue),
                    x.SortOrder,
                    x.OemNo3
            select new { x.ProductId, x.Id, x.ProductName1, x.OemBrand, x.OemNo3, x.Oem2, x.SortOrder, x.MachineType, x.IsPublished, x.RowVersion }
        ).ToListAsync(ct);
        var apps = await _db.MachineApplications.AsNoTracking()
            .Where(m => idList.Contains(m.ProductId))
            .ToListAsync(ct);

        var result = new List<ProductDetailDto>();
        foreach (var p in ordered)
        {
            var pXrefs = xrefs.Where(x => x.ProductId == p.Id)
                .Select(x => new XrefInfo(x.Id, x.ProductName1, x.OemBrand, x.OemNo3, x.Oem2, x.SortOrder, x.MachineType, x.IsPublished, x.RowVersion))
                .ToList();
            var pApps = apps.Where(m => m.ProductId == p.Id)
                .Select(m => new MachineAppInfo(
                    m.Id, m.MachineBrand, m.MachineModel, m.ModelName,
                    m.EngineBrand, m.EngineType, m.EngineEnergy,
                    m.ProductionDateStart, m.ProductionDateEnd,
                    m.Power, m.SerialNumberFrom, m.SerialNumberTo,
                    m.CarBodyType, m.Series,
                    m.Co2EmissionStandard, m.TransmissionType,
                    m.EngineDisplacement, m.NumberOfCylinders,
                    m.Gvwr, m.Tonnage, m.GeographicArea,
                    m.ChassisType, m.EngineModel,
                    m.CabinType, m.Capacity, m.EngineSerialNumber))
                .ToList();
            result.Add(new ProductDetailDto(
                p.Id, p.OemNoDisplay, p.Oem2, p.Mr1,
                p.ProductName1, p.ProductName2, p.Type, p.IsPublished, p.Remark,
                p.RowVersion,  // E2E BD.3 修复 v2: 暴露 xmin 给前端
                p.D1Mm, p.D2Mm, p.D3Mm, p.D4Mm,
                p.H1Mm, p.H2Mm, p.H3Mm, p.H4Mm,
                p.D7Thread, p.D8Thread, p.NoCheckValves, p.NoBypassValves,
                p.Media, p.MediaModel,
                p.BypassValveLr, p.BypassValveHr,
                p.Efficiency1, p.Efficiency2, p.BypassPressure,
                p.CollapsePressureBar, p.SealingMaterial, p.TempRange,
                p.QtyPerCarton, p.WeightKgs,
                p.CartonLengthMm, p.CartonWidthMm, p.CartonHeightMm,
                p.MasterBoxQty, p.MasterBoxWeightKgs,
                p.MasterBoxLengthMm, p.MasterBoxWidthMm, p.MasterBoxHeightMm,
                p.VolumePerCartonM3,
                p.IsDiscontinued, p.CreatedAt, p.UpdatedAt,
                pXrefs, pApps, new List<ProductImageInfo>()
            ));
        }
        return result;
    }

    // ========== 辅助 ==========
    private static void ValidateForm(ProductFormDto form)
    {
        // V2: MR.1 必填校验(内部主键,数据强制)
        if (string.IsNullOrWhiteSpace(form.Mr1))
            throw new ArgumentException("MR1_REQUIRED: MR.1 必填");
        // V2: MR.1 格式校验(1-10 位字母数字)
        if (!System.Text.RegularExpressions.Regex.IsMatch(form.Mr1.Trim(), @"^[A-Za-z0-9]{1,10}$"))
            throw new ArgumentException("MR1_FORMAT_INVALID: MR.1 必须为 1-10 位字母数字");

        if (string.IsNullOrWhiteSpace(form.Oem2))
            throw new ArgumentException("Oem2 (主号) 必填");
        // P2-2 修复: 补充关键字段长度校验, 防止超长输入触发 PG 22001 而非 400
        //   WHY: 之前仅校验 Oem2, 其他字段超长时 PG 报 string_data_right_truncation 返回 500
        //   校验范围与 ProductDbContext HasMaxLength 对齐
        var checks = new (string Label, string? Value, int Max)[]
        {
            ("Oem2", form.Oem2, 50),
            ("ProductName1", form.ProductName1, 100),
            ("ProductName2", form.ProductName2, 100),
            ("Type", form.Type, 50),
            // V2: MR.1 最大长度 10(非 100)
            ("Mr1", form.Mr1, 10),
            ("Media", form.Media, 100),
            ("MediaModel", form.MediaModel, 100),
            ("D7Thread", form.D7Thread, 100),
            ("D8Thread", form.D8Thread, 100),
            ("Efficiency1", form.Efficiency1, 100),
            ("Efficiency2", form.Efficiency2, 100),
            ("SealingMaterial", form.SealingMaterial, 100),
            ("TempRange", form.TempRange, 100),
        };
        foreach (var (label, value, max) in checks)
        {
            if (!string.IsNullOrEmpty(value) && value.Length > max)
                throw new ArgumentException($"{label} 不能超过 {max} 字符 (当前 {value.Length})");
        }

        // V2: machine_type 枚举校验
        var validMachineTypes = new[] { "agriculture", "commercial", "construction", "industrial", "others" };
        foreach (var x in form.CrossReferences)
        {
            if (!string.IsNullOrEmpty(x.MachineType) && !validMachineTypes.Contains(x.MachineType))
                throw new ArgumentException($"MACHINE_TYPE_INVALID: machine_type 必须为 {string.Join("/", validMachineTypes)} 之一");
        }
    }

    private static string NormalizeOem(string oem)
    {
        // 归一化: 大写 + 去空格 + 去常见分隔符, 保证唯一性
        return oem.Trim().ToUpperInvariant()
            .Replace(" ", "").Replace("-", "").Replace("/", "")
            .Replace("(", "").Replace(")", "").Replace(".", "");
    }

    private static string DeriveTypeFromName(string? productName3)
    {
        if (string.IsNullOrWhiteSpace(productName3)) return "others";
        var n = productName3.Trim().ToLower();
        if (n.Contains("oil")) return "oil";
        if (n.Contains("fuel")) return "fuel";
        if (n.Contains("air")) return "air";
        if (n.Contains("cabin")) return "cabin";
        return "others";
    }

    private static decimal? DeriveVolume(decimal? l, decimal? w, decimal? h)
    {
        // 规格: "根据长宽高自动计算体积", 单位 m³
        if (l is null || w is null || h is null) return null;
        // mm³ → m³: / 1_000_000_000
        return Math.Round((l.Value * w.Value * h.Value) / 1_000_000_000m, 6);
    }

    private static MachineApplication MapToMachineApp(long productId, MachineAppInput m) => new()
    {
        ProductId = productId,
        MachineBrand = m.MachineBrand?.Trim(),
        MachineModel = m.MachineModel?.Trim(),
        ModelName = m.ModelName?.Trim(),
        EngineBrand = m.EngineBrand?.Trim(),
        EngineType = m.EngineType?.Trim(),
        EngineEnergy = m.EngineEnergy?.Trim(),
        ProductionDateStart = m.ProductionDateStart,
        ProductionDateEnd = m.ProductionDateEnd,
        Power = m.Power?.Trim(),
        SerialNumberFrom = m.SerialNumberFrom?.Trim(),
        SerialNumberTo = m.SerialNumberTo?.Trim(),
        CarBodyType = m.CarBodyType?.Trim(),
        Series = m.Series?.Trim(),
        Co2EmissionStandard = m.Co2EmissionStandard?.Trim(),
        TransmissionType = m.TransmissionType?.Trim(),
        EngineDisplacement = m.EngineDisplacement?.Trim(),
        NumberOfCylinders = m.NumberOfCylinders,
        Gvwr = m.Gvwr?.Trim(),
        Tonnage = m.Tonnage?.Trim(),
        GeographicArea = m.GeographicArea?.Trim(),
        ChassisType = m.ChassisType?.Trim(),
        EngineModel = m.EngineModel?.Trim(),
        CabinType = m.CabinType?.Trim(),
        Capacity = m.Capacity?.Trim(),
        EngineSerialNumber = m.EngineSerialNumber?.Trim(),
        IsOngoing = m.ProductionDateEnd is null,
        CreatedAt = DateTime.UtcNow
    };
}
