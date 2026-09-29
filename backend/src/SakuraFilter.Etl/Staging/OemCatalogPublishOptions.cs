namespace SakuraFilter.Etl.Staging;

public sealed record OemCatalogPublishOptions(long BatchId, string PgConnectionString);

public sealed record OemCatalogPublishParseResult(OemCatalogPublishOptions? Options, string? Error);

public sealed record OemCatalogPublishReport(
    long BatchId,
    string Status,
    long OemProductRows,
    long CrossReferenceRows,
    long MachineApplicationRows);
