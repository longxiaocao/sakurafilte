using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 产品图片管理服务 (Day 8.1)
/// 用途: 支撑后台分区 4 录入 (1-6 张图, S3 兼容存储 / MinIO)
/// 设计:
///   - S3 key 命名: products/{oem_no_normalized}/{oem_no_normalized}-{slot}.{ext}
///     WHY 用 oem_normalized 而非 oem_display: 不同大小写/分隔符的同一产品指向同一目录, 避免重复图
///     WHY 用 slot: 同 OEM 6 张图按 slot 编号, 覆盖上传时 key 稳定
///   - 上传走 IObjectStorage 抽象 (MinIO / AliyunOSS 可切换)
///   - product_images 表记录 (product_id, slot) UNIQUE, 覆盖上传只换 key
///   - 主图 (slot=1) 同步写 products.image_key, 兼容旧字段
/// </summary>
public class AdminProductImageService
{
    private readonly ProductDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminProductImageService> _logger;

    public AdminProductImageService(
        ProductDbContext db,
        IObjectStorage storage,
        IConfiguration config,
        ILogger<AdminProductImageService> logger)
    {
        _db = db;
        _storage = storage;
        _config = config;
        _logger = logger;
    }

    // ========== 上传单张图 ==========
    // P0-1.2 事务重构: 顺序改为 "DB 事务占位 → S3 上传 → DB 提交 → 异步删旧"
    //   WHY: 旧顺序 "S3 上传 → 异步删旧 → DB 提交" 存在两类数据一致性缺陷:
    //     1) S3 上传成功后 DB 失败 → S3 孤儿对象 (无 DB 记录指向它, 无法回收)
    //     2) 覆盖上传时异步删旧图在 DB 提交前启动, 若 DB 失败 → 旧图已删 + 新图未入库,
    //        用户图片完全丢失
    //   新顺序保证:
    //     - S3 失败 → DB 事务回滚, 不留孤儿对象
    //     - DB 提交成功后才删旧文件, 旧图无引用才删, 不会丢用户图片
    public async Task<ProductImageInfo> UploadAsync(
        long productId, short slot, Stream stream, string contentType,
        string? uploadedBy, CancellationToken ct = default)
    {
        if (slot < 1 || slot > 6) throw new ArgumentException("slot 必须在 1-6 之间");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new KeyNotFoundException($"产品 id={productId} 不存在");

        // 校验大小
        var maxBytes = _config.GetValue<long?>("Minio:ImageMaxBytes") ?? 10L * 1024 * 1024;
        if (stream.Length > maxBytes)
            throw new InvalidOperationException($"图片超过最大尺寸 {maxBytes / 1024 / 1024}MB");

        // 校验类型
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(contentType.ToLowerInvariant()))
            throw new InvalidOperationException($"不支持的图片类型: {contentType}");

