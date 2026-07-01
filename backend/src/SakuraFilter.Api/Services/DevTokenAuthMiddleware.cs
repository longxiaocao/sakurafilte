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
/// Day 9.9: 双 key 轮换支持 (与 CursorHmac 一致)
///   - CurrentKey + PreviousKey(可选) 两个 token
///   - 轮转步骤: ops 配 PreviousKey = 旧 token + CurrentKey = 新 token → 部署 → 等前端刷新 → 清空 PreviousKey
///   - 用 PreviousKey 验证成功时记录 Warning, 提示运维过渡期尚未结束
/// </summary>
public class DevTokenAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DevTokenAuthMiddleware> _logger;
    private readonly string _expectedToken;
    private readonly string? _previousToken;  // Day 9.9: 双 key 轮换
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
        // Day 9.9: 加载 PreviousKey (可选, 轮换过渡期用)
        _previousToken = config["Auth:DevStaticTokenPrevious"];
        if (string.IsNullOrEmpty(_previousToken)) _previousToken = null;  // 空字符串视作未配置
        if (_enabled && _previousToken is { Length: < 32 })
        {
            throw new InvalidOperationException(
                $"Auth:DevStaticTokenPrevious 长度 {_previousToken.Length} < 32, 不安全");
        }
        if (_previousToken is not null && _previousToken == _expectedToken)
        {
            _logger.LogWarning("DevStaticToken 与 Previous 相同, Previous 忽略");
            _previousToken = null;
        }
        else if (_previousToken is not null)
        {
            _logger.LogWarning("DevTokenAuth 双 key 模式: PreviousKey 已配置, 过渡期内旧 token 仍可用");
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
        // Day 9.9: 双 key — 先匹配 Current, 再匹配 Previous (过渡期)
        var tokenValid = false;
        var usingPrevious = false;
        if (ctx.Request.Headers.TryGetValue("X-Admin-Token", out var provided))
        {
            var tokenStr = provided.ToString();
            if (string.Equals(tokenStr, _expectedToken, StringComparison.Ordinal))
                tokenValid = true;
            else if (_previousToken is not null && string.Equals(tokenStr, _previousToken, StringComparison.Ordinal))
            {
                tokenValid = true;
                usingPrevious = true;
            }
        }
        if (!tokenValid)
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
        if (usingPrevious)
            _logger.LogWarning("PreviousKey 验证成功 path={Path}, 过渡期旧 token 仍在使用", path);

        await _next(ctx);
    }
}
