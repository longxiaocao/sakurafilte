using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// XrefOemBrand 实体 EF Core 配置 (Day 10+: P2.1 抽象配置分文件)
/// 用途: 把原 DbContext.OnModelCreating 内的 XrefOemBrand 配置抽出独立文件
///       DbContext 用 ApplyConfigurationsFromAssembly 集中注册
///       后续 Phase 2 P2.2 新增字典 (Product Name 1/2, Type, OEM 3, Media, Machine) 时
///       只加 Entity + Configuration 文件, DbContext 不动
/// </summary>
public class XrefOemBrandConfiguration : IEntityTypeConfiguration<XrefOemBrand>
{
    public void Configure(EntityTypeBuilder<XrefOemBrand> e)
    {
        e.ToTable("xref_oem_brand");
        e.HasKey(p => p.Id);
        e.Property(p => p.Brand).HasMaxLength(100).IsRequired();
        // Day 9.12 教训: NOT NULL 列必须显式 PG 默认值
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        // WHY brand UNIQUE: 后台 typeahead 去重 + 同名 upsert 时按 brand 查重
        e.HasIndex(p => p.Brand).IsUnique();
        // typeahead 高频查询: 按 sort_order 升序 + 排除软删除
        //   用 HasFilter 让 PG 只索引未删除行, 索引体积小, 列表查询 O(log N)
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_xref_oem_brand_active")
            .HasFilter("deleted_at IS NULL");
    }
}
