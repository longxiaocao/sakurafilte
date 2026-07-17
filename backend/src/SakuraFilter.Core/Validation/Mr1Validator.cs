using System.Text.RegularExpressions;

namespace SakuraFilter.Core.Validation;

/// <summary>
/// V2 Task V17-1.3: MR.1 校验工具 (静态类)
///   WHY 必要: AdminProductService / EtlImportService 均需校验 MR.1,抽取公共逻辑避免重复
///   规则 (spec MR.1):
///     - 长度: 1-10 字符
///     - 字符集: 仅字母数字 (A-Za-z0-9)
///     - 不可为 null/空/空白
///   校验失败抛 ArgumentException (调用方转 400)
/// </summary>
public static class Mr1Validator
{
    /// <summary>MR.1 最大长度 (spec MR.1)</summary>
    public const int Mr1MaxLength = 10;

    /// <summary>MR.1 格式正则 (1-10 位字母数字)</summary>
    private static readonly Regex Mr1Pattern = new(
        @"^[A-Za-z0-9]{1,10}$",
        RegexOptions.Compiled);

    /// <summary>
    /// 校验并规范化 MR.1
    ///   - null/空/空白 → 抛 ArgumentException (MR1_REQUIRED)
    ///   - 含非法字符或长度超限 → 抛 ArgumentException (MR1_FORMAT_INVALID)
    ///   - 通过 → 返回 Trim 后的 MR.1
    /// </summary>
    /// <param name="input">待校验的 MR.1 原始输入</param>
    /// <returns>Trim 后的 MR.1</returns>
    /// <exception cref="ArgumentException">校验失败</exception>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("MR1_REQUIRED: MR.1 必填");

        var trimmed = input.Trim();
        if (!Mr1Pattern.IsMatch(trimmed))
            throw new ArgumentException($"MR1_FORMAT_INVALID: MR.1 必须为 1-10 位字母数字 (实际: '{trimmed}')");

        return trimmed;
    }

    /// <summary>
    /// 非抛出式校验 (用于 ETL 静默跳过场景)
    ///   - 校验通过返回 true + trimmed 值
    ///   - 校验失败返回 false + 错误原因 (不抛异常)
    /// </summary>
    /// <param name="input">待校验的 MR.1 原始输入</param>
    /// <param name="normalized">校验通过时返回 Trim 后的 MR.1,失败时返回 null</param>
    /// <param name="errorReason">校验失败时返回错误原因 (MR1_REQUIRED / MR1_FORMAT_INVALID)</param>
    /// <returns>true=校验通过, false=校验失败</returns>
    public static bool TryNormalize(string? input, out string? normalized, out string? errorReason)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            normalized = null;
            errorReason = "MR1_REQUIRED";
            return false;
        }

        var trimmed = input.Trim();
        if (!Mr1Pattern.IsMatch(trimmed))
        {
            normalized = null;
            errorReason = "MR1_FORMAT_INVALID";
            return false;
        }

        normalized = trimmed;
        errorReason = null;
        return true;
    }
}
