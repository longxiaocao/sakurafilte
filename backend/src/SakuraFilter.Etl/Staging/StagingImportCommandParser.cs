namespace SakuraFilter.Etl.Staging;

public sealed record StagingImportParseResult(StagingImportOptions? Options, string? Error);

/// <summary>
/// stage-import 命令参数解析，保持 CLI 层只负责 I/O 和退出码。
/// </summary>
public static class StagingImportCommandParser
{
    public static StagingImportParseResult Parse(IReadOnlyList<string> args)
    {
        string? specs = null, oemNumbers = null, applications = null, pgConn = null;
        var dryRun = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--specs": specs = ReadValue(args, ref i); break;
                case "--oem-numbers": oemNumbers = ReadValue(args, ref i); break;
                case "--applications": applications = ReadValue(args, ref i); break;
                case "--pg-conn": pgConn = ReadValue(args, ref i); break;
                case "--dry-run": dryRun = true; break;
                default: return new(null, $"未知参数: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(specs)) return new(null, "缺少 --specs <path>");
        if (string.IsNullOrWhiteSpace(oemNumbers)) return new(null, "缺少 --oem-numbers <path>");
        if (string.IsNullOrWhiteSpace(applications)) return new(null, "缺少 --applications <path>");
        if (string.IsNullOrWhiteSpace(pgConn)) return new(null, "缺少 --pg-conn <connection-string>");
        return new(new StagingImportOptions(specs, oemNumbers, applications, pgConn, dryRun), null);
    }

    private static string? ReadValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            return null;
        return args[++index];
    }
}
