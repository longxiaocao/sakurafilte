using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data.Configurations;

/// <summary>
/// DictMachine 实体 EF Core 配置 (Day 10+ P2.2)
/// 表名 dict_machine, 多字段字典 (3 字段): machine_brand (主) + machine_model + machine_name
/// 设计:
///   - 主值字段 machine_brand, UNIQUE (machine_brand, machine_model, machine_name) 联合唯一
///   - List/Typeahead 通过 BaseDictService.ExtraSearchProperties 走 OR 匹配 3 字段
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
        e.Property(p => p.SortOrder).HasDefaultValue(0);
        e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        e.Property(p => p.UpdatedAt).HasDefaultValueSql("now()");
        // 联合 UNIQUE: 3 字段一起决定唯一性
        e.HasIndex(p => new { p.MachineBrand, p.MachineModel, p.MachineName }).IsUnique();
        e.HasIndex(p => new { p.DeletedAt, p.SortOrder })
            .HasDatabaseName("idx_dict_machine_active")
            .HasFilter("deleted_at IS NULL");
    }
}
