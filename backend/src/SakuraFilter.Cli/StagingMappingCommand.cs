using System.Text.Json;
using SakuraFilter.Etl.Staging;

namespace SakuraFilter.Cli;

internal static class StagingMappingCommand
{
    public static async Task<int> RefreshAsync(string[] args)
    {
        var parsed = StagingMappingCommandParser.ParseRefresh(args.Skip(1).ToArray());
        if (parsed.Options is null)
        {
            Console.Error.WriteLine($"[stage-map-refresh] {parsed.Error}");
            return 1;
        }

        try
        {
            var report = await new StagingMappingService().RefreshCandidatesAsync(parsed.Options);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[stage-map-refresh] 刷新失败: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> ReviewAsync(string[] args)
    {
        var parsed = StagingMappingCommandParser.ParseReview(args.Skip(1).ToArray());
        if (parsed.Options is null)
        {
            Console.Error.WriteLine($"[stage-map-review] {parsed.Error}");
            return 1;
        }

        try
        {
            await new StagingMappingService().ReviewAsync(parsed.Options);
            Console.WriteLine("审核映射已写入 staging.oem_mr1_mapping_reviews");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[stage-map-review] 审核写入失败: {ex.Message}");
            return 1;
        }
    }
}
