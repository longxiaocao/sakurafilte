using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Services;
using SakuraFilter.Api.Services.Validators;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Controllers;

/// <summary>
/// 用户管理控制器 (管理员专用)
/// 路由前缀: /api/admin/users
/// 鉴权: [Authorize(Policy="Admin")] 仅 admin 角色可访问
/// 设计:
///   - 用户 CRUD + 软删除
///   - 管理员重置密码 (不需旧密码)
///   - 登录审计日志查询 (分页)
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Admin")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;
    private readonly ProductDbContext _db;
    private readonly ILogger<UsersController> _logger;
    private readonly IValidator<CreateUserRequest> _createValidator;
    private readonly IValidator<UpdateUserRequest> _updateValidator;

    public UsersController(
        UserService userService,
        ProductDbContext db,
        ILogger<UsersController> logger,
        IValidator<CreateUserRequest> createValidator,
        IValidator<UpdateUserRequest> updateValidator)
    {
        _userService = userService;
        _db = db;
        _logger = logger;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <summary>
    /// 用户列表 (分页)
    /// 出参: { items: [...], total, page, pageSize }
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (items, total) = await _userService.ListAsync(page, pageSize, ct);
        return Ok(new
        {
            items = items.Select(u => new
            {
                id = u.Id,
                username = u.Username,
                email = u.Email,
                fullName = u.FullName,
                role = u.Role,
                isActive = u.IsActive,
                failedLoginCount = u.FailedLoginCount,
                lockedUntil = u.LockedUntil,
                lastLoginAt = u.LastLoginAt,
                lastLoginIp = u.LastLoginIp,
                createdAt = u.CreatedAt
            }),
            total,
            page,
            pageSize
        });
    }

    /// <summary>
    /// 创建用户
    /// 入参: { username, password, role, email?, fullName? }
    /// </summary>
    [HttpPost("")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        // 安全加固阶段4: FluentValidation 输入校验 (用户名/密码强度/角色/邮箱格式)
        var validation = await _createValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                error = "输入参数校验失败",
                details = validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
            });
        }

        try
        {
            var user = await _userService.CreateAsync(req.Username, req.Password, req.Role ?? "viewer", req.Email, req.FullName, ct);
            return Created($"/api/admin/users/{user.Id}", new
            {
                id = user.Id,
                username = user.Username,
                role = user.Role,
                email = user.Email,
                fullName = user.FullName
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>用户详情</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var u = await _userService.GetByIdAsync(id, ct);
        if (u == null) return NotFound(new { error = $"用户不存在: {id}" });
        return Ok(new
        {
            id = u.Id,
            username = u.Username,
            email = u.Email,
            fullName = u.FullName,
            role = u.Role,
            isActive = u.IsActive,
            failedLoginCount = u.FailedLoginCount,
            lockedUntil = u.LockedUntil,
            lastLoginAt = u.LastLoginAt,
            lastLoginIp = u.LastLoginIp,
            createdAt = u.CreatedAt,
            updatedAt = u.UpdatedAt
        });
    }

    /// <summary>
    /// 修改用户 (role/email/fullName/isActive)
    /// 入参: { role?, email?, fullName?, isActive? } (仅传需改字段)
    /// </summary>
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        // 安全加固阶段4: FluentValidation 输入校验 (角色/邮箱格式/姓名长度, 允许字段为空表示不更新)
        var validation = await _updateValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                error = "输入参数校验失败",
                details = validation.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
            });
        }

        var user = await _userService.GetByIdAsync(id, ct);
        if (user == null) return NotFound(new { error = $"用户不存在: {id}" });

        if (req.Role != null) user.Role = req.Role;
        if (req.Email != null) user.Email = req.Email;
        if (req.FullName != null) user.FullName = req.FullName;
        if (req.IsActive.HasValue)
        {
            // WHY 重新激活/解锁: 管理员把 IsActive=true 通常表示恢复访问意图,
            //   场景1: 用户被禁用 (IsActive=false → true) → 应重置锁定
            //   场景2: 用户被锁定 (LockedUntil != null, IsActive=true) → 应重置锁定
            //   不重置会导致用户仍被锁定, 与管理员意图不符
            if (req.IsActive.Value && (user.LockedUntil != null || !user.IsActive))
            {
                user.FailedLoginCount = 0;
                user.LockedUntil = null;
            }
            user.IsActive = req.IsActive.Value;
        }
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("管理员修改用户 {UserId}: role={Role} isActive={IsActive}", id, user.Role, user.IsActive);
        return Ok(new { id = user.Id, success = true });
    }

    /// <summary>软删除用户</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var ok = await _userService.DeactivateAsync(id, ct);
        if (!ok) return NotFound(new { error = $"用户不存在: {id}" });
        return Ok(new { id, deleted = true });
    }

    /// <summary>
    /// 管理员重置密码
    /// 入参: { newPassword }
    /// </summary>
    [HttpPost("{id:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { error = "新密码不能为空" });
        if (req.NewPassword.Length < 8)
            return BadRequest(new { error = "密码长度不能少于 8 位" });

        var ok = await _userService.ResetPasswordAsync(id, req.NewPassword, ct);
        if (!ok) return NotFound(new { error = $"用户不存在: {id}" });
        return Ok(new { id, success = true });
    }

    /// <summary>
    /// 登录审计日志 (分页, 按时间倒序)
    /// 出参: { items: [...], total, page, pageSize }
    /// </summary>
    [HttpGet("/api/admin/audit/login")]
    public async Task<IActionResult> LoginAudit(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] long? userId = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var query = _db.LoginAuditLogs.AsNoTracking();
        if (userId.HasValue)
            query = query.Where(l => l.UserId == userId.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.LoginAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                id = l.Id,
                userId = l.UserId,
                username = l.Username,
                loginAt = l.LoginAt,
                ip = l.Ip,
                userAgent = l.UserAgent,
                success = l.Success,
                failureReason = l.FailureReason
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize });
    }
}

// ========== 请求 DTO ==========

public class CreateUserRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
}

public class UpdateUserRequest
{
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public bool? IsActive { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}
