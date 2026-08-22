using Microsoft.Extensions.Logging;
using SakuraFilter.Etl;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// typeahead 字典表重建端点 — ETL 导入后调用, 刷新全量 distinct 值快照。
/// WHY: typeahead_dict 是 typeahead 查询的快速路径 (GIN trgm, 万行级);
///      明细表 (cross_references/machine_applications) 数据变化后需重建, 否则候选值缺失。
/// 成本: 全量 distinct 重建 (万行级) 秒级完成, 幂等 (TRUNCATE + INSERT)。
///
/// 2026-08-21 P1 修复: SQL 与重建执行抽到 TypeaheadDictRebuildService (单一来源),
///   ETL 导入成功后自动调用同一逻辑 (见 EtlImportService), 本端点保留手动兜底入口。
/// </summary>
public static class AdminTypeaheadEndpoints
{
    public static void MapAdminTypeaheadEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/typeahead").WithTags("AdminTypeahead")
            .RequireAuthorization("Admin");

        g.MapPost("/rebuild", (
            TypeaheadDictRebuildService rebuild,
            ILogger<TypeaheadDictRebuildService> logger,
            CancellationToken ct) =>
        {
            // 🔧 fix(2026-08-22 Codex 审查): 原 await 同步执行 (3-5 分钟), 客户端断开会经
            //   CancellationToken 中止重建 (实测残留空临时表)。改 fire-and-forget:
            //   - 立即返回"已触发" (管理员无需保持页面连接)
            //   - 后台任务不绑定请求 ct (重建由 SemaphoreSlim 串行, 幂等, 失败记日志)
            _ = Task.Run(async () =>
            {
                try
                {
                    await rebuild.RebuildAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[typeahead-rebuild] 手动触发的 typeahead_dict 重建失败");
                }
            }, CancellationToken.None);
            return Results.Accepted((string?)null, new { ok = true, message = "typeahead_dict 重建已触发 (后台执行, 由 SemaphoreSlim 串行化)" });
        }).WithName("AdminRebuildTypeaheadDict").WithOpenApi();
    }
}
