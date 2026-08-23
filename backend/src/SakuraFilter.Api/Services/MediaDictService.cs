using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// DictMedia 字典服务 (Day 10+ P2.2)
/// 用途: 多字段字典 (2 字段: media_name + media_model)
/// 设计:
///   - 主值字段 MediaName, List/Typeahead 走 OR 匹配 MediaName + MediaModel
///   - GetValue() 返回 "MediaName|MediaModel" 拼接, 让主字段保持 brand
///     实际 ListAsync/TypeaheadAsync 用 BuildSearchPredicate 走多字段 ILIKE OR 匹配, 不依赖 GetValue
///   - 业务逻辑仅 override xrefCount (来自 products.media)
/// </summary>
public class MediaDictService : BaseDictService<DictMedia>
{
    public MediaDictService(ProductDbContext db, ILogger<MediaDictService> logger)
        : base(db, logger, tableName: "dict_media", maxLength: 100) { }

    protected override string ValueProperty => "MediaName";
    protected override string SortOrderProperty => "SortOrder";
    protected override string DeletedAtProperty => "DeletedAt";
    protected override DbSet<DictMedia> Set(ProductDbContext ctx) => ctx.DictMedias;

    // P2.2 多字段扩展: List/Typeahead 走 MediaName + MediaModel OR 匹配
    protected override IReadOnlyList<string> ExtraSearchProperties => new[] { "MediaModel" };

    protected override string GetValue(DictMedia item) => item.MediaName;
    protected override void SetValue(DictMedia item, string value) => item.MediaName = value;
    protected override int GetSortOrder(DictMedia item) => item.SortOrder;
    protected override void SetSortOrder(DictMedia item, int sortOrder) => item.SortOrder = sortOrder;
    protected override DateTime? GetDeletedAt(DictMedia item) => item.DeletedAt;
    protected override void SetDeletedAt(DictMedia item, DateTime? deletedAt) => item.DeletedAt = deletedAt;
    protected override long GetId(DictMedia item) => item.Id;

    // 业务: xrefCount 实时聚合 products.media (按 name 计数, 不区分 model)
    public override async Task<long> GetXrefCountAsync(string value, CancellationToken ct = default)
        => await _db.Products.AsNoTracking()
            .CountAsync(p => p.Media == value, ct);

    // ========== DTO 包装 (与单字段字典风格一致) ==========
    public async Task<List<MediaItem>> ListMediasAsync(
        string? q, bool includeDeleted, int? limit, CancellationToken ct = default)
    {
        var rows = await ListAsync(q, includeDeleted, limit, ct);
        return rows.Select(b => new MediaItem(
            b.Id, b.MediaName, b.MediaModel, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0)).ToList();
    }

    public async Task<List<MediaTypeaheadItem>> TypeaheadMediasAsync(
        string? q, int? limit, CancellationToken ct = default)
    {
        var rows = await TypeaheadAsync(q, limit ?? 20, ct);
        return rows.Select(b => new MediaTypeaheadItem(b.Id, b.MediaName, b.MediaModel)).ToList();
    }

    public async Task<MediaItem> CreateMediaAsync(
        string mediaName, string? mediaModel, int? sortOrder, CancellationToken ct = default)
    {
        // 用 brand 作为 ValueProperty 主值; model 单独作为附加字段
        var b = await CreateAsync(mediaName, sortOrder, ct);
        b.MediaModel = string.IsNullOrWhiteSpace(mediaModel) ? null : mediaModel.Trim();
        await _db.SaveChangesAsync(ct);
        return new MediaItem(b.Id, b.MediaName, b.MediaModel, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, 0);
    }

    public async Task<MediaItem> UpdateMediaAsync(
        long id, string? mediaName, string? mediaModel, int? sortOrder, CancellationToken ct = default)
    {
        var b = await UpdateAsync(id, mediaName, sortOrder, ct);
        if (mediaModel != null)
            b.MediaModel = string.IsNullOrWhiteSpace(mediaModel) ? null : mediaModel.Trim();
        await _db.SaveChangesAsync(ct);
        var cnt = await GetXrefCountAsync(b.MediaName, ct);
        return new MediaItem(b.Id, b.MediaName, b.MediaModel, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task DeleteMediaAsync(long id, CancellationToken ct = default) => DeleteAsync(id, ct);

    public async Task<MediaItem> RestoreMediaAsync(long id, CancellationToken ct = default)
    {
        var b = await RestoreAsync(id, ct);
        var cnt = await GetXrefCountAsync(b.MediaName, ct);
        return new MediaItem(b.Id, b.MediaName, b.MediaModel, b.SortOrder,
            b.CreatedAt, b.UpdatedAt, b.DeletedAt, cnt);
    }

    public Task ReorderMediasAsync(List<MediaReorderItem> items, CancellationToken ct = default)
        => ReorderAsync(items.Select(i => (i.Id, i.SortOrder)).ToList(), ct);
}

public record MediaItem(
    long Id, string MediaName, string? MediaModel, int SortOrder,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt, long XrefCount);
public record MediaTypeaheadItem(long Id, string MediaName, string? MediaModel);
public record MediaReorderItem(long Id, int SortOrder);
public record MediaReorderRequest(List<MediaReorderItem> Items);
public record MediaCreateRequest(string MediaName, string? MediaModel, int? SortOrder);
public record MediaUpdateRequest(string? MediaName, string? MediaModel, int? SortOrder);
