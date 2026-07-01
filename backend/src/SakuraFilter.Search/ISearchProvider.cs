using SakuraFilter.Core.DTOs;

namespace SakuraFilter.Search;

/// <summary>
/// 搜索提供者抽象 (Strategy Pattern)
/// - MeiliSearchProvider: 主,支持 typo 容错 + facet
/// - PostgresSearchProvider: 兜底,ILike + 范围 (无 typo 容错)
/// - ResilientSearchProvider: 包装,Polly 熔断,主失败自动切兜底
/// </summary>
public interface ISearchProvider
{
    /// <summary>提供者名 (用于日志和 health check)</summary>
    string Name { get; }

    /// <summary>健康检查 (用于 Resilient 包装判断主备切换)</summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);

    /// <summary>搜索 (主入口)</summary>
    Task<SearchResult> SearchAsync(SearchRequest req, CancellationToken ct = default);

    /// <summary>批量索引 (ETL 调用,失败抛异常由 caller 决定是否重试)</summary>
    Task IndexAsync(IEnumerable<ProductIndexDoc> docs, CancellationToken ct = default);

    /// <summary>按 ID 删除 (后台编辑产品时同步)</summary>
    Task DeleteAsync(IEnumerable<long> ids, CancellationToken ct = default);
}

/// <summary>
/// 索引文档 (Meili/PG 共用,字段命名遵循 Meili 约定)
/// </summary>
public record ProductIndexDoc(
    long Id,
    string OemNoNormalized,
    string OemNoDisplay,
    string? Remark,
    string Type,
    decimal? D1Mm,
    decimal? D2Mm,
    decimal? H3Mm,
    decimal? H1Mm,
    string? Media,
    bool IsDiscontinued,
    long UpdatedAtUnix
);
