using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Api.Services;

/// <summary>
/// JWT 令牌服务 (单例, 线程安全)
/// 职责:
///   - 生成 access token (短期, 30min, 无状态)
///   - 生成 refresh token (长期, 7d, 随机串, 服务端存哈希)
///   - 哈希 refresh token (SHA256, 存库前哈希, 防止 DB 泄露后 token 可用)
///   - 验证 access token 签名+过期 (中间件层调用)
/// 设计:
///   - SigningKey 从 IConfiguration 读取 (环境变量 Jwt__SigningKey 覆盖)
///   - 启动时校验 SigningKey >= 32 字符 (HS256 安全最低要求)
///   - SigningKey 为空或过短直接抛异常, 启动失败比运行期泄露安全
/// WHY 单例: 无状态服务, IConfiguration 只读, SymmetricSecurityKey 可重用
/// </summary>
public class JwtTokenService
{
    private readonly IConfiguration _config;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expireMinutes;
    private readonly int _refreshExpireDays;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public JwtTokenService(IConfiguration config)
    {
        _config = config;
        _issuer = config["Jwt:Issuer"] ?? "SakuraFilter";
        _audience = config["Jwt:Audience"] ?? "SakuraFilter.Web";
        _expireMinutes = config.GetValue<int?>("Jwt:ExpireMinutes") ?? 30;
        _refreshExpireDays = config.GetValue<int?>("Jwt:RefreshExpireDays") ?? 7;

        // 启动期校验: SigningKey 必须 >= 32 字符 (HS256 安全最低要求)
        //   WHY: 短 key 易被暴力破解, NIST 推荐 HS256 至少 128bit 熵 (32 字符 Base64 ≈ 192bit)
        var signingKey = config["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured (>=32 chars). " +
                "请在 appsettings.json 或环境变量 Jwt__SigningKey 配置至少 32 字符的签名密钥");
        }
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }

    /// <summary>
    /// 生成 access token
    /// claims: sub (用户 ID), role, email, iat, exp
    /// 有效期: ExpireMinutes (默认 30 分钟)
    /// </summary>
    public string GenerateAccessToken(User user, IEnumerable<string>? roles = null)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            // WHY sub 用 user.Id 字符串: JWT sub 标准 claim 是字符串, 中间件层 ClaimTypes.NameIdentifier 读此值
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(ClaimTypes.Name, user.Username),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        // 角色: 优先用传入 roles, 兜底用 user.Role 单值
        var roleList = roles?.ToList();
        if (roleList is { Count: > 0 })
        {
            foreach (var r in roleList)
                claims.Add(new Claim(ClaimTypes.Role, r));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _issuer,
            Audience = _audience,
            NotBefore = now.UtcDateTime,
            Expires = now.UtcDateTime.AddMinutes(_expireMinutes),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        };

        var token = _tokenHandler.CreateToken(descriptor);
        return _tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// 生成 refresh token 原文 (32 字节随机串, Base64 编码)
    /// 仅返回原文, 入库前需调用 HashRefreshToken 哈希
    /// </summary>
    public string GenerateRefreshToken()
    {
        // WHY 32 字节: 256bit 熵, 与 HS256 等价, 暴力枚举不可行
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 计算 refresh token 的 SHA256 哈希 (存库用, 不存原文)
    /// </summary>
    public string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 验证 access token 签名 + 过期 (中间件层 JwtBearer 自动调用, 此方法供手动验证用)
    /// 返回 null 表示验证失败
    /// </summary>
    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _issuer,
                ValidAudience = _audience,
                IssuerSigningKey = _signingKey,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            return _tokenHandler.ValidateToken(token, parameters, out _);
        }
        catch
        {
            // WHY 吞异常返回 null: 验证失败原因多样 (过期/签名错/格式错), 调用方只关心是否有效
            return null;
        }
    }

    /// <summary>Access token 有效期 (分钟), 供 Controller 返回 expiresIn 字段</summary>
    public int ExpireMinutes => _expireMinutes;

    /// <summary>Refresh token 有效期 (天), 供 UserService.IssueRefreshTokenAsync 计算过期</summary>
    public int RefreshExpireDays => _refreshExpireDays;
}
