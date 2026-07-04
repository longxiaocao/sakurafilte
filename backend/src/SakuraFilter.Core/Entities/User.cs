using System.ComponentModel.DataAnnotations.Schema;

namespace SakuraFilter.Core.Entities;

/// <summary>
/// 用户实体 (JWT 认证体系)
/// 设计:
///   - 支持账号密码登录 (BCrypt 哈希)
///   - 角色: admin / operator / viewer
///   - 失败 5 次锁定 15 分钟
///   - 软删除 (DeletedAt)
///   - 审计字段: LastLoginAt / LastLoginIp / FailedLoginCount / LockedUntil
/// WHY 独立文件: 与 Product 实体解耦, 用户域与产品域分离
/// </summary>
public class User
{
    public long Id { get; set; }

    /// <summary>登录用户名 (UNIQUE, snake_case 列名 username)</summary>
    [Column("username")] public string Username { get; set; } = "";

    /// <summary>邮箱 (可空, 用于通知/找回)</summary>
    [Column("email")] public string? Email { get; set; }

    /// <summary>BCrypt 哈希后的密码 (cost=12)</summary>
    [Column("password_hash")] public string PasswordHash { get; set; } = "";

    /// <summary>显示名 (可空)</summary>
    [Column("full_name")] public string? FullName { get; set; }

    /// <summary>角色: admin / operator / viewer (默认 viewer)</summary>
    [Column("role")] public string Role { get; set; } = "viewer";

    /// <summary>是否启用 (默认 true, 禁用后无法登录)</summary>
    [Column("is_active")] public bool IsActive { get; set; } = true;

    /// <summary>连续登录失败次数 (默认 0, 达 5 次触发锁定)</summary>
    [Column("failed_login_count")] public int FailedLoginCount { get; set; }

    /// <summary>锁定截止时间 (可空, null 表示未锁定)</summary>
    [Column("locked_until")] public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>最后登录时间 (UTC, 可空)</summary>
    [Column("last_login_at")] public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>最后登录 IP (可空, 用于审计)</summary>
    [Column("last_login_ip")] public string? LastLoginIp { get; set; }

    /// <summary>创建时间 (默认 now())</summary>
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }

    /// <summary>更新时间 (默认 now())</summary>
    [Column("updated_at")] public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>软删除时间 (可空, null 表示未删除)</summary>
    [Column("deleted_at")] public DateTimeOffset? DeletedAt { get; set; }
}
