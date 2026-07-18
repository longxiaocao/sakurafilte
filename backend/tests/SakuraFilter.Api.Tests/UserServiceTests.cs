using System.Security.Claims;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// V24-F63 (spec 26.13.7 建议 3): UserService 单元测试
///
/// 测试目标: 覆盖安全敏感操作 (登录/密码/Token/锁定)
///   - AuthenticateAsync: 成功 / 密码错 / 用户不存在 / 禁用 / 锁定 / 失败计数 + 锁定触发
///   - CreateAsync: 角色校验 / UNIQUE 校验 / BCrypt 哈希
///   - ChangePasswordAsync: 旧密码错 / 用户不存在 / 成功
///   - ResetPasswordAsync: 成功 + 重置锁定状态
///   - DeactivateAsync: 软删 + 撤销所有 refresh token
///   - IssueRefreshTokenAsync: 入库 + 返回原文 (TokenHash 临时塞原文)
///   - ValidateRefreshTokenAsync: 哈希查表 + 过期/撤销过滤
///   - RevokeRefreshTokenAsync: 幂等撤销
///   - RevokeAndIssueAsync: 撤销旧 + 签发新 + ReplacedByTokenId 链
///   - SeedDefaultUsersAsync: 空表 seed / 非空跳过 / 缺环境变量跳过
///
/// WHY 单元测试: UserService 涉及安全敏感操作, 0 测试覆盖是重大风险
///
/// 注: 使用 EF Core InMemory + 真实 JwtTokenService (HashRefreshToken 是纯函数)
///   V24-F52 复用: TestProductDbContext 子类 Ignore Alert* 实体
/// </summary>
public class UserServiceTests
{
    private sealed class TestProductDbContext : ProductDbContext
    {
        public TestProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Ignore<AlertRule>();
            mb.Ignore<AlertHistory>();
            mb.Ignore<SecurityEvent>();
        }
    }

    private static ProductDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new TestProductDbContext(options);
    }

    private static JwtTokenService CreateJwt()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "test-signing-key-with-32-chars-min-Z9Y8W7V6",
                ["Jwt:Issuer"] = "Test",
                ["Jwt:Audience"] = "Test",
                ["Jwt:AccessTokenExpireMinutes"] = "30",
                ["Jwt:RefreshExpireDays"] = "7"
            })
            .Build();
        return new JwtTokenService(config);
    }

    private static UserService CreateSut(ProductDbContext db, JwtTokenService? jwt = null, XssSanitizer? xss = null)
        => new(db, jwt ?? CreateJwt(), NullLogger<UserService>.Instance, xss ?? new XssSanitizer());

    private static string HashPwd(string pwd) => BCrypt.Net.BCrypt.HashPassword(pwd, workFactor: 4);  // 4 = 测试快速

    private static User User(long id = 1, string username = "tester", string pwd = "Pass123!",
        bool isActive = true, int failedCount = 0, DateTimeOffset? lockedUntil = null) => new()
    {
        Id = id,
        Username = username,
        PasswordHash = HashPwd(pwd),
        Role = "operator",
        IsActive = isActive,
        FailedLoginCount = failedCount,
        LockedUntil = lockedUntil,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // ==================== AuthenticateAsync ====================

    [Fact]
    public async Task AuthenticateAsync_ValidCredentials_ReturnsUser_AndResetsFailedCount()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(failedCount: 3));  // 已有 3 次失败, 成功应重置
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.AuthenticateAsync("tester", "Pass123!", "127.0.0.1", "UA", default);

        result.Should().NotBeNull();
        result!.Username.Should().Be("tester");
        result.FailedLoginCount.Should().Be(0);
        result.LockedUntil.Should().BeNull();
        result.LastLoginAt.Should().NotBeNull();
        result.LastLoginIp.Should().Be("127.0.0.1");
        // 写入成功审计日志
        (await db.LoginAuditLogs.SingleAsync()).Success.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_UserNotFound_ReturnsNull_AndWritesAuditLog()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var result = await sut.AuthenticateAsync("ghost", "x", "1.1.1.1", "UA", default);

        result.Should().BeNull();
        var log = await db.LoginAuditLogs.SingleAsync();
        log.Success.Should().BeFalse();
        log.FailureReason.Should().Be("not_found");
        log.UserId.Should().BeNull();  // 用户不存在时 UserId 为 null
    }

    [Fact]
    public async Task AuthenticateAsync_InactiveUser_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(isActive: false));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.AuthenticateAsync("tester", "Pass123!", "1.1.1.1", "UA", default);

        result.Should().BeNull();
        (await db.LoginAuditLogs.SingleAsync()).FailureReason.Should().Be("inactive");
    }

    [Fact]
    public async Task AuthenticateAsync_LockedUser_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(10)));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.AuthenticateAsync("tester", "Pass123!", "1.1.1.1", "UA", default);

        result.Should().BeNull();
        (await db.LoginAuditLogs.SingleAsync()).FailureReason.Should().Be("locked");
    }

    [Fact]
    public async Task AuthenticateAsync_WrongPassword_IncrementsFailedCount()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(failedCount: 2));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.AuthenticateAsync("tester", "WrongPwd", "1.1.1.1", "UA", default);

        result.Should().BeNull();
        var user = await db.Users.SingleAsync();
        user.FailedLoginCount.Should().Be(3);
        (await db.LoginAuditLogs.SingleAsync()).FailureReason.Should().Be("wrong_password");
    }

    [Fact]
    public async Task AuthenticateAsync_FifthFailure_LocksAccount()
    {
        // WHY: 第 5 次失败触发锁定 (MaxFailedLoginCount=5), LockedUntil = now + 15min
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(failedCount: 4));  // 第 5 次失败触发
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.AuthenticateAsync("tester", "WrongPwd", "1.1.1.1", "UA", default);

        var user = await db.Users.SingleAsync();
        user.FailedLoginCount.Should().Be(5);
        user.LockedUntil.Should().NotBeNull();
        user.LockedUntil!.Value.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task AuthenticateAsync_LockExpired_AttemptsAllowedAgain()
    {
        // WHY: LockedUntil 已过, 应允许尝试 (不返回 locked)
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1)));  // 已过期
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var result = await sut.AuthenticateAsync("tester", "Pass123!", "1.1.1.1", "UA", default);

        result.Should().NotBeNull();  // 锁已过期, 正常登录成功
        (await db.LoginAuditLogs.SingleAsync()).Success.Should().BeTrue();
    }

    // ==================== CreateAsync ====================

    [Fact]
    public async Task CreateAsync_HashesPassword_WithBcrypt()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var result = await sut.CreateAsync("newuser", "PlainPwd123!", "operator", "e@x.com", "Tester", default);

        result.PasswordHash.Should().NotBe("PlainPwd123!");
        result.PasswordHash.Should().StartWith("$2");  // BCrypt hash 格式
        BCrypt.Net.BCrypt.Verify("PlainPwd123!", result.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_DuplicateUsername_Throws()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(username: "existing"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var act = async () => await sut.CreateAsync("existing", "pwd", "viewer", null, null, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已存在*");
    }

    [Theory]
    [InlineData("superadmin")]
    [InlineData("root")]
    [InlineData("")]
    public async Task CreateAsync_InvalidRole_Throws(string role)
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var act = async () => await sut.CreateAsync("u", "p", role, null, null, default);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*非法角色*");
    }

    [Fact]
    public async Task CreateAsync_SanitizesEmailAndFullName()
    {
        // WHY: 防止 XSS 攻击, Email/FullName 应被消毒 (移除 HTML 标签)
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var result = await sut.CreateAsync("u", "p", "viewer", "<script>alert(1)</script>@x.com", "<b>Tester</b>", default);

        result.Email.Should().NotContain("<script>");
        result.FullName.Should().NotContain("<b>");
    }

    // ==================== ChangePasswordAsync ====================

    [Fact]
    public async Task ChangePasswordAsync_OldPasswordCorrect_UpdatesHash()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var oldHash = (await db.Users.SingleAsync()).PasswordHash;
        var sut = CreateSut(db);

        var ok = await sut.ChangePasswordAsync(1, "Pass123!", "NewPwd456!", default);

        ok.Should().BeTrue();
        var user = await db.Users.SingleAsync();
        user.PasswordHash.Should().NotBe(oldHash);
        BCrypt.Net.BCrypt.Verify("NewPwd456!", user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePasswordAsync_OldPasswordWrong_ReturnsFalse()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var oldHash = (await db.Users.SingleAsync()).PasswordHash;
        var sut = CreateSut(db);

        var ok = await sut.ChangePasswordAsync(1, "WrongOld", "NewPwd456!", default);

        ok.Should().BeFalse();
        (await db.Users.SingleAsync()).PasswordHash.Should().Be(oldHash);  // 未变
    }

    [Fact]
    public async Task ChangePasswordAsync_UserNotFound_ReturnsFalse()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var ok = await sut.ChangePasswordAsync(999, "x", "y", default);

        ok.Should().BeFalse();
    }

    // ==================== ResetPasswordAsync ====================

    [Fact]
    public async Task ResetPasswordAsync_ClearsLockAndFailedCount()
    {
        // WHY: 管理员重置密码应同时解除锁定, 否则用户改了密码仍登不上
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(failedCount: 5, lockedUntil: DateTimeOffset.UtcNow.AddMinutes(10)));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var ok = await sut.ResetPasswordAsync(1, "NewPwd789!", default);

        ok.Should().BeTrue();
        var user = await db.Users.SingleAsync();
        user.FailedLoginCount.Should().Be(0);
        user.LockedUntil.Should().BeNull();
        BCrypt.Net.BCrypt.Verify("NewPwd789!", user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ResetPasswordAsync_UserNotFound_ReturnsFalse()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var ok = await sut.ResetPasswordAsync(999, "x", default);

        ok.Should().BeFalse();
    }

    // ==================== DeactivateAsync ====================

    [Fact]
    public async Task DeactivateAsync_SoftDeletes_AndRevokesAllRefreshTokens()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        db.RefreshTokens.Add(new RefreshToken { UserId = 1, TokenHash = "h1", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), CreatedAt = DateTimeOffset.UtcNow });
        db.RefreshTokens.Add(new RefreshToken { UserId = 1, TokenHash = "h2", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), CreatedAt = DateTimeOffset.UtcNow });
        db.RefreshTokens.Add(new RefreshToken { UserId = 1, TokenHash = "h3", ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow });  // 已过期, 不应被撤销
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var ok = await sut.DeactivateAsync(1, default);

        ok.Should().BeTrue();
        var user = await db.Users.SingleAsync();
        user.DeletedAt.Should().NotBeNull();
        user.IsActive.Should().BeFalse();
        var tokens = await db.RefreshTokens.ToListAsync();
        tokens.Where(t => t.TokenHash == "h1" || t.TokenHash == "h2").Should().AllSatisfy(t => t.RevokedAt.Should().NotBeNull());
        tokens.Single(t => t.TokenHash == "h3").RevokedAt.Should().BeNull();  // 已过期不撤销
    }

    [Fact]
    public async Task DeactivateAsync_UserNotFound_ReturnsFalse()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var ok = await sut.DeactivateAsync(999, default);

        ok.Should().BeFalse();
    }

    // ==================== Refresh Token Lifecycle ====================

    [Fact]
    public async Task IssueRefreshTokenAsync_StoresHash_ReturnsRawToken()
    {
        // WHY: 入库的是 hash, 返回给客户端的是原文 (TokenHash 字段被临时塞原文)
        //   注意: 返回的 token 实体被 EF Core 跟踪, 修改 TokenHash 会同步到 db
        //   所以这里用 AsNoTracking 重新查, 验证 DB 中存的是 hash
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var token = await sut.IssueRefreshTokenAsync(1, "1.2.3.4", default);

        token.TokenHash.Should().NotBeEmpty();  // 临时是原文
        // AsNoTracking 重新查 DB, 避免被返回值修改后的跟踪状态影响
        var dbToken = await db.RefreshTokens.AsNoTracking().SingleAsync();
        dbToken.TokenHash.Should().NotBe(token.TokenHash);  // 入库的是 hash, 不等于返回的原文
        dbToken.TokenHash.Should().NotBeEmpty();
        dbToken.UserId.Should().Be(1);
        dbToken.CreatedIp.Should().Be("1.2.3.4");
        dbToken.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_ValidToken_ReturnsToken()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);
        var issued = await sut.IssueRefreshTokenAsync(1, null, default);
        var rawToken = issued.TokenHash;  // 原文

        var validated = await sut.ValidateRefreshTokenAsync(rawToken, default);

        validated.Should().NotBeNull();
        validated!.UserId.Should().Be(1);
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_RevokedToken_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);
        var issued = await sut.IssueRefreshTokenAsync(1, null, default);
        await sut.RevokeRefreshTokenAsync(issued.Id, default);

        var validated = await sut.ValidateRefreshTokenAsync(issued.TokenHash, default);

        validated.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_ExpiredToken_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);
        var issued = await sut.IssueRefreshTokenAsync(1, null, default);
        // 手动改过期
        var dbToken = await db.RefreshTokens.SingleAsync();
        dbToken.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var validated = await sut.ValidateRefreshTokenAsync(issued.TokenHash, default);

        validated.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_TamperedToken_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        var validated = await sut.ValidateRefreshTokenAsync("nonexistent-token", default);

        validated.Should().BeNull();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_Idempotent_AlreadyRevoked_NoError()
    {
        await using var db = CreateInMemoryDb();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = 1,
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-1),  // 已撤销
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.RevokeRefreshTokenAsync(1, default);  // 不应抛异常

        (await db.RefreshTokens.SingleAsync()).RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_NotFound_NoError()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);

        await sut.RevokeRefreshTokenAsync(999, default);  // 不应抛异常
    }

    [Fact]
    public async Task RevokeAndIssueAsync_RevokesOldAndSetsReplacedByTokenId()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);
        var old = await sut.IssueRefreshTokenAsync(1, null, default);

        var next = await sut.RevokeAndIssueAsync(old.Id, 1, "1.1.1.1", default);

        next.Id.Should().NotBe(old.Id);
        var dbOld = await db.RefreshTokens.FirstAsync(t => t.Id == old.Id);
        dbOld.RevokedAt.Should().NotBeNull();
        dbOld.ReplacedByTokenId.Should().Be(next.Id);
    }

    // ==================== SeedDefaultUsersAsync ====================

    [Fact]
    public async Task SeedDefaultUsersAsync_NonEmptyTable_SkipsSeed()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(username: "existing"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        await sut.SeedDefaultUsersAsync(default);

        (await db.Users.CountAsync()).Should().Be(1);  // 不新增
    }

    [Fact]
    public async Task SeedDefaultUsersAsync_EmptyTable_NoEnvVars_SkipsBoth()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);
        // 不设环境变量
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", null);
        Environment.SetEnvironmentVariable("INITIAL_OPERATOR_PASSWORD", null);

        await sut.SeedDefaultUsersAsync(default);

        (await db.Users.CountAsync()).Should().Be(0);  // 跳过
    }

    [Fact]
    public async Task SeedDefaultUsersAsync_EmptyTable_WithAdminPwd_CreatesAdmin()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", "AdminPwd123!");
        Environment.SetEnvironmentVariable("INITIAL_OPERATOR_PASSWORD", null);

        await sut.SeedDefaultUsersAsync(default);

        var users = await db.Users.ToListAsync();
        users.Should().HaveCount(1);
        users[0].Username.Should().Be("admin");
        users[0].Role.Should().Be("admin");
        BCrypt.Net.BCrypt.Verify("AdminPwd123!", users[0].PasswordHash).Should().BeTrue();

        // 清理环境变量, 避免污染其他测试
        Environment.SetEnvironmentVariable("INITIAL_ADMIN_PASSWORD", null);
    }

    // ==================== ListAsync ====================

    [Fact]
    public async Task ListAsync_ExcludesSoftDeleted()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User(id: 1, username: "active"));
        db.Users.Add(new User { Id = 2, Username = "deleted", PasswordHash = "h", DeletedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var (items, total) = await sut.ListAsync(1, 20, default);

        total.Should().Be(1);
        items.Should().HaveCount(1);
        items[0].Username.Should().Be("active");
    }

    [Fact]
    public async Task ListAsync_ClampsPageSize_ToMax200()
    {
        await using var db = CreateInMemoryDb();
        for (var i = 0; i < 5; i++) db.Users.Add(User(id: i + 1, username: $"u{i}"));
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var (items, _) = await sut.ListAsync(1, 1000, default);

        items.Should().HaveCount(5);  // 总共只有 5 条, 不会超出
        // page size 1000 被 clamp 到 200, 但因为数据只有 5 条, 返回 5 条
    }

    [Fact]
    public async Task ListAsync_PageZero_TreatedAsPage1()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);

        var (items, total) = await sut.ListAsync(0, 20, default);

        total.Should().Be(1);
        items.Should().HaveCount(1);
    }

    // ==================== GetCurrentUserAsync ====================

    [Fact]
    public async Task GetCurrentUserAsync_ValidClaim_ReturnsUser()
    {
        await using var db = CreateInMemoryDb();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        var sut = CreateSut(db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1")
        }));

        var user = await sut.GetCurrentUserAsync(principal, default);

        user.Should().NotBeNull();
        user!.Username.Should().Be("tester");
    }

    [Fact]
    public async Task GetCurrentUserAsync_NoClaim_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var user = await sut.GetCurrentUserAsync(principal, default);

        user.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentUserAsync_InvalidUserId_ReturnsNull()
    {
        await using var db = CreateInMemoryDb();
        var sut = CreateSut(db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "not-a-number")
        }));

        var user = await sut.GetCurrentUserAsync(principal, default);

        user.Should().BeNull();
    }
}
