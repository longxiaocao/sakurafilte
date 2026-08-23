using Microsoft.AspNetCore.Mvc;
using SakuraFilter.Core.Interfaces;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 图片代理端点 — 🔧 fix(审查): 容器内 MinIO 预签名 URL host (minio:9000) 浏览器不可达 → 裂图。
/// 统一由 API 读流返回, 云存储 (MinIO / Aliyun OSS / Cloudflare R2) 切换时无需改前端。
/// 安全: key 白名单校验 (仅 [A-Za-z0-9/._-]), 防路径遍历; 公开读 (产品图片)。
/// </summary>
public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/public/images").WithTags("Storage");

        // 🔧 fix(审查): {*key} catch-all — 存储 key 含 / 多段路径 (products/detail/xxx.png), {key} 单段不匹配 → 404
        g.MapGet("/{*key}", async (string key, IObjectStorage storage, HttpContext ctx, CancellationToken ct) =>
        {
            // 🔧 fix(审查): key 白名单 — 存储 key 由系统生成 (如 products/xxx.jpg), 防任意路径读取
            if (string.IsNullOrWhiteSpace(key) || !IsSafeKey(key))
                return Results.BadRequest(new { error = "key 非法" });
            try
            {
                var (stream, contentType) = await storage.GetAsync(key, ct);
                // 浏览器图片缓存 (图片不可变, 长缓存)
                ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                return Results.Stream(stream, contentType ?? "application/octet-stream");
            }
            catch (Exception)
            {
                return Results.NotFound();
            }
        }).WithName("PublicGetImage").WithMetadata(new ResponseCacheAttribute { Duration = 31536000 });
    }

    private static bool IsSafeKey(string key)
    {
        // 仅允许字母数字 / . _ - 与常见图片扩展名
        foreach (var ch in key)
        {
            if (char.IsLetterOrDigit(ch) || ch is '/' or '.' or '_' or '-') continue;
            return false;
        }
        return key.Length is > 0 and <= 512;
    }
}
