namespace SakuraFilter.Core.DTOs;

/// <summary>
/// 存储配置 (运维中心"存储配置"页) — 保存于 system_settings.storage_config (key='storage_config'),
/// 重启时由 Program.cs 读取并覆盖环境变量。支持 MinIO / Aliyun OSS / Cloudflare R2 三端切换。
/// </summary>
public class StorageConfigDto
{
    /// <summary>minio / aliyun-oss / r2</summary>
    public string? Provider { get; set; }
    public StorageEndpointConfig? Minio { get; set; }
    public StorageEndpointConfig? Aliyun { get; set; }
    public StorageEndpointConfig? R2 { get; set; }
}

public class StorageEndpointConfig
{
    public string? Endpoint { get; set; }
    public string? AccessKey { get; set; }        // MinIO (minio)
    public string? SecretKey { get; set; }        // MinIO (minio)
    public string? AccessKeyId { get; set; }      // aliyun-oss / r2
    public string? AccessKeySecret { get; set; }  // aliyun-oss / r2
    public string? BucketName { get; set; }
    public string? PublicEndpoint { get; set; }
    public string? CdnEndpoint { get; set; }      // aliyun-oss (CDN 加速域名)
}

/// <summary>连通性测试结果</summary>
public class StorageTestResult
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public long? LatencyMs { get; set; }
}
