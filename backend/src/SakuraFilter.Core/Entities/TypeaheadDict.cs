namespace SakuraFilter.Core.Entities;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// typeahead 全量 distinct 字典表 (8 字段候选值快照)
/// WHY: 8 字段 typeahead 直接在 1550 万行明细表 ILIKE, machine-brand/engine-brand 命中 45 万行 → 2-4s;
///      字典表存全量 distinct 值 + GIN trgm 索引, 查询只扫字典 (万行级) → 毫秒级。
/// 填充: 迁移 023 初始填充; ETL 导入后需重建刷新 (见 admin typeahead rebuild 端点)。
/// 复合主键 (field, value) 在 ProductDbContext.OnModelCreating 配置 (Core 层不引 EF)。
/// </summary>
public class TypeaheadDict
{
    [Column("field")] public string Field { get; set; } = "";
    [Column("value")] public string Value { get; set; } = "";
}
