using SakuraFilter.Api.Endpoints;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// 统一端点映射扩展。按模块调用各 Endpoints 类的 Map 方法。
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSakuraFilterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCommonEndpoints();
        app.MapProductEndpoints();
        app.MapEtlEndpoints();
        app.MapAdminProductEndpoints();
        app.MapAdminEtlEndpoints();
        app.MapDeadLetterEndpoints();
        app.MapDictionaryEndpoints();
        app.MapPublicTypeaheadEndpoints();
        // P3.2 (Task 10): MVC 控制器路由 (PublicSearchController 等)
        app.MapControllers();
        return app;
    }
}
