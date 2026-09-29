namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 已审核 OEM 映射发布命令解析器。默认 dry-run，避免遗漏开关导致正式写入。
/// </summary>
public static class ApprovedOemPublishCommandParser
{
    public static ApprovedOemPublishParseResult Parse(IReadOnlyList<string> args)
    {
        long batchId = 0;
        string? pgConn = null;
        var apply = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--apply":
                    apply = true;
                    break;
                case "--batch-id":
                    if (!TryReadValue(args, ref i, out var batchIdText))
                        return new(null, "参数 --batch-id 缺少值");
                    if (!long.TryParse(batchIdText, out batchId) || batchId <= 0)
                        return new(null, "缺少 --batch-id <positive-bigint>");
                    break;
                case "--pg-conn":
                    if (!TryReadValue(args, ref i, out pgConn))
                        return new(null, "参数 --pg-conn 缺少值");
                    break;
                default:
                    return new(null, $"未知参数: {args[i]}");
            }
        }

        if (batchId <= 0) return new(null, "缺少 --batch-id <positive-bigint>");
        if (string.IsNullOrWhiteSpace(pgConn)) return new(null, "缺少 --pg-conn <connection-string>");
        return new(new ApprovedOemPublishOptions(batchId, pgConn, apply), null);
    }

    private static bool TryReadValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal)) return false;
        value = args[++index];
        return true;
    }
}
