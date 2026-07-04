using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// User 实体 EF Core 配置 (JWT 认证体系)
/// 表名 users, snake_case 列名 (与项目铁律一致)
/// 设计:
///   - username UNIQUE 索引 (登录主键查询 + ON CONFLICT 目标列)
///   - 软删除索引 (deleted_at IS NULL 过滤, 列表查询高效)
///   - 所有 NOT NULL 列配 HasDefaultValue/HasDefaultValueSql (项目铁律)
///   - role 默认 viewer, is_active 默认 true, failed_login_count 默认 0
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.ToTable("users");
        e.HasKey(u => u.Id);

        e.Property(u => u.Username).HasMaxLength(64).IsRequired();
        e.Property(u => u.Email).HasMaxLength(128);
        e.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
        e.Property(u => u.FullName).HasMaxLength(64);
        e.Property(u => u.Role).HasMaxLength(16).IsRequired();
        e.Property(u => u.LastLoginIp).HasMaxLength(45);

        // 项目铁律: NOT NULL 列必须显式 PG 默认值 (与 XrefOemBrandConfiguration 一致)
        e.Property(u => u.IsActive).HasDefaultValue(true);
        e.Property(u => u.FailedLoginCount).HasDefaultValue(0);
        e.Property(u => u.Role).HasDefaultValue("viewer");
        e.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        e.Property(u => u.UpdatedAt).HasDefaultValueSql("now()");

        // WHY username UNIQUE: 登录按 username 等值查询 + ON CONFLICT (username) 目标列 (项目铁律)
        e.HasIndex(u => u.Username).IsUnique();

        // 软删除索引: 列表查询 WHERE deleted_at IS NULL 走 partial index
        e.HasIndex(u => u.DeletedAt)
            .HasDatabaseName("idx_users_deleted_at")
            .HasFilter("deleted_at IS NULL");
    }
}
