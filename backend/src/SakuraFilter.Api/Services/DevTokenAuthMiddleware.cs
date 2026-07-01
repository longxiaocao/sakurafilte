using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Day 8.4: 简易鉴权中间件
/// 用途: 保护 /api/admin/* 和 /api/etl/* 端点, 防止未授权访问
/// 设计:
///   - dev 环境: 静态 token 验证 (Header X-Admin-Token: <token>)
///   - 白名单路径 (/api/search, /scalar 等) 直接放行
///   - 错误统一返回 RFC 7807 ProblemDetails
///   - 401 含 WWW-Authenticate 头, 符合 HTTP 规范
///   WHY 不直接 JWT: MVP 阶段前端无登录, 单 token 足矣; 生产换 JWT 时只改这一个类
/// </summary>
public class DevTokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DevTokenAuthMiddleware> _logger;
    private readonly string _expectedToken;
    private readonly bool _enabled;
    private readonly string[] _adminPrefixes;
    private readonly string[] _exemptExactPaths;

    public DevTokenAuthMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<DevTokenAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        _enabled = config.GetValue<bool?>("Auth:Enabled") ?? true;
        _expectedToken = config["Auth:DevStaticToken"]
            ?? throw new InvalidOperationException(
                "Auth:DevStaticToken 未配置, 启动失败. 必须在 appsettings.json 配置 ≥ 32 字符 token");
        if (_enabled && _expectedToken.Length < 32)
        {
            throw new InvalidOperationException(
                $"Auth:DevStaticToken 长度 {_expectedToken.Length} < 32, 不安全");
        }
        _adminPrefixes = config.GetSection("Auth:AdminPaths").Get<string[]>()
            ?? new[] { "/api/admin", "/api/etl" };
        _exemptExactPaths = config.GetSection("Auth:ExemptPaths").Get<string[]>()
            ?? Array.Empty<string>();
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!_enabled)
        {
            await _next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "/";

        // 白名单精确路径
        foreach (var exempt in _exemptExactPaths)
        {
            if (path.Equals(exempt, StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }
        }

        // 检查是否在受保护前缀
        bool isProtected = false;
        foreach (var prefix in _adminPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                isProtected = true;
                break;
            }
        }
        if (!isProtected)
        {
            await _next(ctx);
            return;
        }

        // 验证 token (Header X-Admin-Token: <token>)
        if (!ctx.Request.Headers.TryGetValue("X-Admin-Token", out var provided)
            || !string.Equals(provided.ToString(), _expectedToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("鉴权失败 path={Path} ip={Ip} ua={UA}",
                path, ctx.Connection.RemoteIpAddress, ctx.Request.Headers.UserAgent.ToString());
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers["WWW-Authenticate"] = "X-Admin-Token";
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsync(
                "{\"type\":\"https://tools.ietf.org/html/rfc7235#section-3.1\"," +
                "\"title\":\"Unauthorized\"," +
                "\"status\":401," +
                "\"detail\":\"缺少或非法的 X-Admin-Token header\"," +
                "\"instance\":\"" + path + "\"}");
            return;
        }

        await _next(ctx);
    }
}
