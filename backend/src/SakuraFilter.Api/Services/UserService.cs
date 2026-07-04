using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// 用户服务 (Scoped, 单请求生命周期)
/// 职责:
///   - 登录认证 (BCrypt.Verify, 失败计数 + 锁定)
///   - 用户 CRUD (管理员创建/查询/软删除)
///   - 密码管理 (修改/重置, BCrypt cost=12)
///   - Refresh token 生命周期管理 (签发/验证/撤销)
///   - 启动期默认用户 seed (admin/operator, 密码从环境变量读)
/// 设计:
///   - 所有写操作走 ProductDbContext (与产品域共享 DbContext, 单事务边界)
///   - 登录失败 5 次锁定 15 分钟 (防暴力破解)
///   - 登录审计日志同步写入 (成功/失败均记录)
///   - Refresh token 仅存哈希 (SHA256), 原文仅返回给客户端一次
/// WHY Scoped: 依赖 ProductDbContext (Scoped), 生命周期必须 ≤ DbContext
/// </summary>
public class UserService
{
    private readonly ProductDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly ILogger<UserService> _logger;
    private readonly XssSanitizer _xssSanitizer;

    // 锁定策略常量
    private const int MaxFailedLoginCount = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public UserService(ProductDbContext db, JwtTokenService jwt, ILogger<UserService> logger, XssSanitizer xssSanitizer)
    {
        _db = db;
        _jwt = jwt;
        _logger = logger;
        _xssSanitizer = xssSanitizer;
    }

    /// <summary>
    /// 登录认证
    /// 流程:
    ///   1. 按 username 查用户 (排除软删除)
    ///   2. 检查 IsActive / LockedUntil
    ///   3. BCrypt.Verify 验证密码
    ///   4. 失败: FailedLoginCount++, 达 5 次设 LockedUntil = now + 15min
    ///   5. 成功: 重置 FailedLoginCount=0, 更新 LastLoginAt/LastLoginIp
    ///   6. 写 LoginAuditLog (成功/失败均记录)
    /// 返回 null 表示认证失败 (具体原因见 audit log)
    /// </summary>
    public async Task<User?> AuthenticateAsync(
        string username, string password, string? ip, string? userAgent, CancellationToken ct)
    {
        // 按 username 查未删除用户 (username 有 UNIQUE 索引, 单行返回)
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.DeletedAt == null, ct);

        // 用户不存在: 记录 audit + 返回 null (不区分"用户不存在"和"密码错", 防止枚举用户)
        if (user == null)
        {
            await WriteAuditLogAsync(null, username, ip, userAgent, false, "not_found", ct);
            return null;
        }

        // 账号禁用: 记录 audit + 返回 null
        if (!user.IsActive)
        {
            await WriteAuditLogAsync(user.Id, username, ip, userAgent, false, "inactive", ct);
            return null;
        }

