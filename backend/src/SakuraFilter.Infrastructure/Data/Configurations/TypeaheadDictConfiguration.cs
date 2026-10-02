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
        // 🔧 fix(2026-10-03, P2 技术债): 该表由 SQL 迁移 023_typeahead_dict.sql 建 (EF 迁移从未建它),
        //   标记 ExcludeFromMigrations 使 EF 模型快照与运行时模型一致 —— 否则
        //   `dotnet ef migrations has-pending-model-changes` 恒为 true, 该门禁失去意义。
        //   ExcludeFromMigrations 仅影响迁移的 DDL 生成, 不影响运行时查询。
        e.ToTable("typeahead_dict", t => t.ExcludeFromMigrations());
        e.HasKey(x => new { x.Field, x.Value });
        e.Property(x => x.Field).HasMaxLength(50).IsRequired();
        e.Property(x => x.Value).HasMaxLength(500).IsRequired();
    }
}
