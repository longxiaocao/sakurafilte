namespace SakuraFilter.Etl.Staging;

public sealed record StagingCleanOptions(long BatchId, string PgConnectionString);

public sealed record StagingCleanReport(
    long BatchId,
    string Status,
    long ProductSpecsRows,
    long ProductSpecConflictRows,
    long OemNumbersRows,
    long OemNumbersMergedRows,
    long ApplicationsRows);
