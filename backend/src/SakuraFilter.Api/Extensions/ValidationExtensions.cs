using System.Reflection;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// ETL JSONL 路径白名单校验工具。
/// 抽出原 Program.cs 中 ValidateJsonlPath 顶层静态函数。
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// 校验 jsonlPath 是否在 Etl:AllowedImportDirs 白名单内。
    /// </summary>
    /// <param name="configuration">应用配置</param>
    /// <param name="jsonlPath">待校验的 jsonl 路径</param>
    /// <param name="requireJsonlExtension">是否要求 .jsonl 扩展名（dry-run 端点用）</param>
    /// <returns>null 表示通过；非 null 为错误消息</returns>
    public static string? ValidateJsonlPath(this IConfiguration configuration, string jsonlPath, bool requireJsonlExtension = false)
    {
        if (string.IsNullOrWhiteSpace(jsonlPath))
            return "jsonlPath 不能为空";
        if (requireJsonlExtension && !jsonlPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return "jsonlPath 必须是 .jsonl 文件";

        var allowedDirs = configuration.GetSection("Etl:AllowedImportDirs").Get<string[]>()
            ?? Array.Empty<string>();
        if (allowedDirs.Length == 0)
        {
            // dev 兼容: 未配置白名单时不拦截
            return null;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(jsonlPath);
        }
        catch
        {
            return $"jsonlPath 路径非法: {jsonlPath}";
        }

        foreach (var dir in allowedDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string normalizedDir;
            try
            {
                normalizedDir = Path.GetFullPath(dir);
            }
            catch
            {
                continue;
            }
            if (normalized.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase) &&
                (normalized.Length == normalizedDir.Length ||
                 normalized[normalizedDir.Length] == Path.DirectorySeparatorChar ||
                 normalized[normalizedDir.Length] == Path.AltDirectorySeparatorChar))
            {
                return null;
            }
        }
        return "jsonlPath 不在允许目录内";
    }
}

/// <summary>
/// dry-run JSON Schema 校验结果。
/// 显式 record 而非 tuple: 跨 lambda 调用 .MissingFields/.TypeMismatches。
/// </summary>
public record LineSchemaReport(
    int LineNo,
    Dictionary<string, string> Fields,
    List<string> MissingFields,
    List<string> TypeMismatches,
    string? Error);
