using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 存储配置端点 (运维中心"存储配置"页) — 查看 / 保存 / 连通性测试。
/// 保存后写入 system_settings (key='storage_config'), 重启容器后由 Program.cs 覆盖环境变量生效。
/// 密钥以明文存内部 DB (系统内部配置), 读取时脱敏展示。
/// </summary>
public static class StorageConfigEndpoints
{
    private const string ConfigKey = "storage_config";

    public static void MapStorageConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/storage").WithTags("Storage").RequireAuthorization("Admin");

        // 当前配置 (system_settings 优先, 否则环境变量; 密钥脱敏)
        g.MapGet("/config", async (ProductDbContext db, IConfiguration cfg, CancellationToken ct) =>
        {
            var saved = await GetSavedConfig(db, ct);
            if (saved != null) return Results.Ok(Mask(saved));
            // 无保存记录 → 从环境变量组装
            var dto = new StorageConfigDto
            {
                Provider = cfg["Storage:Provider"] ?? "minio",
                Minio = new StorageEndpointConfig
                {
                    Endpoint = cfg["Minio:Endpoint"],
                    AccessKey = cfg["Minio:AccessKey"],
                    SecretKey = cfg["Minio:SecretKey"],
                    BucketName = cfg["Minio:BucketName"],
                    PublicEndpoint = cfg["Minio:PublicEndpoint"],
                },
                Aliyun = new StorageEndpointConfig
                {
                    Endpoint = cfg["Aliyun:Endpoint"],
                    AccessKeyId = cfg["Aliyun:AccessKeyId"],
                    AccessKeySecret = cfg["Aliyun:AccessKeySecret"],
                    BucketName = cfg["Aliyun:BucketName"],
                    PublicEndpoint = cfg["Aliyun:PublicEndpoint"],
                    CdnEndpoint = cfg["Aliyun:CdnEndpoint"],
                },
                R2 = new StorageEndpointConfig
                {
                    Endpoint = cfg["R2:Endpoint"],
                    AccessKeyId = cfg["R2:AccessKeyId"],
                    AccessKeySecret = cfg["R2:AccessKeySecret"],
                    BucketName = cfg["R2:BucketName"],
                    PublicEndpoint = cfg["R2:PublicEndpoint"],
                },
            };
            return Results.Ok(Mask(dto));
        }).WithName("AdminGetStorageConfig");

