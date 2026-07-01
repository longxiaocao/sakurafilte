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

        // 上传到对象存储
        await _storage.UploadAsync(key, stream, contentType, ct);

        // 查旧图 (覆盖上传) → 先删旧文件
        var old = await _db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == productId && i.Slot == slot, ct);
        if (old != null)
        {
            // 异步删除旧文件 (失败不阻塞, 旧文件后台 GC)
            _ = Task.Run(async () =>
            {
                try { await _storage.DeleteAsync(old.ImageKey); }
                catch (Exception ex) { _logger.LogWarning(ex, "旧图删除失败 key={Key}", old.ImageKey); }
            });
            old.ImageKey = key;
            old.FileSize = stream.Length;
            old.ContentType = contentType;
            old.UploadedAt = DateTime.UtcNow;
            old.UploadedBy = uploadedBy;
            old.IsPrimary = slot == 1;
            old.DisplayOrder = slot;
            await _db.SaveChangesAsync(ct);
            return ToInfo(old, await GetUrlAsync(key));
        }
        else
        {
            var img = new ProductImage
            {
                ProductId = productId,
                Slot = slot,
                ImageKey = key,
                FileSize = stream.Length,
                ContentType = contentType,
                IsPrimary = slot == 1,
                DisplayOrder = slot,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = uploadedBy
            };
            _db.ProductImages.Add(img);
            await _db.SaveChangesAsync(ct);

            // 主图同步到 product.image_key
            if (slot == 1)
            {
                product.ImageKey = key;
                product.ImageStatus = "ready";
                product.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            return ToInfo(img, await GetUrlAsync(key));
        }
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
        var result = new List<ProductImageInfo>();
        foreach (var i in imgs)
        {
            var url = await GetUrlAsync(i.ImageKey);
            result.Add(ToInfo(i, url));
        }
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
        i.UploadedAt, i.UploadedBy
    );
}
