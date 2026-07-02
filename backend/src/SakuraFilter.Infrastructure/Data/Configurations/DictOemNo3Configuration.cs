using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictOemNo3 实体 EF Core 配置 (Day 10+ P2.2)
/// 表名 dict_oem_no3, 字段 oem_no_3 (单字段字典, 来源 cross_references.oem_no_3)
/// </summary>
public class DictOemNo3Configuration : IEntityTypeConfiguration<DictOemNo3>
{
    public void Configure(EntityTypeBuilder<DictOemNo3> e)
    {
        e.ToTable("dict_oem_no3");
        e.HasKey(p => p.Id);
        e.Property(p => p.OemNo3).HasMaxLength(200).IsRequired();
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        e.HasIndex(p => p.OemNo3).IsUnique();
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_oem_no3_active")
            .HasFilter("deleted_at IS NULL");
    }
}
