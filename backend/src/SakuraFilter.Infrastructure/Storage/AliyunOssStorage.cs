using Aliyun.OSS;
using SakuraFilter.Core.Interfaces;

namespace SakuraFilter.Infrastructure.Storage;

/// <summary>
/// 阿里云 OSS 对象存储 (生产环境 CDN 走 OSS)
/// 用途: P1.2 CDN 切换, 把图片从自建 MinIO 迁到阿里云 OSS + CDN 域名
/// 设计:
///   - SDK: Aliyun.OSS.SDK.NetCore 2.14.0 (官方维护, .NET 8 兼容)
///   - Endpoint 分两种:
///     * 内网 endpoint (oss-cn-hangzhou-internal.aliyuncs.com): 后端上传下载用, 同区免流量费
///     * 外网/公共 endpoint (oss-cn-hangzhou.aliyuncs.com): PublicEndpoint 字段, 浏览器/SDK 直连读图
///   - Bucket 名: 阿里云全局唯一, 需用户自定义 (见 docs/cdn-switch.md)
///   - 预签名 URL: GeneratePresignedUri 生成临时 URL, 默认 1h 过期, 前台产品页直接读图无需中转
///   - 风格: 与 MinioStorage 完全对称, 上层 (AdminProductImageService) 无感知切换
/// </summary>
public class AliyunOssStorage : IObjectStorage
{
    private readonly OssClient _client;
    private readonly string _bucket;
    private readonly string _publicEndpoint;
    private readonly string? _cdnEndpoint;

    public AliyunOssStorage(
        OssClient client,
        string bucketName,
        string publicEndpoint,
        string? cdnEndpoint = null)
    {
        _client = client;
        _bucket = bucketName;
        _publicEndpoint = publicEndpoint.TrimEnd('/');
        _cdnEndpoint = string.IsNullOrWhiteSpace(cdnEndpoint) ? null : cdnEndpoint.TrimEnd('/');
    }

    public async Task<string> UploadAsync(string key, Stream stream, string contentType, CancellationToken ct = default)
    {
        // SDK 2.x 同步 API 已是 I/O 异步实现, 但不返回 Task — 包一层 Task.Run 让调用方走 await
        // WHY: 保持与 IObjectStorage 接口签名一致 (async Task)
        await EnsureBucketAsync(ct);
        // Aliyun OSS SDK PutObject(bucket, key, stream) 内部走异步 I/O, 这里直接调用即可
        // 注: 取消令牌传不到 SDK 内部, 大文件上传场景靠 ClientConfiguration.ConnectionTimeout 控制
        _ = await Task.Run(() => _client.PutObject(_bucket, key, stream, BuildMetadata(contentType)), ct);
        return key;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await Task.Run(() => _client.DeleteObject(_bucket, key), ct);
    }

    public string GetUrl(string key, int expirySeconds = 3600)
    {
        // 同步预签名 URL, 与 MinioStorage 行为一致
        // 优先返回 CDN 域名 (如配 cdn.sakurafilter.com), 否则走 OSS 公共 endpoint
        var expiry = DateTime.UtcNow.AddSeconds(expirySeconds);
        var uri = _client.GeneratePresignedUri(_bucket, key, expiry);
        // 若用户配了 CDN 域名, 把 OSS 域名替换成 CDN 域名 (签名仍由 OSS 颁发, CDN 回源到 OSS 校验)
        if (!string.IsNullOrEmpty(_cdnEndpoint) && uri.Host != null)
        {
            var ub = new UriBuilder(uri) { Host = new Uri(_cdnEndpoint).Host, Scheme = new Uri(_cdnEndpoint).Scheme };
            return ub.Uri.ToString();
        }
        return uri.ToString();
    }

    public async Task<string> GetPresignedUrlAsync(string key, int expirySeconds = 3600, CancellationToken ct = default)
    {
        // 异步预签名 URL, 用于前台产品页直接读图 (避免后端中转带宽)
        return await Task.Run(() => GetUrl(key, expirySeconds), ct);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() => _client.DoesObjectExist(_bucket, key), ct);
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        // 阿里云 Bucket 需用户先在控制台创建 (全局唯一), 运行时不存在则抛错
        // 注: SDK 没有 MakeBucket 公开方法 (CreateBucket 在 IOss 接口), 略保守不自动建
        //   业务约定: bucket 由 ops 提前建好, 这里只校验存在性
        var exists = await Task.Run(() => _client.DoesBucketExist(_bucket), ct);
        if (!exists)
        {
            throw new InvalidOperationException(
                $"阿里云 OSS Bucket '{_bucket}' 不存在, 请先在阿里云控制台创建 (注: bucket 名全局唯一)");
        }
    }

    private static ObjectMetadata BuildMetadata(string contentType)
    {
        // 设置 Content-Type, 否则浏览器收到 application/octet-stream 会触发下载
        return new ObjectMetadata
        {
            ContentType = contentType
        };
    }
}
