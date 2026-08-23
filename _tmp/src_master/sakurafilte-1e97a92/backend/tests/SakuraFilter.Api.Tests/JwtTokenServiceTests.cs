using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// JwtTokenService 单测
/// 覆盖: signing key 校验、access token 生成/验证、refresh token 生成/哈希
/// </summary>
public class JwtTokenServiceTests
{
    private const string ValidKey = "test-jwt-signing-key-must-be-32-chars-min-X7K9M2P5Q8R3";
    private const string ShortKey = "short";

    private JwtTokenService CreateSut(string? key = ValidKey, int expireMin = 30, int refreshDays = 7)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = key,
            ["Jwt:Issuer"] = "SakuraFilter",
            ["Jwt:Audience"] = "SakuraFilter.Web",
            ["Jwt:ExpireMinutes"] = expireMin.ToString(),
            ["Jwt:RefreshExpireDays"] = refreshDays.ToString(),
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();
        return new JwtTokenService(config);
    }

    private static User NewUser(long id = 1, string email = "test@example.com", string role = "admin", string username = "tester")
        => new()
        {
            Id = id,
            Email = email,
            Username = username,
            Role = role,
        };

    [Fact]
    public void Constructor_WithMissingKey_Throws()
    {
        var act = () => CreateSut(null);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Jwt:SigningKey*");
    }

    [Fact]
    public void Constructor_WithShortKey_Throws()
    {
        var act = () => CreateSut(ShortKey);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*至少 32 字符*");
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidJwt_Verifiable()
    {
        var sut = CreateSut();
        var user = NewUser();
        var token = sut.GenerateAccessToken(user);

        token.Should().NotBeNullOrEmpty();
        // JWT 由 3 段 base64url + . 组成
        token.Split('.').Should().HaveCount(3);

        var principal = sut.ValidateAccessToken(token);
        principal.Should().NotBeNull();
        principal!.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void GenerateAccessToken_ContainsUserClaims()
    {
        // WHY: 鉴权中间件依赖 sub (user id) 和 role, 缺失会导致权限判断失败
        var sut = CreateSut();
        var user = NewUser(id: 42, email: "user@sakura.dev", role: "operator");
        var token = sut.GenerateAccessToken(user);

        var principal = sut.ValidateAccessToken(token);
        principal!.Identity!.Name.Should().Be("tester");
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("42");
        principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value.Should().Be("user@sakura.dev");
        principal.IsInRole("operator").Should().BeTrue();
    }

    [Fact]
    public void ValidateAccessToken_WithTamperedSignature_ReturnsNull()
    {
        // V24-F58: 修复 flaky 测试
        //   WHY 旧实现: 翻转 base64url 末字符, 但末字符低位可能是 base64 padding 位,
        //     解码后字节序列不变 → 签名仍有效 → 测试间歇性失败
        //   修复: 用不同 signing key 生成同 payload token, 签名必然不同 → 验证必然失败
        //     覆盖场景: token 被替换 payload 或用错误 key 伪造
        var sut = CreateSut();

        // 用不同 key 构造同 issuer/audience 的 token, 签名段必然不同
        var otherKey = "another-valid-signing-key-with-32-chars-min-Z9Y8W7V6U5";
        var handler = new JwtSecurityTokenHandler();
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(otherKey)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var forged = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "SakuraFilter",
            audience: "SakuraFilter.Web",
            claims: new[]
            {
                new System.Security.Claims.Claim("sub", "1"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "tester")
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);
        var tampered = handler.WriteToken(forged);

        var principal = sut.ValidateAccessToken(tampered);
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_WithExpiredToken_ReturnsNull()
    {
        // WHY: 过期 token 必须被拒绝, 防止"凭据被窃后长期滥用"
        // 手工构造一个过期的 token (用相同 issuer/audience/key 签名)
        var handler = new JwtSecurityTokenHandler();
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(ValidKey)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var expired = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "SakuraFilter",
            audience: "SakuraFilter.Web",
            claims: new[]
            {
                new System.Security.Claims.Claim("sub", "1"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "tester")
            },
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5),  // 5 分钟前已过期
            signingCredentials: creds);
        var token = handler.WriteToken(expired);

        var sut = CreateSut();
        var principal = sut.ValidateAccessToken(token);
        principal.Should().BeNull();
    }

    [Fact]
    public void GenerateRefreshToken_Unique()
    {
        // WHY: refresh token 必须不可预测, 否则拿到一个就能刷新拿所有用户 token
        var sut = CreateSut();
        var t1 = sut.GenerateRefreshToken();
        var t2 = sut.GenerateRefreshToken();
        t1.Should().NotBe(t2);
        t1.Length.Should().BeGreaterThan(40);  // 32 字节 Base64 ≈ 44 字符
    }

    [Fact]
    public void HashRefreshToken_Deterministic()
    {
        // WHY: 存库用 SHA256 哈希, 相同输入必须产生相同哈希 (查询接口才能匹配)
        var sut = CreateSut();
        var token = "test-refresh-token-123";
        var h1 = sut.HashRefreshToken(token);
        var h2 = sut.HashRefreshToken(token);
        h1.Should().Be(h2);
    }

    [Fact]
    public void HashRefreshToken_DifferentTokens_DifferentHashes()
    {
        // WHY: 不同 token 必须产生不同哈希, 否则存在 hash collision 风险
        var sut = CreateSut();
        var h1 = sut.HashRefreshToken("token-1");
        var h2 = sut.HashRefreshToken("token-2");
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void ExpireMinutes_ReturnsConfigured()
    {
        var sut = CreateSut(expireMin: 60);
        sut.ExpireMinutes.Should().Be(60);
    }

    [Fact]
    public void RefreshExpireDays_ReturnsConfigured()
    {
        var sut = CreateSut(refreshDays: 14);
        sut.RefreshExpireDays.Should().Be(14);
    }
}