        // 账号锁定中: 记录 audit + 返回 null
        if (user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
        {
            await WriteAuditLogAsync(user.Id, username, ip, userAgent, false, "locked", ct);
            return null;
        }

        // BCrypt.Verify 验证密码 (BCrypt 内部已防时序攻击)
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            // 失败: FailedLoginCount++, 达阈值则锁定
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLoginCount)
            {
                user.LockedUntil = DateTimeOffset.UtcNow.Add(LockDuration);
                _logger.LogWarning("用户 {Username} 连续登录失败 {Count} 次, 已锁定至 {LockedUntil}",
                    username, user.FailedLoginCount, user.LockedUntil);
            }
            await _db.SaveChangesAsync(ct);
            await WriteAuditLogAsync(user.Id, username, ip, userAgent, false, "wrong_password", ct);
            return null;
        }

        // 成功: 重置计数 + 更新最后登录信息
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.LastLoginIp = ip;
        await _db.SaveChangesAsync(ct);
        await WriteAuditLogAsync(user.Id, username, ip, userAgent, true, null, ct);
        return user;
    }

    /// <summary>按 ID 查用户 (排除软删除)</summary>
    public async Task<User?> GetByIdAsync(long id, CancellationToken ct)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null, ct);
    }

    /// <summary>
    /// 分页列表 (软删除过滤)
    /// 返回 (items, total)
    /// </summary>
    public async Task<(List<User> items, int total)> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;  // 防止恶意超大页

        var query = _db.Users.Where(u => u.DeletedAt == null);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>
    /// 创建用户 (管理员调用)
    /// 密码用 BCrypt cost=12 哈希
    /// </summary>
    public async Task<User> CreateAsync(
        string username, string password, string role, string? email, string? fullName, CancellationToken ct)
    {
        // 校验角色值 (防注入非法角色)
        var validRoles = new[] { "admin", "operator", "viewer" };
        if (!validRoles.Contains(role))
            throw new ArgumentException($"非法角色: {role}, 必须为 admin/operator/viewer");

        // 校验用户名唯一 (并发场景下 BCrypt 后再 UniqueIndex 兜底)
        var exists = await _db.Users.AnyAsync(u => u.Username == username, ct);
        if (exists)
            throw new InvalidOperationException($"用户名已存在: {username}");

        // 安全加固阶段4: XSS 消毒 Email/FullName (纯文本字段, 移除所有 HTML 标签)
        //   WHY 不消毒 Username: Validator 限制为 [a-zA-Z0-9_-], 无注入风险
        //   WHY 不消毒 Password: 直接 BCrypt 哈希, HTML 字符不影响安全性
        email = _xssSanitizer.SanitizePlainText(email);
        fullName = _xssSanitizer.SanitizePlainText(fullName);

        // WHY cost=12: BCrypt 默认 cost=10, 12 提供更强抗暴力破解 (单次验证 ~250ms, 可接受)
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

        var user = new User
        {
            Username = username,
            PasswordHash = hash,
            Role = role,
            Email = email,
            FullName = fullName,
            IsActive = true,
            FailedLoginCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("创建用户 {Username} (role={Role})", username, role);
        return user;
    }

    /// <summary>
    /// 修改密码 (用户自己改, 需验证旧密码)
    /// </summary>
    public async Task<bool> ChangePasswordAsync(long userId, string oldPassword, string newPassword, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user == null) return false;

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("用户 {UserId} 修改密码成功", userId);
        return true;
    }

    /// <summary>
    /// 重置密码 (管理员调用, 不需旧密码)
    /// </summary>
    public async Task<bool> ResetPasswordAsync(long userId, string newPassword, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user == null) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("管理员重置用户 {UserId} 密码", userId);
        return true;
    }

    /// <summary>
    /// 软删除用户 (设置 DeletedAt, 不物理删除保留审计数据)
    /// 同时撤销该用户所有有效 refresh token
    /// </summary>
    public async Task<bool> DeactivateAsync(long userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user == null) return false;

        user.DeletedAt = DateTimeOffset.UtcNow;
        user.IsActive = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // 撤销所有有效 refresh token (防止被删用户 token 仍可用)
        var now = DateTimeOffset.UtcNow;
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = now;

        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("软删除用户 {UserId}, 撤销 {Count} 个 refresh token", userId, tokens.Count);
        return true;
    }

    /// <summary>
    /// 签发 refresh token (登录成功后调用)
    /// 流程: 生成原文 → 哈希 → 入库 → 返回原文给客户端
    /// </summary>
    public async Task<RefreshToken> IssueRefreshTokenAsync(long userId, string? ip, CancellationToken ct)
    {
        var rawToken = _jwt.GenerateRefreshToken();
        var token = new RefreshToken
        {
            UserId = userId,
            TokenHash = _jwt.HashRefreshToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshExpireDays),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedIp = ip
        };
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);

        // WHY 把原文塞回 TokenHash 字段返回: Controller 拿到原文返给客户端, 但 DB 存的是哈希
        //   这是为了避免再定义一个 DTO, 复用 RefreshToken 实体, 调用方需知此约定
        token.TokenHash = rawToken;
        return token;
    }

    /// <summary>
    /// 验证 refresh token (refresh 端点调用)
    /// 流程: 哈希原文 → 查表 → 检查未撤销未过期
    /// 返回 token 实体 (含 UserId) 或 null
    /// </summary>
    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string token, CancellationToken ct)
    {
        var hash = _jwt.HashRefreshToken(token);
        var now = DateTimeOffset.UtcNow;
        // token_hash UNIQUE 索引, 单行返回
        return await _db.RefreshTokens.FirstOrDefaultAsync(
            t => t.TokenHash == hash && t.RevokedAt == null && t.ExpiresAt > now, ct);
    }

    /// <summary>
    /// 撤销 refresh token (登出/refresh 后调用)
    /// 同时记录 ReplacedByTokenId 链 (refresh 场景: 旧 token 指向新 token)
    /// </summary>
    public async Task RevokeRefreshTokenAsync(long tokenId, CancellationToken ct)
    {
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == tokenId, ct);
        if (token == null) return;
        if (token.RevokedAt != null) return;  // 已撤销, 幂等返回
        token.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 撤销并替换 refresh token (refresh 端点专用)
    /// 旧 token 撤销 + ReplacedByTokenId 指向新 token, 签发新 token 返回
    /// </summary>
    public async Task<RefreshToken> RevokeAndIssueAsync(long oldTokenId, long userId, string? ip, CancellationToken ct)
    {
        // 撤销旧 token
        var oldToken = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == oldTokenId, ct);
        if (oldToken != null && oldToken.RevokedAt == null)
        {
            oldToken.RevokedAt = DateTimeOffset.UtcNow;
        }

        // 签发新 token
        var newToken = await IssueRefreshTokenAsync(userId, ip, ct);

        // 记录链路 (旧 → 新), 便于审计追踪
        if (oldToken != null)
        {
            oldToken.ReplacedByTokenId = newToken.Id;
            await _db.SaveChangesAsync(ct);
        }
        return newToken;
    }

    /// <summary>
    /// 启动期 seed 默认用户 (admin + operator)
    /// 仅当 Users 表为空时执行 (首次部署)
    /// 密码从环境变量读 (禁止硬编码):
    ///   - INITIAL_ADMIN_PASSWORD
    ///   - INITIAL_OPERATOR_PASSWORD
    /// 任一缺失则跳过该用户并记 Warning (不抛异常, 不阻塞启动)
    /// </summary>
    public async Task SeedDefaultUsersAsync(CancellationToken ct)
    {
        var hasUsers = await _db.Users.AnyAsync(ct);
        if (hasUsers)
        {
            _logger.LogInformation("Users 表已有数据, 跳过默认用户 seed");
            return;
        }

        _logger.LogInformation("Users 表为空, 开始 seed 默认用户 (admin/operator)");

        var adminPwd = Environment.GetEnvironmentVariable("INITIAL_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(adminPwd))
        {
            await CreateAsync("admin", adminPwd, "admin", "admin@sakurafilter.local", "系统管理员", ct);
        }
        else
        {
            _logger.LogWarning("环境变量 INITIAL_ADMIN_PASSWORD 未设置, 跳过 admin 用户 seed (后续可通过 CLI 创建)");
        }

        var operatorPwd = Environment.GetEnvironmentVariable("INITIAL_OPERATOR_PASSWORD");
        if (!string.IsNullOrWhiteSpace(operatorPwd))
        {
            await CreateAsync("operator", operatorPwd, "operator", null, "运营人员", ct);
        }
        else
        {
            _logger.LogWarning("环境变量 INITIAL_OPERATOR_PASSWORD 未设置, 跳过 operator 用户 seed");
        }
    }

    /// <summary>
    /// 获取当前登录用户 (从 ClaimsPrincipal 读取)
    /// 供 Controller [Authorize] 端点使用
    /// </summary>
    public async Task<User?> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? principal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !long.TryParse(userIdStr, out var userId))
            return null;
        return await GetByIdAsync(userId, ct);
    }

    /// <summary>
    /// 写登录审计日志 (内部方法)
    /// </summary>
    private async Task WriteAuditLogAsync(
        long? userId, string username, string? ip, string? userAgent, bool success, string? failureReason, CancellationToken ct)
    {
        _db.LoginAuditLogs.Add(new LoginAuditLog
        {
            UserId = userId,
            Username = username,
            LoginAt = DateTimeOffset.UtcNow,
            Ip = ip,
            UserAgent = userAgent,
            Success = success,
            FailureReason = failureReason
        });
        await _db.SaveChangesAsync(ct);
    }
}
