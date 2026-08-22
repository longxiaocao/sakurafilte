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

    /// <summary>异步获取预签名 URL(P1.2 新增, 用于前台产品页直接 OSS 读图, 避免后端中转带宽)</summary>
    Task<string> GetPresignedUrlAsync(string key, int expirySeconds = 3600, CancellationToken ct = default);

    /// <summary>
    /// 获取公开直链 URL (2026-08-22 新增, 用于卸载服务器流量)。
    /// 配置了 PublicEndpoint (如 R2 自定义域名 images.xxx.com / OSS CDN) 时返回 {publicEndpoint}/{key} 直链,
    /// 浏览器直连云存储/边缘节点, 服务器不中转图片字节。
    /// 未配置 PublicEndpoint 或 key 非法时返回 null, 调用方应回退 API 代理路径。
    /// </summary>
    string? GetPublicUrl(string key);

    /// <summary>
    /// 读取对象流 (🔧 fix: 图片代理端点用 — 容器内 MinIO 不对外暴露, 预签名 URL host 浏览器不可达 → 裂图;
    /// 代理读流后由 API 返回, 云存储(R2/OSS)切换时同样生效)
    /// </summary>
    Task<(Stream Stream, string ContentType)> GetAsync(string key, CancellationToken ct = default);

    /// <summary>检查文件存在</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// 列出指定前缀下的所有对象 key (V24-F89 v27-2 新增, 供 CleanupOrphanImages CLI 枚举存储桶)
    /// </summary>
    /// <param name="prefix">对象 key 前缀 (如 "products/"), 传空字符串列出全部</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>所有匹配的对象 key 列表 (不含 bucket 名)</returns>
    Task<IReadOnlyList<string>> ListAsync(string prefix = "", CancellationToken ct = default);
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
