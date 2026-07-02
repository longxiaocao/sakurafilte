using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictMachine 实体 EF Core 配置 (Day 10+ P2.2 + P2.3)
/// 表名 dict_machine, 多字段字典 (3 字段): machine_brand (主) + machine_model + machine_name
/// 设计:
///   - 主值字段 machine_brand, UNIQUE (machine_brand, machine_model, machine_name) 联合唯一
///   - List/Typeahead 通过 BaseDictService.ExtraSearchProperties 走 OR 匹配 3 字段
///   - P2.3: machine_category 默认 'others' (4 大类: Agriculture/Commercial/Construction/others)
/// </summary>
public class DictMachineConfiguration : IEntityTypeConfiguration<DictMachine>
{
    public void Configure(EntityTypeBuilder<DictMachine> e)
    {
        e.ToTable("dict_machine");
        e.HasKey(p => p.Id);
        e.Property(p => p.MachineBrand).HasMaxLength(200).IsRequired();
        e.Property(p => p.MachineModel).HasMaxLength(200);
        e.Property(p => p.MachineName).HasMaxLength(200);
        // P2.3: machine_category 默认 'others', max 50 (与 type 字典保持一致)
        e.Property(p => p.MachineCategory).HasMaxLength(50).HasDefaultValue("others");
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        // 联合 UNIQUE: 3 字段一起决定唯一性
        e.HasIndex(p => new { p.MachineBrand, p.MachineModel, p.MachineName }).IsUnique();
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_machine_active")
            .HasFilter("deleted_at IS NULL");
        // P2.3: 按 category 查询索引, 支撑 /api/public/machine-brands/aggregated 聚合
        e.HasIndex(p => new { p.DeletedAt, p.MachineCategory })
            .HasDatabaseName("idx_dict_machine_category")
            .HasFilter("deleted_at IS NULL");
    }
}
