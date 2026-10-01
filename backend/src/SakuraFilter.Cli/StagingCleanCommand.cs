using System.Text.Json;
using SakuraFilter.Etl.Staging;

namespace SakuraFilter.Cli;

internal static class StagingCleanCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = StagingCleanCommandParser.Parse(args.Skip(1).ToArray());
        if (parsed.Options is null)
        {
            Console.Error.WriteLine($"[stage-clean] {parsed.Error}");
            return 1;
        }

        try
        {
            var report = await new StagingCleanService().RefreshAsync(parsed.Options);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[stage-clean] 清洗失败: {ex.Message}");
            return 1;
        }
    }
}
