using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Prometheus;
using SakuraFilter.Api.Services;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// 统一中间件管道配置。
/// 抽出原 Program.cs 中的 UseMiddleware / Use* 调用序列。
///
/// 顺序（与原 Program.cs 保持一致）：
///   0) CorrelationIdMiddleware
///   1) UseForwardedHeaders
///   2) UseExceptionHandler (生产) / UseDeveloperExceptionPage (开发)
///   3) UseHsts + UseHttpsRedirection (仅生产)
///   4) UseCors
///   5) SecurityHeadersMiddleware
///   6) UseRateLimiter
///   7) UseHttpMetrics
///   8) UseAuthentication / UseAuthorization
///   9) DevTokenAuthMiddleware
///  10) ResponseTimeMiddleware
///  11) UseSwagger / UseSwaggerUI (仅开发)
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
        app.UseAuthentication();
        app.UseAuthorization();

        // 9) DevToken
        app.UseMiddleware<DevTokenAuthMiddleware>();

        // 10) 响应时间埋点
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
