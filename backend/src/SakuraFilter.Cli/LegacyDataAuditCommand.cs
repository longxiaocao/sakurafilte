using System.Text.Json;
using SakuraFilter.Etl.Staging;

namespace SakuraFilter.Cli;

internal static class LegacyDataAuditCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = LegacyDataAuditCommandParser.Parse(args.Skip(1).ToArray());
        if (parsed.Options is null)
        {
            Console.Error.WriteLine($"[stage-legacy-audit] {parsed.Error}");
            return 1;
        }

        try
        {
            var report = await new LegacyDataAuditService().AuditAsync(parsed.Options);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[stage-legacy-audit] 审计失败: {ex.Message}");
            return 1;
        }
    }
}
