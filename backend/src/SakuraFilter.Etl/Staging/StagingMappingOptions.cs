namespace SakuraFilter.Etl.Staging;

public sealed record StagingMappingRefreshOptions(long BatchId, string PgConnectionString);

public sealed record StagingMappingReviewOptions(
    long BatchId,
    string OemNo1,
    string ReviewStatus,
    string? TargetMr1,
    long? TargetProductId,
    string? ReviewReason,
    string ReviewedBy,
    string PgConnectionString);

public sealed record StagingMappingRefreshReport(
    long BatchId,
    string Status,
    long TotalOemCount,
    long CandidateCount,
    long PendingCount,
    long AmbiguousCount);
