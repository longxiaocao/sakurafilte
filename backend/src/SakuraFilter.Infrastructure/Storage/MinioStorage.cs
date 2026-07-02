using Minio;
using Minio.DataModel.Args;
using SakuraFilter.Core.Interfaces;

namespace SakuraFilter.Infrastructure.Storage;

/// <summary>
/// MinIO 自托管 S3 兼容存储 (MVP 阶段默认)
/// </summary>
public class MinioStorage : IObjectStorage
{
    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly string _publicEndpoint;

    public MinioStorage(IMinioClient client, string bucket, string publicEndpoint)
    {
        _client = client;
        _bucket = bucket;
        _publicEndpoint = publicEndpoint.TrimEnd('/');
    }

    public async Task<string> UploadAsync(string key, Stream stream, string contentType, CancellationToken ct = default)
    {
        await EnsureBucket();
        var args = new PutObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType);
        await _client.PutObjectAsync(args, ct);
        return key;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var args = new RemoveObjectArgs().WithBucket(_bucket).WithObject(key);
        await _client.RemoveObjectAsync(args, ct);
    }

    public string GetUrl(string key, int expirySeconds = 3600)
    {
        // 预签名 URL,前端可直接访问
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithExpiry(expirySeconds);
        return _client.PresignedGetObjectAsync(args).GetAwaiter().GetResult();
    }

    public Task<string> GetPresignedUrlAsync(string key, int expirySeconds = 3600, CancellationToken ct = default)
    {
        // P1.2: 异步预签名 URL, MinIO SDK 内部已 async, 此处包一层 Task
        //   与 AliyunOssStorage 行为一致, 业务层无感切换
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(key)
            .WithExpiry(expirySeconds);
        return _client.PresignedGetObjectAsync(args);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var args = new StatObjectArgs().WithBucket(_bucket).WithObject(key);
            await _client.StatObjectAsync(args, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureBucket()
    {
        var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket));
        if (!exists)
        {
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket));
        }
    }
}
