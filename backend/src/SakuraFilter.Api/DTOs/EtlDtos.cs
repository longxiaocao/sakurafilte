namespace SakuraFilter.Api.DTOs;

// Day 11 改进 1: 统一 ETL 端点入口, EntityType 可选参数
//   - 不传或传 products: 走 /api/etl/import (默认, 兼容旧调用)
//   - 传 xrefs/apps: 路由到对应 Import*Async
//   - 旧端点 /import-xrefs /import-apps 保留 (向后兼容)
public record ImportRequest(string JsonlPath, string? Mode, string? EntityType, bool? Cascade);

// Day 8.2: 批量对比请求体
// Day 9.4: ETL 取消请求体, 携带取消原因写到 etl_progress_log
public record CancelRequest(string? Reason, string? ReasonCode);

public record CompareRequest(List<long> Ids);

// Day 7.5: 死信查询参数 (运维可见性)
// Day 7.10: 增加 recovery_count / last_recovery_at / last_recovery_error 字段
// Day 7.10.1: 增加 status / recovered_at / recovered_to_pending_id 字段
public record DeadLetterItem(long Id, long OriginalId, string Operation, int RetryCount,
    string? LastError, DateTime CreatedAt, DateTime MovedAt, string PayloadPreview,
    int RecoveryCount, DateTime? LastRecoveryAt, string? LastRecoveryError,
    string Status, DateTime? RecoveredAt, long? RecoveredToPendingId);
