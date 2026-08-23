using SakuraFilter.Api.Services;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// Task 1: 机型三级树查询端点
/// 用途: 返回 MachineType(MachineCategory) → MachineBrand → MachineModel 三级树结构
/// 设计:
///   - 路由 GET /api/admin/machine-tree (admin 角色鉴权, spec F11)
///   - 数据源: dict_machines 表按 machine_category → machine_brand → machine_model 分组聚合
///   - 缓存: IMemoryCache 5 分钟 (MachineDictService.GetTreeAsync 内部实现)
///   - 空数据返回 200 + [] (不返回 null, 前端可直接 v-for 渲染)
/// </summary>
public static class AdminMachineTreeEndpoints
{
    public static IEndpointRouteBuilder MapAdminMachineTreeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/machine-tree", async (
            MachineDictService svc, CancellationToken ct) =>
        {
            var tree = await svc.GetTreeAsync(ct);
            return Results.Ok(tree);
        })
        .WithName("AdminGetMachineTree")
        .WithTags("AdminMachineTree")
        .RequireAuthorization("Admin");  // V24-F19: spec F11

        return app;
    }
}
