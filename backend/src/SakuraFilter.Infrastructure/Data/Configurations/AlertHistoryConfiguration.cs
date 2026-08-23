using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// AlertHistory EF Core 配置 (P2-1)
/// - 与 raw SQL migration 018_create_alert_history.sql 同步
/// - JSONB 列用 .HasColumnType("jsonb") 显式声明
/// - 索引已在 raw SQL 建好, 避免 EF 重复建索引 (EF 用 HasIndex 会与命名冲突)
/// </summary>
public class AlertHistoryConfiguration : IEntityTypeConfiguration<AlertHistory>
{
    public void Configure(EntityTypeBuilder<AlertHistory> e)
    {
        e.ToTable("alert_history");
        e.HasKey(a => a.Id);

        e.Property(a => a.Type).HasMaxLength(64).IsRequired();
        e.Property(a => a.Severity).HasMaxLength(16).IsRequired();
        e.Property(a => a.Title).HasMaxLength(256).IsRequired();
        e.Property(a => a.Channel).HasMaxLength(32).IsRequired();
        e.Property(a => a.Status).HasMaxLength(16).IsRequired();
        e.Property(a => a.Response).HasColumnType("text");
        e.Property(a => a.Error).HasColumnType("text");

        e.Property(a => a.SentAt).HasDefaultValueSql("now()");
    }
}

/// <summary>
/// AlertRule EF Core 配置 (P2-1)
/// </summary>
public class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> e)
    {
        e.ToTable("alert_rules");
        e.HasKey(r => r.Id);

        e.Property(r => r.Type).HasMaxLength(64).IsRequired();
        e.Property(r => r.Severity).HasMaxLength(16).IsRequired();
        e.Property(r => r.Description).HasColumnType("text");
        e.Property(r => r.Enabled).HasDefaultValue(true);
        e.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
        e.Property(r => r.UpdatedAt).HasDefaultValueSql("now()");

        e.HasIndex(r => r.Type).IsUnique();
    }
}

/// <summary>
/// SecurityEvent EF Core 配置 (P2-1)
/// </summary>
public class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> e)
    {
        e.ToTable("security_events");
        e.HasKey(s => s.Id);

        e.Property(s => s.EventType).HasMaxLength(64).IsRequired();
        e.Property(s => s.Username).HasMaxLength(64);
        e.Property(s => s.Ip).HasMaxLength(45);
        e.Property(s => s.UserAgent).HasMaxLength(255);

        e.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
    }
}