        var ext = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "bin"
        };
        var key = BuildKey(product.OemNoNormalized, slot, ext);
        var sizeBytes = stream.Length;  // 提前缓存: S3 上传后 stream 可能不可再读

        // ===== 1. DB 事务占位: 先写 DB (覆盖旧记录 / 新增记录 / 主图同步), 但不提交 =====
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var old = await _db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == productId && i.Slot == slot, ct);
        // 记录旧 key, DB 提交成功后再异步删 (避免 DB 失败时旧图已被删 → 用户图片丢失)
        var oldKeyToDelete = old?.ImageKey;

        ProductImage saved;
        if (old != null)
        {
            // 覆盖上传: 更新已有记录指向新 key
            old.ImageKey = key;
            old.FileSize = sizeBytes;
            old.ContentType = contentType;
            old.UploadedAt = DateTime.UtcNow;
            old.UploadedBy = uploadedBy;
            old.IsPrimary = slot == 1;
            old.DisplayOrder = slot;
            saved = old;
        }
        else
        {
            // 新增上传: 创建 ProductImage 记录
            saved = new ProductImage
            {
                ProductId = productId,
                Slot = slot,
                ImageKey = key,
                FileSize = sizeBytes,
                ContentType = contentType,
                IsPrimary = slot == 1,
                DisplayOrder = slot,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = uploadedBy
            };
            _db.ProductImages.Add(saved);
        }

        // 主图 (slot=1) 同步写 products.image_key, 兼容旧字段
        // WHY ImageStatus=pending: 上传后等待后续校验/发布流程置 ready (P0 仅保证事务一致, 状态机后续任务维护)
        if (slot == 1)
        {
            product.ImageKey = key;
            product.ImageStatus = "pending";
            product.UpdatedAt = DateTime.UtcNow;
        }

        // 事务内保存: 新增场景拿到 saved.Id, 但事务尚未提交 (S3 失败可整体回滚)
        await _db.SaveChangesAsync(ct);

        // ===== 2. S3 上传: 失败则回滚 DB 事务, 不留孤儿对象 =====
        try
        {
            await _storage.UploadAsync(key, stream, contentType, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "S3 上传失败, DB 事务已回滚 productId={ProductId} slot={Slot} key={Key}", productId, slot, key);
            throw;
        }

        // ===== 3. DB 提交: S3 上传成功后正式提交事务 =====
        await tx.CommitAsync(ct);

        // ===== 4. 异步删旧文件 (DB 提交后): 仅当旧 key 存在且与新 key 不同 =====
        //   WHY 不同: 同 key (相同 ext) 时 S3 已覆盖, 无需再删
        //   WHY 提交后: DB 已确认新图生效, 旧图无引用, 删除不会导致用户图片丢失
        //   WHY 异步: 删旧不阻塞响应, 失败仅 LogWarning (旧文件后台 GC 或人工清理)
        if (!string.IsNullOrEmpty(oldKeyToDelete) && oldKeyToDelete != key)
        {
            var staleKey = oldKeyToDelete;
            _ = Task.Run(async () =>
            {
                try { await _storage.DeleteAsync(staleKey); }
                catch (Exception ex) { _logger.LogWarning(ex, "旧图删除失败 key={Key}", staleKey); }
            });
        }

        _logger.LogInformation("产品图上传成功 productId={ProductId} slot={Slot} key={Key}", productId, slot, key);
        return ToInfo(saved, await GetUrlAsync(key));
    }

    // ========== 删除单张图 ==========
    public async Task DeleteAsync(long productId, short slot, CancellationToken ct = default)
    {
        if (slot < 1 || slot > 6) throw new ArgumentException("slot 必须在 1-6 之间");
        var img = await _db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == productId && i.Slot == slot, ct)
            ?? throw new KeyNotFoundException($"产品 {productId} 的 slot {slot} 不存在图");

        _db.ProductImages.Remove(img);
        await _db.SaveChangesAsync(ct);

        // 主图删除, 清 product.image_key
        if (slot == 1)
        {
            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId, ct);
            if (p != null) { p.ImageKey = null; p.ImageStatus = "pending"; p.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); }
        }

        // 异步删文件
        var key = img.ImageKey;
        _ = Task.Run(async () =>
        {
            try { await _storage.DeleteAsync(key); }
            catch (Exception ex) { _logger.LogWarning(ex, "图文件删除失败 key={Key}", key); }
        });
        _logger.LogInformation("产品图删除 productId={Id} slot={Slot} key={Key}", productId, slot, key);
    }

    // ========== 列出产品 6 张图 ==========
    public async Task<List<ProductImageInfo>> ListAsync(long productId, CancellationToken ct = default)
    {
        var imgs = await _db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.Slot)
            .ToListAsync(ct);
        // P1-4.2: 并行生成预签名 URL (Task.WhenAll), 6 张图从串行 600ms+ 降到并行 200ms 内
        //   WHY: 原 foreach 串行 await, 单张 ~100ms × 6 = 600ms+; 并行后总耗时 ≈ max(单张) ≈ 100-200ms
        //   GetUrlAsync 内已有 try-catch (失败返回空串), 单张失败不影响其他图
        //   空集合安全: Task.WhenAll(空 IEnumerable) 返回空数组, 不抛 NRE
        var urls = await Task.WhenAll(imgs.Select(i => GetUrlAsync(i.ImageKey)));
        var result = new List<ProductImageInfo>(imgs.Count);
        for (int i = 0; i < imgs.Count; i++)
            result.Add(ToInfo(imgs[i], urls[i]));
        return result;
    }

    // ========== 辅助 ==========
    public static string BuildKey(string oemNormalized, short slot, string ext)
    {
        // 产品图 1-6 走同目录, slot 后缀区分
        // WHY 同目录: 运营可一次性浏览某产品的全部图
        // WHY 不放日期分目录: 1M 产品图数量大, 日期分目录带来额外 IO, 暂不需要
        return $"products/{oemNormalized}/{oemNormalized}-{slot}.{ext}";
    }

    private async Task<string> GetUrlAsync(string key)
    {
        try
        {
            return await Task.Run(() => _storage.GetUrl(key, 3600));
        }
        catch
        {
            return "";
        }
    }

    private static ProductImageInfo ToInfo(ProductImage i, string url) => new(
        i.Id, i.ProductId, i.Slot, i.ImageKey, url,
        i.FileSize, i.ContentType, i.Width, i.Height, i.IsPrimary,
        i.UploadedAt, i.UploadedBy, i.OemNo3, i.ImageRole
    );
}
