using System.Text;
using ClosedXML.Excel;

namespace SakuraFilter.Etl;

/// <summary>
/// V3(2026-08-25): ETL 导入模板生成器 (P0 导入向导)
///   WHY: 交付后客户自助导入 — 提供带"表头 + 字段说明 + 示例行"的模板 Excel,
///        客户按说明填表后上传, 由 EtlSpreadsheetAdapter.ConvertAsync 解析 (表头 = JSON 字段名).
///   结构 (2 行式 — 兼容 ConvertAsync 的 Skip(1) 行为, 说明行会被当数据):
///     Row1 表头 (JSON key, 与 EtlImportService 读取 key 严格一致; 字段说明放单元格批注 comment)
///     Row2 示例数据 (橙色底, comment 提示"导入前删除")
/// </summary>
public static class EtlTemplateGenerator
{
    // 列定义: (JSON key, 中文说明含必填标记, 示例值)
    private static readonly (string Key, string Desc, string? Sample)[] ProductsColumns =
    {
        ("mr_1", "必填* 自有产品编码 (唯一主键), 如 MR00000001", "MR00000001"),
        ("oem_no_display", "展示用 OEM 号 (可选)", "000001"),
        ("type", "产品类型: OIL FILTER / AIR FILTER / FUEL FILTER / HYDRAULIC FILTER / OTHER", "OIL FILTER"),
        ("product_name_1", "产品名称 1 (可选)", "OIL FILTER"),
        ("product_name_2", "产品名称 2 (可选)", null),
        ("product_name_3", "产品名称 3 (可选)", null),
        ("oem_2", "OEM 二级号 (可选)", "OIL-1"),
        ("is_published", "是否上架: true 或 false (默认 true)", "true"),
        ("remark", "备注 (可选)", "示例产品"),
        ("d1_mm", "直径 1 (mm, 数字, 可选)", "231.6"),
        ("d2_mm", "直径 2 (mm, 数字, 可选)", "123.5"),
        ("d3_mm", "直径 3 (mm, 数字, 可选)", "175.9"),
        ("d4_mm", "直径 4 (mm, 数字, 可选)", "166.4"),
        ("h1_mm", "高度 1 (mm, 数字, 可选)", "451.8"),
        ("h2_mm", "高度 2 (mm, 数字, 可选)", "354.3"),
        ("h3_mm", "高度 3 (mm, 数字, 可选)", "444.2"),
        ("h4_mm", "高度 4 (mm, 数字, 可选)", "139.7"),
        ("d7_thread", "螺纹规格 (可选), 如 M36x1.5", "M36x1.5"),
        ("d8_thread", "螺纹规格 2 (可选)", null),
    };

    private static readonly (string Key, string Desc, string? Sample)[] XrefsColumns =
    {
        ("mr_1", "必填* 关联的自有产品编码 (需先导入 products, 缺失/找不到则该行跳过)", "MR00000001"),
        ("oem_brand", "必填* OEM 品牌, 如 MAHLE / BOSCH / FRAM", "MAHLE"),
        ("oem_no_3", "必填* OEM 三级号, 如 S1002390", "S1002390"),
        ("oem_2", "OEM 二级号 (可选)", "MAH-01"),
        ("sort_order", "排序 (数字, 越小越靠前, 可选)", "1"),
        ("machine_type", "机型类型 (可选): agriculture / commercial / construction / industrial / others", "agriculture"),
        ("is_published", "是否上架: true 或 false", "true"),
    };

    private static readonly (string Key, string Desc, string? Sample)[] AppsColumns =
    {
        ("mr_1", "必填* 关联的自有产品编码 (需先导入 products)", "MR00000001"),
        ("machine_brand", "必填* 机器品牌, 如 DEUTZ / KUBOTA", "DEUTZ"),
        ("machine_model", "必填* 机器型号, 如 D5297", "D5297"),
        ("model_name", "型号全称 (可选)", "DEUTZ D5297 发动机"),
        ("engine_brand", "发动机品牌 (可选)", "DEUTZ"),
        ("engine_type", "发动机型号 (可选)", "D5297"),
        ("engine_energy", "能源类型 (可选)", "diesel"),
        ("production_date_start", "生产起始日期 (可选, 格式 yyyy-MM-dd)", "2015-01-01"),
        ("is_ongoing", "是否在产: true 或 false", "true"),
        ("machine_category", "机型分类 (可选): agriculture / commercial / industrial / others", "agriculture"),
    };

    public static byte[] Build(string entityKey)
    {
        var columns = entityKey switch
        {
            "products" => ProductsColumns,
            "xrefs" => XrefsColumns,
            "apps" => AppsColumns,
            _ => throw new ArgumentException($"不支持的实体: {entityKey}")
        };

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(entityKey);

        // Row1 表头 (JSON key — ConvertAsync 读取的字段名) + 批注说明
        for (int i = 0; i < columns.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = columns[i].Key;
            cell.GetComment().AddText(columns[i].Desc);
        }
        var headerRange = ws.Range(1, 1, 1, columns.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        headerRange.Style.Alignment.WrapText = true;

        // Row2 示例数据 (橙色底, 提示导入前删除)
        for (int i = 0; i < columns.Length; i++)
        {
            var cell = ws.Cell(2, i + 1);
            if (columns[i].Sample is { } sv)
                cell.Value = sv;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
        }
        ws.Cell(2, 1).GetComment().AddText("示例数据行 — 导入前请删除本行");

        // 列宽自适应 + 冻结表头
        for (int i = 0; i < columns.Length; i++)
            ws.Column(i + 1).AdjustToContents(4, 28);
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
