using System.Text.Json;
using SakuraFilter.Etl.Staging;

namespace SakuraFilter.Cli;

internal static class ApprovedOemPublishCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = ApprovedOemPublishCommandParser.Parse(args.Skip(1).ToArray());
        if (parsed.Options is null)
        {
            Console.Error.WriteLine($"[stage-publish-approved] {parsed.Error}");
            return 1;
        }

        try
        {
            var report = await new ApprovedOemPublishService().PreflightAsync(parsed.Options);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            if (parsed.Options.Apply)
            {
                Console.Error.WriteLine("[stage-publish-approved] 当前版本只提供预检，--apply 尚未开放，未写入任何正式表");
                return 2;
            }

            return report.BlockedMappingCount == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[stage-publish-approved] 预检失败: {ex.Message}");
            return 1;
        }
    }
}
