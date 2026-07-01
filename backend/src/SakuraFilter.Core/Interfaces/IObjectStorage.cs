namespace SakuraFilter.Core.Interfaces;

/// <summary>
/// 对象存储抽象 - MinIO / AliyunOSS / Local 可切换
/// </summary>
public interface IObjectStorage
{
    /// <summary>上传文件,返回 S3 key</summary>
    Task<string> UploadAsync(string key, Stream stream, string contentType, CancellationToken ct = default);

    /// <summary>删除文件</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>获取 URL(可签名)</summary>
    string GetUrl(string key, int expirySeconds = 3600);

    /// <summary>检查文件存在</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// 搜索引擎抽象 - 主 Meili,兜底 PostgreSQL
/// </summary>
public interface ISearchEngine
{
    /// <summary>批量索引文档</summary>
    Task IndexAsync(string indexName, IEnumerable<SearchDocument> docs, CancellationToken ct = default);

    /// <summary>删除文档</summary>
    Task DeleteAsync(string indexName, IEnumerable<string> ids, CancellationToken ct = default);

    /// <summary>搜索</summary>
    Task<SearchEngineResult> SearchAsync(string indexName, string? query, SearchFilter filter, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>通用搜索文档(Meili / PG 兜底共用)</summary>
public record SearchDocument(
    string Id,
    string OemNoDisplay,
    string OemNoNormalized,
    string Type,
    string? Remark,
    decimal? D1Mm,
    decimal? D2Mm,
    decimal? D3Mm,
    decimal? H1Mm,
    decimal? H2Mm,
    decimal? H3Mm,
    string? Media,
    bool IsDiscontinued,
    IEnumerable<string>? CrossRefBrands = null,
    IEnumerable<string>? MachineBrands = null
);

public record SearchFilter(
    string? Type,
    decimal? D1, decimal? D1Tolerance,
    decimal? D2, decimal? D2Tolerance,
    decimal? H1, decimal? H1Tolerance,
    bool IncludeDiscontinued
);

public record SearchEngineResult(long Total, int ElapsedMs, IEnumerable<string> Ids);
