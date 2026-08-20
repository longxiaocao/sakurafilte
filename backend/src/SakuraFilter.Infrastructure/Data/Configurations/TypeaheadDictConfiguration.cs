using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// TypeaheadDict 实体 EF Core 配置 (2026-08-20)
/// 表名 typeahead_dict, 全量 distinct 候选值快照 (8 字段)
/// 设计:
///   - 复合主键 (field, value), field 为 8 个 typeahead 字段名之一
///   - GIN trgm 索引在迁移 023 建 (value gin_trgm_ops), 支撑 ILIKE '%q%'
/// </summary>
public class TypeaheadDictConfiguration : IEntityTypeConfiguration<TypeaheadDict>
{
    public void Configure(EntityTypeBuilder<TypeaheadDict> e)
    {
        e.ToTable("typeahead_dict");
        e.HasKey(x => new { x.Field, x.Value });
        e.Property(x => x.Field).HasMaxLength(50).IsRequired();
        e.Property(x => x.Value).HasMaxLength(500).IsRequired();
    }
}
