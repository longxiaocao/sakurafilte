using System.Reflection;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// 反射工具：用于字典 schema 契约端点。
/// 抽出原 Program.cs 中 ToCSharpTypeName / IsNullable / GetPgTableName 顶层静态函数。
/// </summary>
public static class ReflectionExtensions
{
    /// <summary>
    /// C# Type → 契约客户端期望的类型字符串。
    /// int → "int", long? → "long?", DateTime → "datetime", decimal? → "decimal?"
    /// </summary>
    public static string ToCSharpTypeName(this Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t);
        var baseName = underlying?.Name ?? t.Name;
        var nullable = underlying != null ? "?" : "";
        return baseName.ToLower() switch
        {
            "int32" => "int" + nullable,
            "int64" => "long" + nullable,
            "datetime" => "datetime" + nullable,
            "boolean" => "bool" + nullable,
            "decimal" => "decimal" + nullable,
            "double" => "double" + nullable,
            _ => baseName.ToLower() + nullable
        };
    }

    /// <summary>
    /// PropertyInfo → 是否可空。
    /// 引用类型默认可空；Nullable&lt;T&gt; 视为可空。
    /// </summary>
    public static bool IsNullable(this PropertyInfo p)
    {
        if (!p.PropertyType.IsValueType) return true;
        return Nullable.GetUnderlyingType(p.PropertyType) != null;
    }
}
