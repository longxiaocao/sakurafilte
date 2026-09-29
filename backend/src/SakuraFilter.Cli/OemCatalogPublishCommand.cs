using System.Text.Json;
using SakuraFilter.Etl.Staging;

namespace SakuraFilter.Cli;

internal static class OemCatalogPublishCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = OemCatalogPublishCommandParser.Parse(args.Skip(1).ToArray());
        if (parsed.Options is null)
        {
            Console.Error.WriteLine($"[stage-publish-oem-catalog] {parsed.Error}");
            return 1;
        }

        try
        {
            var report = await new OemCatalogPublishService().PublishAsync(parsed.Options);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return report.Status == "completed" ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[stage-publish-oem-catalog] 发布失败: {ex.Message}");
            return 1;
        }
    }
}
