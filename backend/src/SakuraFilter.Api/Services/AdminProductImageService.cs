using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 产品图片管理服务 (Day 8.1)
/// 用途: 支撑后台分区 4 录入 (1-6 张图, S3 兼容存储 / MinIO)
/// V2 Task 3.1/3.2: 主图/详情图分层 + 命名字段可配置
///   - 主图 key: products/primary/{namingValue}/{namingValue}-1.{ext} (namingValue 默认 oem_no_3)
///   - 详情图 key: products/detail/{namingValue}/{namingValue}-{slot}.{ext} (namingValue 默认 mr_1)
///   - 命名字段从 system_settings 读取 (image.primary_naming_field / image.detail_naming_field)
///   - IMemoryCache 5 分钟缓存配置, 避免每次上传查 DB
/// 设计:
///   - 上传走 IObjectStorage 抽象 (MinIO / AliyunOSS 可切换)
///   - product_images 表 (product_id, slot) UNIQUE(detail) / (oem_no_3) UNIQUE(primary)
///   - 主图 (slot=1, image_role=primary) 同步写 products.image_key, 兼容旧字段
/// </summary>
public class AdminProductImageService
{
    private readonly ProductDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AdminProductImageService> _logger;

    // V2 Task 3.1.4: system_settings 缓存键 + 过期时间
    private const string CacheKeyPrimary = "image.primary_naming_field";
    private const string CacheKeyDetail = "image.detail_naming_field";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public AdminProductImageService(
        ProductDbContext db,
        IObjectStorage storage,
        IConfiguration config,
        IMemoryCache cache,
        ILogger<AdminProductImageService> logger)
    {
        _db = db;
        _storage = storage;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    // ========== V2 Task 3.1: BuildKeyAsync (读 system_settings + 分层) ==========
    /// <summary>
    /// V2: 根据 image_role + system_settings 配置生成 S3 key
    /// </summary>
    /// <param name="imageRole">"primary" 或 "detail"</param>
    /// <param name="oemNo3">主图关联的 OEM 3 (primary 必填)</param>
    /// <param name="mr1">MR.1 (detail 必填, 也是 primary 的 fallback)</param>
    /// <param name="slot">图片 slot (primary=1, detail=2-6)</param>
    /// <param name="ext">扩展名 (jpg/png/webp)</param>
    public async Task<string> BuildKeyAsync(string imageRole, string? oemNo3, string? mr1, short slot, string ext, CancellationToken ct = default)
    {
        // V2 Task 3.1.2/3.1.3: 根据 image_role 选命名字段 + 目录分层
        string namingValue;
        string roleDir;

        if (imageRole == "primary")
        {
            // 主图: 默认按 oem_no_3 命名, 配置可切换为 mr_1
            var namingField = await GetNamingFieldAsync(CacheKeyPrimary, "oem_no_3", ct);
            namingValue = namingField == "mr_1" ? (mr1 ?? "") : (oemNo3 ?? "");
            roleDir = "primary";
            if (string.IsNullOrEmpty(namingValue))
                throw new InvalidOperationException($"IMAGE_ROLE_SLOT_MISMATCH: 主图命名值 ({namingField}) 不能为空");
        }
        else
        {
            // 详情图: 默认按 mr_1 命名 (MR.1 共享详情图), 配置可切换为 oem_no_3
            var namingField = await GetNamingFieldAsync(CacheKeyDetail, "mr_1", ct);
            namingValue = namingField == "oem_no_3" ? (oemNo3 ?? "") : (mr1 ?? "");
            roleDir = "detail";
            if (string.IsNullOrEmpty(namingValue))
                throw new InvalidOperationException($"IMAGE_ROLE_SLOT_MISMATCH: 详情图命名值 ({namingField}) 不能为空");
        }

        // 清洗 namingValue: 仅允许字母数字-_, 防路径穿越
        //   WHY: namingValue 来自 DB (oem_no_3/mr_1), 但防御性编程防止异常数据导致 S3 路径穿越
        var safe = new string(namingValue.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
        return $"products/{roleDir}/{safe}/{safe}-{slot}.{ext}";
    }

    /// <summary>
    /// V2 Task 3.1.4: 从 system_settings 读取命名字段 (IMemoryCache 5 分钟缓存)
    /// </summary>
    private async Task<string> GetNamingFieldAsync(string cacheKey, string defaultValue, CancellationToken ct)
    {
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached != null)
            return cached;

        var value = await _db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == cacheKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        // 校验值合法性 (仅允许 oem_no_3 / mr_1)
        var result = (value == "oem_no_3" || value == "mr_1") ? value! : defaultValue;
        _cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    // ========== V2 Task 3.2: 分层上传 (主图/详情图) ==========
    /// <summary>
    /// V2: 分层上传 (主图 imageRole=primary slot=1 / 详情图 imageRole=detail slot=2-6)
    /// 与旧 UploadAsync 区别:
    ///   - 签名改为 (mr1, imageRole, oemNo3, slot, stream, contentType) 修复漏洞 5
    ///   - 主图校验 oemNo3 存在性 + 唯一约束 uq_product_images_primary
    ///   - 详情图校验 slot 2-6 + 唯一约束 uq_product_images_detail_slot
    ///   - 路径按 image_role 分层 (products/primary/... vs products/detail/...)
    /// </summary>
    public async Task<ProductImageInfo> UploadAsync(
        string mr1, string imageRole, string? oemNo3, short slot,
        Stream stream, string contentType,
        string? uploadedBy, CancellationToken ct = default)
    {
        // V2 Task 3.2.2: 校验 imageRole / slot 一致性
        if (imageRole == "primary" && slot != 1)
            throw new InvalidOperationException($"IMAGE_ROLE_SLOT_MISMATCH: 主图 slot 必须为 1 (当前 {slot})");
        if (imageRole == "detail" && (slot < 2 || slot > 6))
            throw new InvalidOperationException($"IMAGE_DETAIL_SLOT_INVALID: 详情图 slot 必须在 2-6 之间 (当前 {slot})");
        if (imageRole != "primary" && imageRole != "detail")
            throw new InvalidOperationException($"IMAGE_ROLE_SLOT_MISMATCH: imageRole 必须为 primary 或 detail (当前 {imageRole})");

        // V2 Task 3.2.3: 校验 mr_1 存在性
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Mr1 == mr1, ct)
            ?? throw new KeyNotFoundException($"MR1_NOT_FOUND: MR.1 '{mr1}' 不存在");

        // V2 Task 3.2.4: 主图校验 oemNo3 存在性
        if (imageRole == "primary")
        {
            if (string.IsNullOrWhiteSpace(oemNo3))
                throw new InvalidOperationException("IMAGE_ROLE_SLOT_MISMATCH: 主图必须提供 oemNo3");
            var oem3Exists = await _db.CrossReferences
                .AnyAsync(x => x.OemNo3 == oemNo3 && x.ProductId == product.Id && !x.IsDiscontinued, ct);
            if (!oem3Exists)
                throw new KeyNotFoundException($"OEM3_NOT_FOUND: OEM 3 '{oemNo3}' 不属于 MR.1 '{mr1}' 或已下架");
        }

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
        var key = await BuildKeyAsync(imageRole, oemNo3, mr1, slot, ext, ct);
        var sizeBytes = stream.Length;

        // ===== 1. DB 事务占位 =====
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // V2 Task 3.2.6/3.2.7: 唯一约束软校验 (在 DB 之前, 给出更友好的 errorCode)
        //   WHY 软校验先于 DB: 避免直接撞 PostgresException 23505 兜底为 ERR_DB_CONFLICT
        if (imageRole == "primary")
        {
            var primaryExists = await _db.ProductImages
                .AnyAsync(i => i.ImageRole == "primary" && i.OemNo3 == oemNo3, ct);
            if (primaryExists)
                throw new InvalidOperationException($"IMAGE_PRIMARY_DUPLICATE: OEM 3 '{oemNo3}' 已有主图 (uq_product_images_primary 约束)");
        }
        else
        {
            var detailSlotExists = await _db.ProductImages
                .AnyAsync(i => i.ProductId == product.Id && i.Slot == slot && i.ImageRole == "detail", ct);
            if (detailSlotExists)
                throw new InvalidOperationException($"IMAGE_DETAIL_SLOT_DUPLICATE: MR.1 '{mr1}' slot {slot} 已有详情图 (uq_product_images_detail_slot 约束)");
        }

        // 覆盖上传: 查旧记录 (主图按 oem_no_3, 详情图按 product_id + slot)
        ProductImage? old;
        if (imageRole == "primary")
            old = await _db.ProductImages.FirstOrDefaultAsync(i => i.ImageRole == "primary" && i.OemNo3 == oemNo3, ct);
        else
            old = await _db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == product.Id && i.Slot == slot && i.ImageRole == "detail", ct);

        var oldKeyToDelete = old?.ImageKey;

        ProductImage saved;
        if (old != null)
        {
            old.ImageKey = key;
            old.FileSize = sizeBytes;
            old.ContentType = contentType;
            old.UploadedAt = DateTime.UtcNow;
            old.UploadedBy = uploadedBy;
            old.IsPrimary = imageRole == "primary";
            old.DisplayOrder = slot;
            old.OemNo3 = imageRole == "primary" ? oemNo3 : null;
            old.ImageRole = imageRole;
            saved = old;
        }
        else
        {
            saved = new ProductImage
            {
                ProductId = product.Id,
                Slot = slot,
                ImageKey = key,
                FileSize = sizeBytes,
                ContentType = contentType,
                IsPrimary = imageRole == "primary",
                DisplayOrder = slot,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = uploadedBy,
                OemNo3 = imageRole == "primary" ? oemNo3 : null,
                ImageRole = imageRole
            };
            _db.ProductImages.Add(saved);
        }

        // 主图同步写 products.image_key (兼容旧字段)
        if (imageRole == "primary")
        {
            product.ImageKey = key;
            product.ImageStatus = "pending";
            product.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        // ===== 2. S3 上传 =====
        try
        {
            await _storage.UploadAsync(key, stream, contentType, ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "S3 上传失败, DB 事务已回滚 mr1={Mr1} role={Role} slot={Slot} key={Key}", mr1, imageRole, slot, key);
            throw;
        }

        // ===== 3. DB 提交 =====
        await tx.CommitAsync(ct);

        // ===== 4. 异步删旧文件 =====
        if (!string.IsNullOrEmpty(oldKeyToDelete) && oldKeyToDelete != key)
        {
            var staleKey = oldKeyToDelete;
            _ = Task.Run(async () =>
            {
                try { await _storage.DeleteAsync(staleKey); }
                catch (Exception ex) { _logger.LogWarning(ex, "旧图删除失败 key={Key}", staleKey); }
            });
        }

        _logger.LogInformation("V2 产品图上传成功 mr1={Mr1} role={Role} oemNo3={OemNo3} slot={Slot} key={Key}",
            mr1, imageRole, oemNo3, slot, key);
        return ToInfo(saved, await GetUrlAsync(key));
    }

    // ========== 删除单张图 (V2: 按 mr1 + imageRole + slot) ==========
    public async Task DeleteAsync(string mr1, string imageRole, short slot, CancellationToken ct = default)
    {
        if (imageRole == "primary" && slot != 1)
            throw new InvalidOperationException("IMAGE_ROLE_SLOT_MISMATCH: 主图 slot 必须为 1");
        if (imageRole == "detail" && (slot < 2 || slot > 6))
            throw new InvalidOperationException("IMAGE_DETAIL_SLOT_INVALID: 详情图 slot 必须在 2-6 之间");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Mr1 == mr1, ct)
            ?? throw new KeyNotFoundException($"MR1_NOT_FOUND: MR.1 '{mr1}' 不存在");

        ProductImage? img;
        if (imageRole == "primary")
            img = await _db.ProductImages.FirstOrDefaultAsync(i => i.ImageRole == "primary" && i.ProductId == product.Id, ct)
                ?? throw new KeyNotFoundException($"MR.1 '{mr1}' 无主图");
        else
            img = await _db.ProductImages.FirstOrDefaultAsync(i => i.ProductId == product.Id && i.Slot == slot && i.ImageRole == "detail", ct)
                ?? throw new KeyNotFoundException($"MR.1 '{mr1}' slot {slot} 无详情图");

        _db.ProductImages.Remove(img);
        await _db.SaveChangesAsync(ct);

        // 主图删除, 清 product.image_key
        if (imageRole == "primary")
        {
            product.ImageKey = null;
            product.ImageStatus = "pending";
            product.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // 异步删文件
        var key = img.ImageKey;
        _ = Task.Run(async () =>
        {
            try { await _storage.DeleteAsync(key); }
            catch (Exception ex) { _logger.LogWarning(ex, "图文件删除失败 key={Key}", key); }
        });
        _logger.LogInformation("V2 产品图删除 mr1={Mr1} role={Role} slot={Slot} key={Key}", mr1, imageRole, slot, key);
    }

    // ========== 列出产品图片 (V2: 按 mr1, 区分 primary/detail) ==========
    public async Task<List<ProductImageInfo>> ListAsync(string mr1, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Mr1 == mr1, ct)
            ?? throw new KeyNotFoundException($"MR1_NOT_FOUND: MR.1 '{mr1}' 不存在");

        var imgs = await _db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == product.Id)
            .OrderBy(i => i.ImageRole).ThenBy(i => i.Slot)
            .ToListAsync(ct);
        var urls = await Task.WhenAll(imgs.Select(i => GetUrlAsync(i.ImageKey)));
        var result = new List<ProductImageInfo>(imgs.Count);
        for (int i = 0; i < imgs.Count; i++)
            result.Add(ToInfo(imgs[i], urls[i]));
        return result;
    }

    // ========== 辅助 ==========
    // V2: 保留旧 BuildKey (static) 兼容性, 内部不再使用, 标记 Obsolete
    [Obsolete("V2: 改用 BuildKeyAsync (支持 image_role 分层 + system_settings 配置)")]
    public static string BuildKey(string oemNormalized, short slot, string ext)
        => $"products/{oemNormalized}/{oemNormalized}-{slot}.{ext}";

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