        // 保存配置 (重启生效)
        g.MapPut("/config", async (StorageConfigDto body, ProductDbContext db, CancellationToken ct) =>
        {
            var err = Validate(body);
            if (err != null) return Results.BadRequest(new { error = err });
            var json = System.Text.Json.JsonSerializer.Serialize(body);
            var now = DateTime.UtcNow;
            var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == ConfigKey, ct);
            if (row == null)
            {
                db.SystemSettings.Add(new SystemSetting { Key = ConfigKey, Value = json, Description = "storage config (ops center)", UpdatedAt = now });
            }
            else
            {
                row.Value = json; row.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true, message = "已保存, 重启容器后生效 (存储客户端为单例, 无法热切换)" });
        }).WithName("AdminSaveStorageConfig");

        // 连通性测试 — 用提交的参数创建临时客户端, 上传/读取/删除探针
        g.MapPost("/test", async (StorageConfigDto body, CancellationToken ct) =>
        {
            var err = Validate(body);
            if (err != null) return Results.BadRequest(new { error = err });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var key = $"__conn_test_{Guid.NewGuid():N}.txt";
                switch (body.Provider)
                {
                    case "minio":
                        await TestWithMinio(body.Minio!, key, ct);
                        break;
                    case "r2":
                        await TestWithMinio(new StorageEndpointConfig
                        {
                            Endpoint = body.R2!.Endpoint,
                            AccessKey = body.R2.AccessKeyId,
                            SecretKey = body.R2.AccessKeySecret,
                            BucketName = body.R2.BucketName,
                            PublicEndpoint = body.R2.PublicEndpoint,
                        }, key, ct, r2: true);
                        break;
                    case "aliyun-oss":
                        await TestWithAliyun(body.Aliyun!, key, ct);
                        break;
                    default:
                        return Results.BadRequest(new { error = "provider 非法" });
                }
                sw.Stop();
                return Results.Ok(new StorageTestResult { Ok = true, LatencyMs = sw.ElapsedMilliseconds, Message = "连通性测试通过 (上传/读取/删除探针完成)" });
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Results.Ok(new StorageTestResult { Ok = false, LatencyMs = sw.ElapsedMilliseconds, Message = ex.Message });
            }
        }).WithName("AdminTestStorageConfig").DisableAntiforgery();
    }

    private static async Task<StorageConfigDto?> GetSavedConfig(ProductDbContext db, CancellationToken ct)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == ConfigKey, ct);
        if (row == null || string.IsNullOrWhiteSpace(row.Value)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<StorageConfigDto>(row.Value); }
        catch { return null; }
    }

    private static StorageConfigDto Mask(StorageConfigDto dto)
    {
        MaskEndpoint(dto.Minio, x => x.AccessKey, (x, v) => x.AccessKey = v, x => x.SecretKey, (x, v) => x.SecretKey = v);
        MaskEndpoint(dto.Aliyun, x => x.AccessKeyId, (x, v) => x.AccessKeyId = v, x => x.AccessKeySecret, (x, v) => x.AccessKeySecret = v);
        MaskEndpoint(dto.R2, x => x.AccessKeyId, (x, v) => x.AccessKeyId = v, x => x.AccessKeySecret, (x, v) => x.AccessKeySecret = v);
        return dto;
    }

    private static void MaskEndpoint(StorageEndpointConfig? ep,
        Func<StorageEndpointConfig, string?> getKey, Action<StorageEndpointConfig, string?> setKey,
        Func<StorageEndpointConfig, string?> getSecret, Action<StorageEndpointConfig, string?> setSecret)
    {
        if (ep == null) return;
        setKey(ep, MaskSecret(getKey(ep)));
        setSecret(ep, MaskSecret(getSecret(ep)));
    }

    private static string? MaskSecret(string? v)
        => string.IsNullOrEmpty(v) ? null : (v.Length <= 6 ? "******" : $"{v[..2]}****{v[^2..]}");

    private static string? Validate(StorageConfigDto body)
    {
        if (body == null || string.IsNullOrEmpty(body.Provider)) return "provider 不能为空";
        var ep = body.Provider switch
        {
            "minio" => body.Minio,
            "r2" => body.R2,
            "aliyun-oss" => body.Aliyun,
            _ => null,
        };
        if (ep == null) return $"{body.Provider} 的配置不能为空";
        if (string.IsNullOrEmpty(ep.Endpoint)) return "Endpoint 不能为空";
        var key = body.Provider switch
        {
            "minio" => ep.AccessKey,
            _ => ep.AccessKeyId,
        };
        var secret = body.Provider switch
        {
            "minio" => ep.SecretKey,
            _ => ep.AccessKeySecret,
        };
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(secret)) return "AccessKey / SecretKey 不能为空";
        if (string.IsNullOrEmpty(ep.BucketName)) return "BucketName 不能为空";
        return null;
    }

    private static async Task TestWithMinio(StorageEndpointConfig cfg, string key, CancellationToken ct, bool r2 = false)
    {
        // 后台页面允许填写完整 HTTPS 地址，但 MinIO SDK 只接受 host[:port]。
        // 与服务注册路径使用相同归一化，避免测试通过而重启后不可用。
        var endpoint = NormalizeS3Endpoint(cfg.Endpoint!);
        var client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(cfg.AccessKey ?? cfg.AccessKeyId ?? "", cfg.SecretKey ?? cfg.AccessKeySecret ?? "")
            .WithSSL(r2 || cfg.Endpoint!.StartsWith("https"))
            .Build();
        if (r2) client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(cfg.AccessKey ?? cfg.AccessKeyId ?? "", cfg.SecretKey ?? cfg.AccessKeySecret ?? "")
            .WithSSL(true)
            .WithRegion("auto")
            .Build();
        var content = new MemoryStream("sakura-storage-conn-test"u8.ToArray());
        await client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(cfg.BucketName!)
            .WithObject(key)
            .WithStreamData(content)
            .WithObjectSize(content.Length), ct);
        await client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(cfg.BucketName!)
            .WithObject(key)
            .WithCallbackStream(_ => { }), ct);
        await client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(cfg.BucketName!)
            .WithObject(key), ct);
    }

    private static string NormalizeS3Endpoint(string endpoint)
    {
        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return endpoint[(endpoint.IndexOf("://", StringComparison.Ordinal) + 3)..].TrimEnd('/');
        return endpoint.TrimEnd('/');
    }

    private static async Task TestWithAliyun(StorageEndpointConfig cfg, string key, CancellationToken ct)
    {
        var client = new Aliyun.OSS.OssClient(cfg.Endpoint!, cfg.AccessKeyId!, cfg.AccessKeySecret!);
        var content = new MemoryStream("sakura-storage-conn-test"u8.ToArray());
        await Task.Run(() => client.PutObject(cfg.BucketName!, key, content), ct);
        await Task.Run(() => client.GetObject(cfg.BucketName!, key), ct);
        await Task.Run(() => client.DeleteObject(cfg.BucketName!, key), ct);
    }
}
