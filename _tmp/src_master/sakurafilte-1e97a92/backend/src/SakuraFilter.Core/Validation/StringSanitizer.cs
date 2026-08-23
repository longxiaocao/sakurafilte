namespace SakuraFilter.Core.Validation;

/// <summary>
/// V2 Task 0.3.16 / 5.1.17: 字符串控制字符清理工具 (静态类)
///   WHY 必要: AdminProductService.ValidateForm 与 EtlImportService.GetStringOrNull 均需过滤控制字符,
///            抽取公共逻辑避免重复 (D3-12 / D3-27)
///   规则 (spec Task 0.3.16):
///     - 移除 U+0000-U+001F (保留 \t \n \r, Excel 多行文本兼容)
///     - 移除 U+007F-U+009F (C1 控制字符)
///     - 移除 BMP 私用区 U+E000-U+F8FF (防 Meilisearch 高亮占位符注入)
///     - 移除非字符 U+FDD0-U+FDEF, U+FFFE/U+FFFF
///   返回: 清理后的字符串; 输入 null 返回 null
///   注意: 不删除合法 Unicode 辅助平面字符 (如 emoji),仅清理 BMP 内的控制/私用/非字符
/// </summary>
public static class StringSanitizer
{
    /// <summary>
    /// 移除字符串中的控制字符 / BMP 私用区 / 非字符 (保留 \t \n \r)
    /// </summary>
    /// <param name="input">待清理的字符串</param>
    /// <returns>清理后的字符串; 输入 null 返回 null</returns>
    public static string? StripControlChars(string? input)
    {
        if (input is null) return null;
        if (input.Length == 0) return input;

        // 快速路径: 扫描无非法字符直接返回原串 (避免不必要的 string 分配)
        bool needsClean = false;
        foreach (var c in input)
        {
            if (IsControlChar(c)) { needsClean = true; break; }
        }
        if (!needsClean) return input;

        // 慢路径: 构建清理后的字符串
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (!IsControlChar(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 判断字符是否为需移除的控制字符 / BMP 私用区 / 非字符
    ///   保留 \t (U+0009) / \n (U+000A) / \r (U+000D), 兼容 Excel 多行文本
    /// </summary>
    private static bool IsControlChar(char c)
    {
        // C0 控制字符 (U+0000-U+001F), 保留 \t \n \r
        if (c <= 0x001F && c != '\t' && c != '\n' && c != '\r') return true;
        // C1 控制字符 (U+007F-U+009F)
        if (c >= 0x007F && c <= 0x009F) return true;
        // BMP 私用区 (U+E000-U+F8FF) - 防 Meilisearch 高亮占位符 \uE000/\uE001 注入
        if (c >= 0xE000 && c <= 0xF8FF) return true;
        // 非字符 (U+FDD0-U+FDEF)
        if (c >= 0xFDD0 && c <= 0xFDEF) return true;
        // 非字符 (U+FFFE / U+FFFF)
        if (c == 0xFFFE || c == 0xFFFF) return true;
        return false;
    }
}
