using System.ComponentModel.DataAnnotations.Schema;

namespace SakuraFilter.Core.Entities;

/// <summary>
/// 登录审计日志实体 (JWT 认证体系)
/// 设计:
///   - 记录所有登录尝试 (成功/失败均留痕)
///   - UserId 可空 (用户名不存在时仍记录, 用于暴力破解检测)
///   - FailureReason 标记失败原因 (locked / wrong_password / inactive / not_found)
///   - 不做软删除, 日志只追加, 定期清理走 EtlLogCleanupService 类似机制
/// WHY 独立表: 与业务表隔离, 高频写入不影响产品查询性能
/// </summary>
public class LoginAuditLog
{
    public long Id { get; set; }

    /// <summary>用户 ID (可空, 用户名不存在时为 null)</summary>
    [Column("user_id")] public long? UserId { get; set; }

    /// <summary>登录使用的用户名 (即使不存在也记录, 用于暴力破解分析)</summary>
    [Column("username")] public string Username { get; set; } = "";

    /// <summary>登录时间 (UTC, 默认 now())</summary>
    [Column("login_at")] public DateTimeOffset LoginAt { get; set; }

    /// <summary>客户端 IP (可空, 用于地理分析/异常检测)</summary>
    [Column("ip")] public string? Ip { get; set; }

    /// <summary>User-Agent (可空, 用于设备指纹)</summary>
    [Column("user_agent")] public string? UserAgent { get; set; }

    /// <summary>是否登录成功</summary>
    [Column("success")] public bool Success { get; set; }

    /// <summary>失败原因 (可空, 成功时为 null; 值: locked / wrong_password / inactive / not_found)</summary>
    [Column("failure_reason")] public string? FailureReason { get; set; }
}
