using System.ComponentModel.DataAnnotations.Schema;

namespace SakuraFilter.Core.Entities;

/// <summary>
/// 刷新令牌实体 (JWT 认证体系)
/// 设计:
///   - 存储 token 的 SHA256 哈希 (不存明文, 防止 DB 泄露后 token 可用)
///   - 一次性使用: 用后撤销并记录 ReplacedByTokenId 链
///   - 7 天有效期 (RefreshExpireDays 配置)
///   - 撤销机制: RevokedAt 标记撤销时间
/// WHY 独立表: access token 短期(30min)无状态, refresh token 需服务端状态管理
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }

    /// <summary>所属用户 ID (FK → users.id)</summary>
    [Column("user_id")] public long UserId { get; set; }

    /// <summary>token 的 SHA256 哈希 (UNIQUE, 不存明文)</summary>
    [Column("token_hash")] public string TokenHash { get; set; } = "";

    /// <summary>过期时间 (UTC, 超过此时间不可用)</summary>
    [Column("expires_at")] public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>撤销时间 (UTC, 可空, null 表示有效)</summary>
    [Column("revoked_at")] public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>替换此 token 的新 token ID (用于 token 链路追踪, 可空)</summary>
    [Column("replaced_by_token_id")] public long? ReplacedByTokenId { get; set; }

    /// <summary>创建时间 (默认 now())</summary>
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }

    /// <summary>创建时客户端 IP (用于审计, 可空)</summary>
    [Column("created_ip")] public string? CreatedIp { get; set; }

    /// <summary>导航属性: 所属用户</summary>
    public User? User { get; set; }
}
