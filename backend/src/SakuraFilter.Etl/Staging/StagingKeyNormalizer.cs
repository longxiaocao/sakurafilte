using System.Security.Cryptography;
using System.Text;

namespace SakuraFilter.Etl.Staging;

/// <summary>
/// 暂存导入的 OEM 锚点和整行哈希规范化。
/// </summary>
public static class StagingKeyNormalizer
{
    /// <summary>
    /// OEM NO 1 只做可逆的展示清洗：去首尾空白、折叠连续空白、统一大写。
    /// WHY 不删除内部字符：OEM 编码中的空格、斜杠和连字符可能有业务含义。
    /// </summary>
    public static string? NormalizeOemNo1(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var folded = string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return folded.ToUpperInvariant();
    }

    /// <summary>
    /// 计算稳定的整行 SHA-256 哈希。
    /// 使用长度前缀区分 null、空字符串和包含分隔符的值，避免简单拼接碰撞。
    /// </summary>
    public static string ComputeRowHash(IReadOnlyList<string?> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (value is null)
            {
                builder.Append("-1:");
            }
            else
            {
                builder.Append(value.Length).Append(':').Append(value);
            }
            builder.Append('|');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
