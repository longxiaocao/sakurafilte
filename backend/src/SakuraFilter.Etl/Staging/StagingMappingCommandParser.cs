namespace SakuraFilter.Etl.Staging;

public sealed record StagingMappingRefreshParseResult(StagingMappingRefreshOptions? Options, string? Error);
public sealed record StagingMappingReviewParseResult(StagingMappingReviewOptions? Options, string? Error);

/// <summary>
/// OEM 到 MR.1 审核映射命令解析，限制 CLI 只写 staging 审核表。
/// </summary>
public static class StagingMappingCommandParser
{
    public static StagingMappingRefreshParseResult ParseRefresh(IReadOnlyList<string> args)
    {
        var values = ParseValues(args, out var error);
        if (error is not null) return new(null, error);
        if (!TryGetPositiveBatchId(values, out var batchId)) return new(null, "缺少 --batch-id <positive-bigint>");
        if (!values.TryGetValue("--pg-conn", out var pgConn) || string.IsNullOrWhiteSpace(pgConn))
            return new(null, "缺少 --pg-conn <connection-string>");

        return new(new StagingMappingRefreshOptions(batchId, pgConn), null);
    }

    public static StagingMappingReviewParseResult ParseReview(IReadOnlyList<string> args)
    {
        var values = ParseValues(args, out var error);
        if (error is not null) return new(null, error);
        if (!TryGetPositiveBatchId(values, out var batchId)) return new(null, "缺少 --batch-id <positive-bigint>");
        if (!values.TryGetValue("--oem-no-1", out var oemNo1) || string.IsNullOrWhiteSpace(oemNo1))
            return new(null, "缺少 --oem-no-1 <OEM NO 1>");
        if (!values.TryGetValue("--status", out var reviewStatus) || reviewStatus is not ("approved" or "rejected"))
            return new(null, "--status 只能是 approved 或 rejected");
        if (!values.TryGetValue("--reviewed-by", out var reviewedBy) || string.IsNullOrWhiteSpace(reviewedBy))
            return new(null, "缺少 --reviewed-by <name>");
        if (!values.TryGetValue("--pg-conn", out var pgConn) || string.IsNullOrWhiteSpace(pgConn))
            return new(null, "缺少 --pg-conn <connection-string>");

        values.TryGetValue("--mr1", out var targetMr1);
        values.TryGetValue("--reason", out var reason);
        values.TryGetValue("--product-id", out var productIdText);
        long? targetProductId = long.TryParse(productIdText, out var parsedProductId) && parsedProductId > 0
            ? parsedProductId
            : null;

        if (reviewStatus == "approved" && string.IsNullOrWhiteSpace(targetMr1))
            return new(null, "批准映射必须提供 --mr1 <MR.1>");
        if (reviewStatus == "rejected" && string.IsNullOrWhiteSpace(reason))
            return new(null, "驳回映射必须提供 --reason <text>");
        if (productIdText is not null && targetProductId is null)
            return new(null, "--product-id 必须是正整数");

        return new(new StagingMappingReviewOptions(
            batchId, oemNo1, reviewStatus, targetMr1, targetProductId, reason, reviewedBy, pgConn), null);
    }

    private static Dictionary<string, string?> ParseValues(IReadOnlyList<string> args, out string? error)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        error = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is not ("--batch-id" or "--oem-no-1" or "--status" or "--mr1" or "--product-id" or "--reason" or "--reviewed-by" or "--pg-conn"))
            {
                error = $"未知参数: {args[i]}";
                return values;
            }

            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"参数 {args[i]} 缺少值";
                return values;
            }

            values[args[i]] = args[++i];
        }

        return values;
    }

    private static bool TryGetPositiveBatchId(IReadOnlyDictionary<string, string?> values, out long batchId)
    {
        batchId = 0;
        return values.TryGetValue("--batch-id", out var text)
            && long.TryParse(text, out batchId)
            && batchId > 0;
    }
}
