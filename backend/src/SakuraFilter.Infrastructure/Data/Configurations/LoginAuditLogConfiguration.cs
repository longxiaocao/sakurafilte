using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// LoginAuditLog 实体 EF Core 配置 (JWT 认证体系)
/// 表名 login_audit_logs, snake_case 列名
/// 设计:
///   - 高频写入, 不建过多索引 (仅 login_at 时间索引, 用于按时间分页)
///   - failure_reason 可空 (成功时为 null)
///   - login_at 默认 now()
/// </summary>
public class LoginAuditLogConfiguration : IEntityTypeConfiguration<LoginAuditLog>
{
    public void Configure(EntityTypeBuilder<LoginAuditLog> e)
    {
        e.ToTable("login_audit_logs");
        e.HasKey(l => l.Id);

        e.Property(l => l.Username).HasMaxLength(64).IsRequired();
        e.Property(l => l.Ip).HasMaxLength(45);
        e.Property(l => l.UserAgent).HasMaxLength(255);
        e.Property(l => l.FailureReason).HasMaxLength(64);

        // 项目铁律: NOT NULL 列配默认值
        e.Property(l => l.LoginAt).HasDefaultValueSql("now()");

        // 按时间倒序分页查询审计日志 (后台审计页面)
        e.HasIndex(l => l.LoginAt);
        // 按用户维度查询 (查某用户登录历史)
        e.HasIndex(l => new { l.UserId, l.LoginAt })
            .HasDatabaseName("idx_login_audit_user_login_at");
    }
}
