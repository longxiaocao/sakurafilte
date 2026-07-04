using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// RefreshToken 实体 EF Core 配置 (JWT 认证体系)
/// 表名 refresh_tokens, snake_case 列名
/// 设计:
///   - token_hash UNIQUE (按 hash 查表 + ON CONFLICT 目标列)
///   - user_id 索引 (用户所有 refresh token 列表查询)
///   - created_at 默认 now()
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("refresh_tokens");
        e.HasKey(t => t.Id);

        e.Property(t => t.TokenHash).HasMaxLength(255).IsRequired();
        e.Property(t => t.CreatedIp).HasMaxLength(45);

        // 项目铁律: NOT NULL 列配默认值
        e.Property(t => t.CreatedAt).HasDefaultValueSql("now()");

        // WHY token_hash UNIQUE: 按 hash 等值查表 (validate) + ON CONFLICT 目标列
        e.HasIndex(t => t.TokenHash).IsUnique();
        // 用户维度查询 (列出某用户所有 token, 撤销时用)
        e.HasIndex(t => t.UserId);

        // 外键关系: user_id → users.id, 级联删除 (用户删除时 token 一并清理)
        e.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
