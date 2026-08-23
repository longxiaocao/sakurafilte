using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictMedia 实体 EF Core 配置 (Day 10+ P2.2)
/// 表名 dict_media, 多字段字典: media_name (主) + media_model (次)
/// 设计:
///   - 主值字段 media_name, UNIQUE (media_name, media_model) 联合唯一
///   - List/Typeahead 通过 BaseDictService.ExtraSearchProperties 走 OR 匹配
/// </summary>
public class DictMediaConfiguration : IEntityTypeConfiguration<DictMedia>
{
    public void Configure(EntityTypeBuilder<DictMedia> e)
    {
        e.ToTable("dict_media");
        e.HasKey(p => p.Id);
        e.Property(p => p.MediaName).HasMaxLength(100).IsRequired();
        e.Property(p => p.MediaModel).HasMaxLength(100);
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        // 联合 UNIQUE: (media_name, media_model), 允许 media_name 重复但 model 区分
        e.HasIndex(p => new { p.MediaName, p.MediaModel }).IsUnique();
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_media_active")
            .HasFilter("deleted_at IS NULL");
    }
}
