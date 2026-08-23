namespace SakuraFilter.Core.DTOs;

/// <summary>
/// V2 Task V17-1.2: 全量重建结果
///   用于 POST /api/admin/etl/reindex-all 端点返回,前端展示重建统计
///   字段对齐 EtlProgress 的 Meili 同步指标
/// </summary>
public record ReindexResult(
    /// <summary>结果消息 (成功/失败摘要)</summary>
    string Message,

    /// <summary>直接成功写入 Meilisearch 的文档数</summary>
    long Direct,

    /// <summary>失败入队待补偿的文档数 (search_index_pending)</summary>
    long Queued,

    /// <summary>总耗时 (毫秒)</summary>
    long ElapsedMs,

    /// <summary>错误信息 (失败时非 null)</summary>
    string? Error = null
);
