namespace SakuraFilter.Etl.Staging;

public sealed record StagingImportOptions(
    string SpecsPath,
    string OemNumbersPath,
    string ApplicationsPath,
    string PgConnectionString,
    bool DryRun = false);

public sealed record StagingSourceReport(
    string SourceKind,
    long SourceRows,
    long StagedRows,
    long DuplicateRows,
    long BlankOemRows,
    long IssueRows);

public sealed record StagingImportReport(
    long BatchId,
    IReadOnlyList<StagingSourceReport> Sources,
    long TotalIssues,
    bool DryRun);
