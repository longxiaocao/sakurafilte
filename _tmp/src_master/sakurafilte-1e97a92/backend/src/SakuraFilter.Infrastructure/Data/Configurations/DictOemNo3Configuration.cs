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
        // P0改-3: 排序专用部分索引, 优化 ListAsync ORDER BY sort_order, oem_no_3
        //   WHY: 527万行 ORDER BY sort_order 原 1.6s (Bitmap Heap Scan + Sort), 加此索引后 0.16ms (Index Scan)
        //   查询模式: WHERE deleted_at IS NULL ORDER BY sort_order, oem_no_3 LIMIT 200
        e.HasIndex(p => new { p.SortOrder, p.OemNo3 })
            .HasDatabaseName("idx_dict_oem_no3_sort")
            .HasFilter("deleted_at IS NULL");
    }
}
