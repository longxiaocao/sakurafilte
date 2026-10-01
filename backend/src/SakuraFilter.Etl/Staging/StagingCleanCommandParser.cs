namespace SakuraFilter.Etl.Staging;

public sealed record StagingCleanParseResult(StagingCleanOptions? Options, string? Error);

/// <summary>
/// stage-clean 参数解析，确保只针对一个已完成的暂存批次刷新 clean 层。
/// </summary>
public static class StagingCleanCommandParser
{
    public static StagingCleanParseResult Parse(IReadOnlyList<string> args)
    {
        string? batchIdText = null;
        string? pgConn = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--batch-id": batchIdText = ReadValue(args, ref i); break;
                case "--pg-conn": pgConn = ReadValue(args, ref i); break;
                default: return new(null, $"未知参数: {args[i]}");
            }
        }

        if (!long.TryParse(batchIdText, out var batchId) || batchId <= 0)
            return new(null, "缺少 --batch-id <positive-bigint>");
        if (string.IsNullOrWhiteSpace(pgConn))
            return new(null, "缺少 --pg-conn <connection-string>");

        return new(new StagingCleanOptions(batchId, pgConn), null);
    }

    private static string? ReadValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            return null;
        return args[++index];
    }
}
