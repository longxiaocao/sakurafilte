using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictProductName2 实体 EF Core 配置 (Day 10+ P2.2)
/// 表名 dict_product_name2, 字段 product_name_2 (单字段字典)
/// </summary>
public class DictProductName2Configuration : IEntityTypeConfiguration<DictProductName2>
{
    public void Configure(EntityTypeBuilder<DictProductName2> e)
    {
        e.ToTable("dict_product_name2");
        e.HasKey(p => p.Id);
        e.Property(p => p.ProductName2).HasMaxLength(200).IsRequired();
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        e.HasIndex(p => p.ProductName2).IsUnique();
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_product_name2_active")
            .HasFilter("deleted_at IS NULL");
    }
}
