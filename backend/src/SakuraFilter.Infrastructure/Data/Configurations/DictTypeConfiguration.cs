using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictType 实体 EF Core 配置 (Day 10+ P2.2)
/// 表名 dict_type, 字段 type (单字段字典, 固定 5 值: oil/fuel/air/cabin/others)
/// 默认值 seed 由 spike-test/_seed_dict_types.py 注入
/// </summary>
public class DictTypeConfiguration : IEntityTypeConfiguration<DictType>
{
    public void Configure(EntityTypeBuilder<DictType> e)
    {
        e.ToTable("dict_type");
        e.HasKey(p => p.Id);
        e.Property(p => p.Type).HasMaxLength(50).IsRequired();
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        e.HasIndex(p => p.Type).IsUnique();
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_type_active")
            .HasFilter("deleted_at IS NULL");
    }
}
