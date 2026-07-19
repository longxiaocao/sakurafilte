using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Prometheus;
using SakuraFilter.Api.Services;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// 统一中间件管道配置。
/// 抽出原 Program.cs 中的 UseMiddleware / Use* 调用序列。
///
/// 顺序（与原 Program.cs 保持一致，v30 P0 修复 DevToken 顺序）：
///   0) CorrelationIdMiddleware
///   1) UseForwardedHeaders
///   2) UseExceptionHandler (生产) / UseDeveloperExceptionPage (开发)
///   3) UseHsts + UseHttpsRedirection (仅生产)
///   4) UseCors
///   5) SecurityHeadersMiddleware
///   6) UseRateLimiter
///   7) UseHttpMetrics
///   8) UseAuthentication → DevTokenAuthMiddleware → UseAuthorization
///      WHY 顺序 (v30 P0 修复, commit cebd2ef): DevToken 必须在 UseAuthorization 之前,
///      否则 X-Admin-Token 请求被 RequireAuthorization("Admin") 端点直接 401 短路
///   9) ResponseTimeMiddleware
///  10) UseSwagger / UseSwaggerUI (仅开发)
/// </summary>
public static class MiddlewarePipelineExtensions
{
    public static IApplicationBuilder UseSakuraFilterMiddleware(
        this IApplicationBuilder app,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        // 0) CorrelationId
        app.UseMiddleware<CorrelationIdMiddleware>();

        // 1) ForwardedHeaders
        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        // 2) 异常处理
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler(handler =>
            {
                handler.Run(async ctx =>
                {
                    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
                    var logger = ctx.RequestServices.GetService<ILogger<Program>>();
                    var result = ProblemDetailsFactory.FromException(ctx, ex ?? new Exception("未知异常"), logger);
                    await result.ExecuteAsync(ctx);
                });
            });
        }

        // 3) HSTS + HTTPS 重定向（仅生产）
        if (!env.IsDevelopment())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        // 4) CORS
        app.UseCors("SakuraFilterCors");

        // 5) 安全响应头
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // 6) 限流
        var rateLimitEnabled = configuration.GetSection("RateLimit").Get<RateLimitOptions>()?.Enabled ?? false;
        if (rateLimitEnabled)
        {
            app.UseRateLimiter();
        }

        // 7) Prometheus HTTP 指标
        app.UseHttpMetrics();

        // 8) 认证 / 授权
        // WHY 顺序: UseAuthentication → DevTokenAuthMiddleware → UseAuthorization
        //   - UseAuthentication 先跑: 处理 Authorization: Bearer (JWT), 设置 ctx.User
        //   - DevTokenAuthMiddleware 中间: 若无 Bearer, 用 X-Admin-Token 设置 ClaimsPrincipal (admin role)
        //   - UseAuthorization 最后跑: 基于 ctx.User 评估 .RequireAuthorization("Admin") policy
        //   修复 (v30 P0): 之前 DevTokenAuthMiddleware 在 UseAuthorization 之后, 导致
        //     X-Admin-Token 请求被 RequireAuthorization("Admin") 端点直接 401 短路
        //     (DevTokenAuthMiddleware 永远跑不到). 12 个 contract 测试 + Playwright smoke 受阻
        //   根因: abefd2d (Day7.8) 拆分 Program.cs 时顺序写反, v30 端到端验证暴露
        app.UseAuthentication();
        app.UseMiddleware<DevTokenAuthMiddleware>();
        app.UseAuthorization();

        // 9) 响应时间埋点
        app.UseMiddleware<ResponseTimeMiddleware>();

        // 11) Swagger（仅开发）
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SakuraFilter v1");
                c.DocumentTitle = "SakuraFilter API (Day 8.4)";
                c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
            });
        }

        return app;
    }
}
