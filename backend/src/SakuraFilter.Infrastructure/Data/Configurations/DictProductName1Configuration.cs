using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictProductName1 实体 EF Core 配置 (Day 10+ P2.2)
/// 表名 dict_product_name1, 字段 product_name_1 (单字段字典)
/// 用途: 复用 P2.1 XrefOemBrandConfiguration 模式, 集中分文件配置
/// </summary>
public class DictProductName1Configuration : IEntityTypeConfiguration<DictProductName1>
{
    public void Configure(EntityTypeBuilder<DictProductName1> e)
    {
        e.ToTable("dict_product_name1");
        e.HasKey(p => p.Id);
        e.Property(p => p.ProductName1).HasMaxLength(200).IsRequired();
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        // UNIQUE (含软删: 同名占用即抛)
        e.HasIndex(p => p.ProductName1).IsUnique();
        // typeahead 索引: (deleted_at, sort_order), PG 部分索引只包含未删除行
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_product_name1_active")
            .HasFilter("deleted_at IS NULL");
    }
}
