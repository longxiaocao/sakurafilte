using Microsoft.AspNetCore.Mvc;
using SakuraFilter.Api.Services;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// 公开搜索页 8 字段 typeahead 端点 (无需 token, 走 "search" 限流分区)
/// WHY: 公开搜索页 8 个输入框, 用户手动输入百万级数据中的 OEM/机型/发动机字段非常困难,
///      提供输入 2 字符后展示 distinct 候选项下拉
/// 路由: GET /api/public/typeahead/{field}?q=xxx&amp;limit=20
///   field ∈ oem-brand | oem-no2 | oem-no3 | machine-brand | machine-model | model-name | engine-brand | engine-type
/// 返回: { count, items: ["MANN", "BOSCH", ...] }
/// 安全: q 长度 &lt; 2 返回空 (避免全表扫描), limit 上限 50, 走 EscapeLikePattern 防注入
/// </summary>
public static class PublicTypeaheadEndpoints
{
    // 8 个合法字段名 (与前端 fields 列表一一对应)
    private static readonly HashSet<string> ValidFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "oem-brand", "oem-no2", "oem-no3",
        "machine-brand", "machine-model", "model-name",
        "engine-brand", "engine-type"
    };

    public static IEndpointRouteBuilder MapPublicTypeaheadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/typeahead")
            .WithTags("PublicTypeahead")
            .RequireRateLimiting("search");

        group.MapGet("/{field}", async (
            [FromRoute] string field,
            [FromQuery] string? q,
            [FromQuery] int? limit,
            PublicTypeaheadService svc,
            CancellationToken ct) =>
        {
            // 字段名校验 (大小写不敏感, 内部统一小写)
            var normalized = field.ToLowerInvariant();
            if (!ValidFields.Contains(normalized))
            {
                return Results.BadRequest(new
                {
                    title = "Invalid field",
                    detail = $"field must be one of: {string.Join(", ", ValidFields)}",
                    status = 400
                });
            }

            var items = await svc.TypeaheadAsync(normalized, q, limit ?? 20, ct);
            return Results.Ok(new { count = items.Count, items });
        })
        .WithSummary("公开搜索 8 字段 typeahead 候选项")
        .WithDescription("输入 2 字符起返回 distinct 候选 (最多 20 条), 走 ILIKE 模糊匹配")
        .WithName("PublicTypeahead")
        .WithOpenApi();

        return app;
    }
}
