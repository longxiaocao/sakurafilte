using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictEngine 实体 EF Core 配置 (Day 10+ P2.2)
/// 表名 dict_engine, 多字段字典 (2 字段): engine_brand (主) + engine_type
/// 设计:
///   - 主值字段 engine_brand, UNIQUE (engine_brand, engine_type) 联合唯一
///   - List/Typeahead 通过 BaseDictService.ExtraSearchProperties 走 OR 匹配
/// </summary>
public class DictEngineConfiguration : IEntityTypeConfiguration<DictEngine>
{
    public void Configure(EntityTypeBuilder<DictEngine> e)
    {
        e.ToTable("dict_engine");
        e.HasKey(p => p.Id);
        e.Property(p => p.EngineBrand).HasMaxLength(200).IsRequired();
        e.Property(p => p.EngineType).HasMaxLength(200);
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        // 联合 UNIQUE: (engine_brand, engine_type)
        e.HasIndex(p => new { p.EngineBrand, p.EngineType }).IsUnique();
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_engine_active")
            .HasFilter("deleted_at IS NULL");
    }
}
