namespace SakuraFilter.Core.Extensions;

/// <summary>
/// LIKE/ILIKE 模式转义扩展 (v24 架构清理)
///
/// 用途: 用户输入文本字段查询 (AdminProduct / AdminDict / Search / PostgresSearchProvider) 前先转义
///       把 %, _, \ 三个通配符当字面量处理, 防止:
///         1) LIKE 注入 (用户输入 % 拖出整列)
///         2) 下划线/百分号被 PG 当通配符导致误命中
///
/// 必须配合 EF.Functions.ILike 三参重载 + ESCAPE '\\' 使用:
///   query.Where(x => EF.Functions.ILike(x.Col, $"%{input.EscapeLikePattern()}%", "\\"));
/// 三参重载: (matchExpression, string pattern, string escapeCharacter)
///
/// 顺序: 先转义反斜杠, 再转义 %, 再转义 _ (顺序错会导致双重转义)
///
/// WHY 抽到 Core: v24 修复 D7/D8 filter 时发现 PostgresSearchProvider (SakuraFilter.Search)
///   与 SakuraFilter.Api.EscapeLikePattern 重复实现, 抽到 Core 供两层引用, 避免架构层次倒置
/// </summary>
public static class LikeEscapeExtensions
{
    /// <summary>
    /// 转义 LIKE/ILIKE 用户输入的 % _ \ 三个特殊字符
    /// 转义后必须配合 EF.Functions.ILike 三参重载 + ESCAPE '\\' 使用
    /// </summary>
    /// <param name="input">用户原始输入 (已 Trim 过的查询关键字)。null/空串原样返回,不抛异常</param>
    /// <returns>转义后的 pattern, 可直接拼到 LIKE 模式两侧 (例: $"%{escaped}%")。null 输入返回 null</returns>
    public static string? EscapeLikePattern(this string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
