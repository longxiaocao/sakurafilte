namespace SakuraFilter.Etl.Staging;

/// <summary>
/// OEM 目录发布命令参数解析。命令没有 apply 开关，调用即只发布独立 catalog 层。
/// </summary>
public static class OemCatalogPublishCommandParser
{
    public static OemCatalogPublishParseResult Parse(IReadOnlyList<string> args)
    {
        long batchId = 0;
        string? pgConn = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--batch-id":
                    if (!TryReadValue(args, ref i, out var batchIdText)) return new(null, "参数 --batch-id 缺少值");
                    if (!long.TryParse(batchIdText, out batchId) || batchId <= 0)
                        return new(null, "缺少 --batch-id <positive-bigint>");
                    break;
                case "--pg-conn":
                    if (!TryReadValue(args, ref i, out pgConn)) return new(null, "参数 --pg-conn 缺少值");
                    break;
                default:
                    return new(null, $"未知参数: {args[i]}");
            }
        }

        if (batchId <= 0) return new(null, "缺少 --batch-id <positive-bigint>");
        if (string.IsNullOrWhiteSpace(pgConn)) return new(null, "缺少 --pg-conn <connection-string>");
        return new(new OemCatalogPublishOptions(batchId, pgConn), null);
    }

    private static bool TryReadValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return false;
        value = args[++index];
        return true;
    }
}
