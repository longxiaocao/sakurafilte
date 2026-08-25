using System.Text.Json;

namespace SakuraFilter.Etl;

/// <summary>
/// V3(2026-08-25): 上传文件实体自动识别 (智能导入入口)
///   用户反馈: 导入页实体三选 (products/xrefs/apps) 令人困惑, 希望统一为智能入口 —
///   上传文件后系统自动判断数据类型, 无需手动选择.
/// 判断依据: 表头/字段特征打分 (模板表头 = JSON key, 见 EtlTemplateGenerator)
///   - apps    : machine_brand / machine_model  (机型适配专用)
///   - xrefs   : oem_brand / oem_no_3           (OEM 交叉引用专用)
///   - products: type / oem_no_normalized / oem_no_display (产品专用)
/// 结果: 最高分实体; 全 0 分默认 products (导入契约最宽)
/// </summary>
public static class EtlEntityDetector
{
    /// <summary>
    /// 从数据文件自动识别实体类型 (jsonl/xlsx 均可)
    /// </summary>
    /// <param name="path">已保存到服务器临时目录的数据文件</param>
    /// <returns>products / xrefs / apps (永不为 null, 默认 products)</returns>
    public static string Detect(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jsonl")
            {
                var keys = PeekJsonlKeys(path);
                return Score(keys);
            }
            if (ext == ".xlsx" || ext == ".xls")
            {
                var keys = PeekXlsxHeader(path);
                return Score(keys);
            }
            return "products";
        }
        catch
        {
            // 解析失败不阻断上传, 走默认 (前端也可手动改)
            return "products";
        }
    }

    private static string Score(HashSet<string> keys)
    {
        if (keys.Count == 0) return "products";

        int apps = 0, xrefs = 0, products = 0;
        foreach (var k in keys)
        {
            switch (k)
            {
                case "machine_brand":
                case "machine_model":
                    apps++;
                    break;
                case "oem_brand":
                case "oem_no_3":
                    xrefs++;
                    break;
                case "type":
                case "oem_no_normalized":
                case "oem_no_display":
                    products++;
                    break;
            }
        }
        // 命中数最高; 相等时优先级 products < xrefs < apps (专用特征更值得信任)
        if (apps >= xrefs && apps >= products && apps > 0) return "apps";
        if (xrefs >= products && xrefs > 0) return "xrefs";
        if (products > 0) return "products";
        return "products";
    }

    /// <summary>jsonl: 读前 10 行聚合 key 集合</summary>
    private static HashSet<string> PeekJsonlKeys(string path)
    {
        var keys = new HashSet<string>();
        using var reader = new StreamReader(path);
        int n = 0;
        while (n < 10)
        {
            var line = reader.ReadLine();
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) { n++; continue; }
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in doc.RootElement.EnumerateObject())
                        keys.Add(p.Name);
                }
            }
            catch { /* 忽略坏行 */ }
            n++;
        }
        return keys;
    }

    /// <summary>xlsx/xls: 遍历所有工作表合并表头 (模板可能多 sheet, 取并集判断实体)
    ///   与 EtlSpreadsheetAdapter 的 sheet 命名 (实体名/中文名) 无关, 只看表头字段</summary>
    private static HashSet<string> PeekXlsxHeader(string path)
    {
        var keys = new HashSet<string>();
        using var wb = new ClosedXML.Excel.XLWorkbook(path);
        foreach (var ws in wb.Worksheets)
        {
            var headerRow = ws.FirstRowUsed();
            if (headerRow is null) continue;
            foreach (var cell in headerRow.CellsUsed())
            {
                var v = cell.GetFormattedString()?.Trim();
                if (!string.IsNullOrEmpty(v)) keys.Add(v);
            }
        }
        return keys;
    }
}
