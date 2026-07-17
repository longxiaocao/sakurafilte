namespace SakuraFilter.Core.Entities;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// V2: 分区 6 占位空表(仅 id + created_at 两列)
/// 用途: 预留分区 6 扩展位,不参与任何业务查询,不展示前端,不进 Meilisearch 索引
/// </summary>
public class Partition6Placeholder
{
    public long Id { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
