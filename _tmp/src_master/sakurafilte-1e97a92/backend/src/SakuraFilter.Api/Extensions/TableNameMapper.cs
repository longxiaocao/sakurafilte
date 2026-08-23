namespace SakuraFilter.Api.Extensions;

/// <summary>
/// PostgreSQL 表名映射：用于字典 schema 契约端点。
/// 字典表命名规则: dict_xxx (xref_oem_brand 为历史命名, 不重命名避免大改)。
/// </summary>
public static class TableNameMapper
{
    public static string GetPgTableName(Type t) => t.Name switch
    {
        "XrefOemBrand" => "xref_oem_brand",
        "DictProductName1" => "dict_product_name1",
        "DictProductName2" => "dict_product_name2",
        "DictType" => "dict_type",
        "DictOemNo3" => "dict_oem_no3",
        "DictMedia" => "dict_media",
        "DictMachine" => "dict_machine",
        "DictEngine" => "dict_engine",
        _ => t.Name.ToLower()
    };
}
