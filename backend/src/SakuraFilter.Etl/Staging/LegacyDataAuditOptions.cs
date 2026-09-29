namespace SakuraFilter.Etl.Staging;

public sealed record LegacyDataAuditOptions(string PgConnectionString);

public sealed record LegacyDataAuditReport(
    string DatabaseName,
    string DatabaseSize,
    bool HasProductsTable,
    bool HasCrossReferencesTable,
    bool HasMachineApplicationsTable,
    bool HasProductImagesTable,
    bool HasEtlProgressLogTable,
    long? ProductsRows,
    long? ProductsMissingMr1Rows,
    long? ProductsMissingOemRows,
    long? CrossReferencesRows,
    long? MachineApplicationsRows,
    long? ProductImagesRows,
    long? EtlProgressLogRows);
