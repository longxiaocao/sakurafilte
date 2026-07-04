using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SakuraFilter.Api.Services;
using SakuraFilter.Api.Services.Validators;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// 认证控制器 (JWT 登录/刷新/登出/当前用户/修改密码)
/// 路由前缀: /api/auth
/// 设计:
///   - login/refresh: [AllowAnonymous] (登录前无 token)
///   - logout/me/change-password: [Authorize] (需登录)
///   - 登录失败统一返回 401 (不区分"用户不存在"和"密码错", 防止枚举用户)
///   - refresh 端点: 旧 token 撤销 + 签发新 token (一次性使用, 防重放)
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserService _userService;
    private readonly JwtTokenService _jwt;
    private readonly ILogger<AuthController> _logger;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;

    public AuthController(
        UserService userService,
        JwtTokenService jwt,
        ILogger<AuthController> logger,
        IValidator<LoginRequest> loginValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator)
    {
        _userService = userService;
        _jwt = jwt;
        _logger = logger;
        _loginValidator = loginValidator;
        _changePasswordValidator = changePasswordValidator;
    }

    /// <summary>
    /// 登录
    /// 入参: { Username, Password }
    /// 出参: { accessToken, refreshToken, expiresIn, user: { id, username, role } }
    /// 失败返回 401 (统一错误信息, 不暴露具体原因)
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]  // 安全加固阶段4: 登录防暴力破解 (5 次/分钟/IP)
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        // 安全加固阶段4: FluentValidation 输入校验 (在业务逻辑前执行)
        var validation = await _loginValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                error = "输入参数校验失败",
                details = validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
            });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();
        // 截断 User-Agent 防止超长 (列定义 255)
        if (ua.Length > 255) ua = ua[..255];

        var user = await _userService.AuthenticateAsync(req.Username, req.Password, ip, ua, ct);
        if (user == null)
            return Unauthorized(new { error = "用户名或密码错误, 或账号已被禁用/锁定" });

        var accessToken = _jwt.GenerateAccessToken(user);
        var refresh = await _userService.IssueRefreshTokenAsync(user.Id, ip, ct);

        return Ok(new
        {
            accessToken,
            refreshToken = refresh.TokenHash,  // 实际是原文 (IssueRefreshTokenAsync 内部约定)
            expiresIn = _jwt.ExpireMinutes * 60,
            user = new { id = user.Id, username = user.Username, role = user.Role }
        });
    }

    /// <summary>
    /// 刷新 token
    /// 入参: { refreshToken }
    /// 出参: 新 { accessToken, refreshToken, expiresIn }
    /// 流程: 验证旧 token → 撤销旧 token + 签发新 token (一次性)
    /// 失败返回 401 (refresh token 无效/过期/已撤销)
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest(new { error = "refreshToken 不能为空" });

        var existing = await _userService.ValidateRefreshTokenAsync(req.RefreshToken, ct);
        if (existing == null)
            return Unauthorized(new { error = "refresh token 无效或已过期" });

        // 撤销旧 token + 签发新 token (记录 ReplacedByTokenId 链)
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var newRefresh = await _userService.RevokeAndIssueAsync(existing.Id, existing.UserId, ip, ct);

        // 取用户生成新 access token
        var user = await _userService.GetByIdAsync(existing.UserId, ct);
        if (user == null)
            return Unauthorized(new { error = "用户不存在或已被删除" });

        var accessToken = _jwt.GenerateAccessToken(user);
        return Ok(new
        {
            accessToken,
            refreshToken = newRefresh.TokenHash,
            expiresIn = _jwt.ExpireMinutes * 60
        });
    }

    /// <summary>
    /// 登出 (撤销当前 refresh token)
    /// 需登录: 从 JWT claims 取 userId
    /// 入参: { refreshToken } (客户端传当前持有的 refresh token)
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest(new { error = "refreshToken 不能为空" });

        var existing = await _userService.ValidateRefreshTokenAsync(req.RefreshToken, ct);
        if (existing != null)
        {
            await _userService.RevokeRefreshTokenAsync(existing.Id, ct);
        }
        return Ok(new { success = true });
    }

    /// <summary>
    /// 获取当前登录用户信息
    /// 需登录: 从 JWT claims 取 userId
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var user = await _userService.GetCurrentUserAsync(User, ct);
        if (user == null)
            return Unauthorized(new { error = "用户不存在或已被删除" });

        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            role = user.Role,
            email = user.Email,
            fullName = user.FullName,
            isActive = user.IsActive,
            lastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// 修改密码
    /// 需登录: 从 JWT claims 取 userId
    /// 入参: { oldPassword, newPassword }
    /// 失败返回 400 (旧密码错误)
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        // 安全加固阶段4: FluentValidation 输入校验 (密码强度 + 长度)
        var validation = await _changePasswordValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                error = "输入参数校验失败",
                details = validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
            });
        }

        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!long.TryParse(userIdStr, out var userId))
            return Unauthorized(new { error = "无法识别用户身份" });

        var ok = await _userService.ChangePasswordAsync(userId, req.OldPassword, req.NewPassword, ct);
        if (!ok)
            return BadRequest(new { error = "旧密码错误" });

        return Ok(new { success = true });
    }
}

// ========== 请求 DTO (内嵌, 与 Controller 同文件, 避免散落) ==========

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = "";
}

public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}
