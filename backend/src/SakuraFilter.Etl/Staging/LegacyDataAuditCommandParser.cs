namespace SakuraFilter.Etl.Staging;

public sealed record LegacyDataAuditParseResult(LegacyDataAuditOptions? Options, string? Error);

/// <summary>
/// 旧正式数据审计命令解析。审计只接受连接串，不提供任何删除参数。
/// </summary>
public static class LegacyDataAuditCommandParser
{
    public static LegacyDataAuditParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new(null, "缺少 --pg-conn <connection-string>");
        if (args.Count != 2 || args[0] != "--pg-conn") return new(null, $"未知参数: {args[0]}");
        if (string.IsNullOrWhiteSpace(args[1])) return new(null, "缺少 --pg-conn <connection-string>");

        return new(new LegacyDataAuditOptions(args[1]), null);
    }
}
