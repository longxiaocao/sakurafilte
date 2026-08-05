using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Api.Extensions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using System.Text;
using ClosedXML.Excel;

namespace SakuraFilter.Api.Endpoints;

// OEM 品牌 DTO（其他 7 类字典 DTO 已存在 SakuraFilter.Api.Services 命名空间下）
// 之所以单独声明：原 Program.cs 中作为顶级 record 存在，迁移到 Endpoints 命名空间下。
public record OemBrandCreateRequest(string Brand, int? SortOrder);
public record OemBrandUpdateRequest(string? Brand, int? SortOrder);
public record ImportCsvRequest(string Csv);

/// <summary>
/// 字典管理端点：admin 角色鉴权 + global 限流。
/// 8 类字典: OemBrand / ProductName1 / ProductName2 / Type / OemNo3 / Media / Machine / Engine + schema 契约。
/// </summary>
public static class DictionaryEndpoints
{
    public static IEndpointRouteBuilder MapDictionaryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/dict").WithTags("AdminDict")
            .RequireAuthorization("Admin")  // V24-F19: spec F11
            .RequireRateLimiting("global");

        MapOemBrandEndpoints(group);
        MapProductName1Endpoints(group);
        MapProductName2Endpoints(group);
        MapTypeEndpoints(group);
        MapOemNo3Endpoints(group);
        MapMediaEndpoints(group);
        MapMachineEndpoints(group);
        MapEngineEndpoints(group);
        MapSchemaEndpoint(group);

        return app;
    }

    // -------------------- OEM 品牌 --------------------

    private static void MapOemBrandEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/oem-brands");

        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            OemBrandDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListOemBrandsAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListOemBrands");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            OemBrandDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadOemBrandsAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadOemBrands");

        g.MapPost("/", async (
            OemBrandCreateRequest body, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Brand))
                return Results.BadRequest(new { error = "brand 不能为空" });
            try
            {
                var item = await svc.CreateOemBrandAsync(body.Brand, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/oem-brands/{item.Id}", item);
            }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateOemBrand");

        // 🔧 fix(审查): 批量导出 CSV — 用户反馈: 无数据导入导出入口, 只能逐个添加
        g.MapGet("/export", async (OemBrandDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListOemBrandsAsync(null, true, 100000, ct);
            var sb = new StringBuilder("brand,sortOrder,deleted\n");
            foreach (var it in items)
            {
                // 🔧 fix(审查): CSV 数据安全 — 纯数字品牌名用 ="..." 公式包裹 (Excel 打开不转数字/丢前导零)
                string brand;
                if (it.Brand.Length > 1 && it.Brand.All(char.IsDigit)) brand = $"=\"{it.Brand}\"";
                else if (it.Brand.Contains(',') || it.Brand.Contains('"')) brand = $"\"{it.Brand.Replace("\"", "\"\"")}\"";
                else brand = it.Brand;
                sb.AppendLine($"{brand},{it.SortOrder},{(it.DeletedAt == null ? 0 : 1)}");
            }
            return Results.Text(sb.ToString(), "text/csv; charset=utf-8");
        }).WithName("AdminExportOemBrands");
        // 🔧 fix(审查): XLSX 导出 (双格式 — ClosedXML, 前导零/长数字零风险)
        g.MapGet("/export-xlsx", async (OemBrandDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListOemBrandsAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "brand", "sortOrder", "deleted" },
                x => new object?[] { x.Brand, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportOemBrandsXlsx");

        // 🔧 fix(审查): 批量导入 CSV — 格式: brand[,sortOrder[,deleted]] 每行一个品牌
        //   deleted=1 时软删该品牌 (不硬删, 与现有字典删除策略一致)
        g.MapPost("/import", async (
            ImportCsvRequest body, OemBrandDictService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Csv))
                return Results.BadRequest(new { error = "csv 不能为空" });
            var rows = body.Csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.Split(',')).ToList();
            return await ImportOemBrandRows(rows, svc, ct);
        }).WithName("AdminImportOemBrands");
        // 🔧 fix(审查): XLSX 批量导入 (双格式 — 复用行级处理, 解析后与 CSV 同逻辑)
        g.MapPost("/import-xlsx", async (IFormFile file, OemBrandDictService svc, CancellationToken ct) =>
            await ImportOemBrandRows(ParseXlsxRows(file), svc, ct)).DisableAntiforgery().WithName("AdminImportOemBrandsXlsx");

        g.MapPut("/{id:long}", async (
            long id, OemBrandUpdateRequest body, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateOemBrandAsync(id, body.Brand, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateOemBrand");

        g.MapDelete("/{id:long}", async (long id, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteOemBrandAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteOemBrand");

        g.MapPost("/{id:long}/restore", async (long id, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreOemBrandAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreOemBrand");

        g.MapPost("/reorder", async (
            OemBrandReorderRequest body, OemBrandDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderOemBrandsAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderOemBrands");
    }

    // -------------------- Product Name 1 --------------------

    // 🔧 fix(审查): 批量导入导出泛型辅助 — OEM 品牌先例 → 6 类单字段字典复用
    //   CSV 格式: value,sortOrder,deleted (deleted=1 软删; 大小写不敏感匹配 upsert)
    private static async Task<IResult> ExportDictCsv<TItem>(
        IDictService<TItem> svc,
        Func<TItem, string> getValue,
        Func<TItem, int?> getSort,
        Func<TItem, DateTime?> getDeleted,
        CancellationToken ct)
        where TItem : class
    {
        var items = await svc.ListAsync(null, true, 100000, ct);
        var sb = new StringBuilder("value,sortOrder,deleted\n");
        foreach (var it in items)
        {
            var v = getValue(it);
            // 🔧 fix(审查): CSV 数据安全 — 纯数字文本用 ="..." 公式包裹 (Excel 打开时不转数字/丢前导零/科学计数法)
            //   sortOrder 等数字语义字段保持数字, 不加公式
            string vv;
            if (v.Length > 1 && v.All(char.IsDigit)) vv = $"=\"{v}\"";
            else if (v.Contains(',') || v.Contains('"')) vv = $"\"{v.Replace("\"", "\"\"")}\"";
            else vv = v;
            sb.AppendLine($"{vv},{getSort(it)},{(getDeleted(it) == null ? 0 : 1)}");
        }
        return Results.Text(sb.ToString(), "text/csv; charset=utf-8");
    }

    private static string CsvSafe(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Length > 1 && s.All(char.IsDigit)) return $"=\"{s.Replace("\"", "\"\"")}\"";
        if (s.Contains(',') || s.Contains('"')) return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    // 🔧 fix(审查): CSV 字段导入解析 — 剥离 ="..." 公式 (导出端数字安全) 与 ' 前缀, 再 Trim
    private static string CsvUnwrap(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var v = s.Trim();
        if (v.StartsWith("=\"") && v.EndsWith("\"") && v.Length >= 4) v = v.Substring(2, v.Length - 3);
        else if (v.StartsWith("'") && v.Length > 1) v = v.Substring(1);
        return v.Trim().Trim('"');
    }

    // 🔧 fix(审查): OEM 品牌行级导入 (CSV/XLSX 共用) — 格式: brand[,sortOrder[,deleted]]
    private static async Task<IResult> ImportOemBrandRows(IEnumerable<string[]> rows, OemBrandDictService svc, CancellationToken ct)
    {
        int created = 0, updated = 0, deleted = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var parts in rows)
        {
            if (parts.Length >= 1 && parts[0].Trim().ToLowerInvariant() == "brand") continue;  // 表头
            var brand = CsvUnwrap(parts.Length > 0 ? parts[0] : "");
            if (string.IsNullOrWhiteSpace(brand)) { skipped++; continue; }
            int? sort = parts.Length > 1 && int.TryParse(CsvUnwrap(parts[1]), out var s) ? s : null;
            var wantDeleted = parts.Length > 2 && CsvUnwrap(parts[2]) == "1";
            try
            {
                var existing = (await svc.ListOemBrandsAsync(brand, true, 5, ct))
                    .FirstOrDefault(x => x.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase));
                if (wantDeleted)
                {
                    if (existing != null && existing.DeletedAt == null) { await svc.DeleteOemBrandAsync(existing.Id, ct); deleted++; }
                    else { skipped++; }
                    continue;
                }
                if (existing != null)
                {
                    if (existing.DeletedAt != null) { await svc.RestoreOemBrandAsync(existing.Id, ct); await svc.UpdateOemBrandAsync(existing.Id, brand, sort, ct); }
                    else { await svc.UpdateOemBrandAsync(existing.Id, brand, sort, ct); }
                    updated++;
                }
                else { await svc.CreateOemBrandAsync(brand, sort, ct); created++; }
            }
            catch (Exception ex) { errors.Add($"{brand}: {ex.Message}"); }
        }
        return Results.Ok(new { created, updated, deleted, skipped, errors });
    }

    // 🔧 fix(审查): 机型字典行级导入 (CSV/XLSX 共用) — 6 列: brand,model,name,category,sortOrder,deleted
    private static async Task<IResult> ImportMachineRows(IEnumerable<string[]> rows, MachineDictService svc, CancellationToken ct)
    {
        int created = 0, updated = 0, deleted = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var p in rows)
        {
            if (p.Length >= 1 && p[0].Trim().ToLowerInvariant() == "brand") continue;  // 表头
            var brand = CsvUnwrap(p.Length > 0 ? p[0] : "");
            var model = CsvUnwrap(p.Length > 1 ? p[1] : "");
            var name = CsvUnwrap(p.Length > 2 ? p[2] : "");
            var category = CsvUnwrap(p.Length > 3 ? p[3] : "");
            if (string.IsNullOrWhiteSpace(brand)) { skipped++; continue; }
            int? sort = p.Length > 4 && int.TryParse(CsvUnwrap(p[4]), out var s) ? s : null;
            var wantDeleted = p.Length > 5 && CsvUnwrap(p[5]) == "1";
            try
            {
                var existing = (await svc.ListMachinesAsync(brand, true, 5, ct))
                    .FirstOrDefault(m => m.MachineBrand.Equals(brand, StringComparison.OrdinalIgnoreCase));
                if (wantDeleted)
                {
                    if (existing != null && existing.DeletedAt == null) { await svc.DeleteMachineAsync(existing.Id, ct); deleted++; }
                    else { skipped++; }
                    continue;
                }
                if (existing != null)
                {
                    if (existing.DeletedAt != null) { await svc.RestoreMachineAsync(existing.Id, ct); }
                    await svc.UpdateMachineAsync(existing.Id, brand, string.IsNullOrEmpty(model) ? null : model,
                        string.IsNullOrEmpty(name) ? null : name, sort, string.IsNullOrEmpty(category) ? "others" : category, ct);
                    updated++;
                }
                else
                {
                    await svc.CreateMachineAsync(brand, string.IsNullOrEmpty(model) ? null : model,
                        string.IsNullOrEmpty(name) ? null : name, sort, string.IsNullOrEmpty(category) ? "others" : category, ct);
                    created++;
                }
            }
            catch (Exception ex) { errors.Add($"{brand}/{model}: {ex.Message}"); }
        }
        return Results.Ok(new { created, updated, deleted, skipped, errors });
    }

    // 🔧 fix(审查): CSV/XLSX 共用行级导入处理 (双格式: XLSX 解析成行列后走同一 upsert 逻辑)
    private static async Task<IResult> ImportDictRows<TItem>(
        IEnumerable<string[]> rows,
        IDictService<TItem> svc,
        Func<TItem, string> getValue,
        Func<TItem, DateTime?> getDeleted,
        Func<TItem, long> getId,
        CancellationToken ct)
        where TItem : class
    {
        int created = 0, updated = 0, deleted = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var parts in rows)
        {
            // 表头行跳过 (value / brand 为首列)
            if (parts.Length >= 1 && (parts[0].Trim().ToLowerInvariant() is "value" or "brand"))
                continue;
            var value = CsvUnwrap(parts.Length > 0 ? parts[0] : "");
            if (string.IsNullOrWhiteSpace(value)) { skipped++; continue; }
            int? sort = parts.Length > 1 && int.TryParse(CsvUnwrap(parts[1]), out var s) ? s : null;
            var wantDeleted = parts.Length > 2 && CsvUnwrap(parts[2]) == "1";
            try
            {
                var existing = (await svc.ListAsync(value, true, 5, ct))
                    .FirstOrDefault(x => getValue(x).Equals(value, StringComparison.OrdinalIgnoreCase));
                if (wantDeleted)
                {
                    if (existing != null && getDeleted(existing) == null) { await svc.DeleteAsync(getId(existing), ct); deleted++; }
                    else { skipped++; }
                    continue;
                }
                if (existing != null)
                {
                    if (getDeleted(existing) != null) { await svc.RestoreAsync(getId(existing), ct); await svc.UpdateAsync(getId(existing), value, sort, ct); }
                    else { await svc.UpdateAsync(getId(existing), value, sort, ct); }
                    updated++;
                }
                else { await svc.CreateAsync(value, sort, ct); created++; }
            }
            catch (Exception ex) { errors.Add($"{value}: {ex.Message}"); }
        }
        return Results.Ok(new { created, updated, deleted, skipped, errors });
    }

    private static async Task<IResult> ImportDictCsv<TItem>(
        string csv,
        IDictService<TItem> svc,
        Func<TItem, string> getValue,
        Func<TItem, DateTime?> getDeleted,
        Func<TItem, long> getId,
        CancellationToken ct)
        where TItem : class
    {
        if (string.IsNullOrWhiteSpace(csv))
            return Results.BadRequest(new { error = "csv 不能为空" });
        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(',')).ToList();
        return await ImportDictRows(rows, svc, getValue, getDeleted, getId, ct);
    }

    // 🔧 fix(审查): XLSX 导出 (双格式 — ClosedXML 复用 ETL 已有依赖, 无新增包)
    //   XLSX 天然文本格式: 前导零/长数字零风险 (用户 Excel 直接编辑)
    private static IResult ExportDictXlsx<TItem>(
        IEnumerable<TItem> items,
        string[] headers,
        Func<TItem, object?[]> getRow)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("data");
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        int r = 2;
        foreach (var it in items)
        {
            var row = getRow(it);
            for (int c = 0; c < row.Length; c++)
            {
                var v = row[c];
                ws.Cell(r, c + 1).Value = v switch
                {
                    null => "",
                    int i => i,
                    DateTime dt => dt,
                    _ => v.ToString() ?? ""
                };
            }
            r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "dict-export.xlsx");
    }

    // 🔧 fix(审查): XLSX 导入解析 — 首个工作表, 前 20 列, 单元格转字符串
    private static List<string[]> ParseXlsxRows(IFormFile file)
    {
        using var ms = new MemoryStream();
        file.CopyTo(ms);
        ms.Position = 0;
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        var rows = new List<string[]>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 1; r <= lastRow; r++)
        {
            var cells = new List<string>();
            for (int c = 1; c <= 20; c++)
            {
                var cell = ws.Cell(r, c);
                cells.Add(cell.IsEmpty() ? "" : cell.GetString());
            }
            rows.Add(cells.ToArray());
        }
        return rows;
    }

    private static void MapProductName1Endpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/product-name1s");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            ProductName1DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListProductName1sAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListProductName1s");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            ProductName1DictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadProductName1sAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadProductName1s");

        g.MapPost("/", async (
            ProductName1CreateRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProductName1)) return Results.BadRequest(new { error = "productName1 不能为空" });
            try { var item = await svc.CreateProductName1Async(body.ProductName1, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/product-name1s/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateProductName1");
        g.MapGet("/export", async (ProductName1DictService svc, CancellationToken ct) =>
            await ExportDictCsv(svc, x => x.ProductName1, x => x.SortOrder, x => x.DeletedAt, ct)).WithName("AdminExportProductName1s");
        g.MapGet("/export-xlsx", async (ProductName1DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "value", "sortOrder", "deleted" }, x => new object?[] { x.ProductName1, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportProductName1sXlsx");
        g.MapPost("/import", async (ImportCsvRequest body, ProductName1DictService svc, CancellationToken ct) =>
            await ImportDictCsv<DictProductName1>(body.Csv, svc, x => x.ProductName1, x => x.DeletedAt, x => x.Id, ct)).WithName("AdminImportProductName1s");
        g.MapPost("/import-xlsx", async (IFormFile file, ProductName1DictService svc, CancellationToken ct) =>
            await ImportDictRows(ParseXlsxRows(file), svc, x => x.ProductName1, x => x.DeletedAt, x => x.Id, ct)).DisableAntiforgery().WithName("AdminImportProductName1sXlsx");

        g.MapPut("/{id:long}", async (
            long id, ProductName1UpdateRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateProductName1Async(id, body.ProductName1, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateProductName1");

        g.MapDelete("/{id:long}", async (long id, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteProductName1Async(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteProductName1");

        g.MapPost("/{id:long}/restore", async (long id, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreProductName1Async(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreProductName1");

        g.MapPost("/reorder", async (
            ProductName1ReorderRequest body, ProductName1DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderProductName1sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderProductName1s");
    }

    // -------------------- Product Name 2 --------------------

    private static void MapProductName2Endpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/product-name2s");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            ProductName2DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListProductName2sAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListProductName2s");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            ProductName2DictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadProductName2sAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadProductName2s");

        g.MapPost("/", async (
            ProductName2CreateRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ProductName2)) return Results.BadRequest(new { error = "productName2 不能为空" });
            try { var item = await svc.CreateProductName2Async(body.ProductName2, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/product-name2s/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateProductName2");
        g.MapGet("/export", async (ProductName2DictService svc, CancellationToken ct) =>
            await ExportDictCsv(svc, x => x.ProductName2, x => x.SortOrder, x => x.DeletedAt, ct)).WithName("AdminExportProductName2s");
        g.MapGet("/export-xlsx", async (ProductName2DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "value", "sortOrder", "deleted" }, x => new object?[] { x.ProductName2, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportProductName2sXlsx");
        g.MapPost("/import", async (ImportCsvRequest body, ProductName2DictService svc, CancellationToken ct) =>
            await ImportDictCsv<DictProductName2>(body.Csv, svc, x => x.ProductName2, x => x.DeletedAt, x => x.Id, ct)).WithName("AdminImportProductName2s");
        g.MapPost("/import-xlsx", async (IFormFile file, ProductName2DictService svc, CancellationToken ct) =>
            await ImportDictRows(ParseXlsxRows(file), svc, x => x.ProductName2, x => x.DeletedAt, x => x.Id, ct)).DisableAntiforgery().WithName("AdminImportProductName2sXlsx");

        g.MapPut("/{id:long}", async (
            long id, ProductName2UpdateRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateProductName2Async(id, body.ProductName2, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateProductName2");

        g.MapDelete("/{id:long}", async (long id, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteProductName2Async(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteProductName2");

        g.MapPost("/{id:long}/restore", async (long id, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreProductName2Async(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreProductName2");

        g.MapPost("/reorder", async (
            ProductName2ReorderRequest body, ProductName2DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderProductName2sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderProductName2s");
    }

    // -------------------- Type --------------------

    private static void MapTypeEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/types");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            TypeDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListTypesAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListTypes");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            TypeDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadTypesAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadTypes");

        g.MapPost("/", async (
            TypeCreateRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Type)) return Results.BadRequest(new { error = "type 不能为空" });
            try { var item = await svc.CreateTypeAsync(body.Type, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/types/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateType");
        g.MapGet("/export", async (TypeDictService svc, CancellationToken ct) =>
            await ExportDictCsv(svc, x => x.Type, x => x.SortOrder, x => x.DeletedAt, ct)).WithName("AdminExportTypes");
        g.MapGet("/export-xlsx", async (TypeDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "value", "sortOrder", "deleted" }, x => new object?[] { x.Type, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportTypesXlsx");
        g.MapPost("/import", async (ImportCsvRequest body, TypeDictService svc, CancellationToken ct) =>
            await ImportDictCsv<DictType>(body.Csv, svc, x => x.Type, x => x.DeletedAt, x => x.Id, ct)).WithName("AdminImportTypes");
        g.MapPost("/import-xlsx", async (IFormFile file, TypeDictService svc, CancellationToken ct) =>
            await ImportDictRows(ParseXlsxRows(file), svc, x => x.Type, x => x.DeletedAt, x => x.Id, ct)).DisableAntiforgery().WithName("AdminImportTypesXlsx");

        g.MapPut("/{id:long}", async (
            long id, TypeUpdateRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateTypeAsync(id, body.Type, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateType");

        g.MapDelete("/{id:long}", async (long id, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteTypeAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteType");

        g.MapPost("/{id:long}/restore", async (long id, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreTypeAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreType");

        g.MapPost("/reorder", async (
            TypeReorderRequest body, TypeDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderTypesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderTypes");
    }

    // -------------------- OEM No3 --------------------

    private static void MapOemNo3Endpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/oem-no3s");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            OemNo3DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListOemNo3sAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListOemNo3s");

        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            OemNo3DictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadOemNo3sAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadOemNo3s");

        g.MapPost("/", async (
            OemNo3CreateRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.OemNo3)) return Results.BadRequest(new { error = "oemNo3 不能为空" });
            try { var item = await svc.CreateOemNo3Async(body.OemNo3, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/oem-no3s/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateOemNo3");
        g.MapGet("/export", async (OemNo3DictService svc, CancellationToken ct) =>
            await ExportDictCsv(svc, x => x.OemNo3, x => x.SortOrder, x => x.DeletedAt, ct)).WithName("AdminExportOemNo3s");
        g.MapGet("/export-xlsx", async (OemNo3DictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "value", "sortOrder", "deleted" }, x => new object?[] { x.OemNo3, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportOemNo3sXlsx");
        g.MapPost("/import", async (ImportCsvRequest body, OemNo3DictService svc, CancellationToken ct) =>
            await ImportDictCsv<DictOemNo3>(body.Csv, svc, x => x.OemNo3, x => x.DeletedAt, x => x.Id, ct)).WithName("AdminImportOemNo3s");
        g.MapPost("/import-xlsx", async (IFormFile file, OemNo3DictService svc, CancellationToken ct) =>
            await ImportDictRows(ParseXlsxRows(file), svc, x => x.OemNo3, x => x.DeletedAt, x => x.Id, ct)).DisableAntiforgery().WithName("AdminImportOemNo3sXlsx");

        g.MapPut("/{id:long}", async (
            long id, OemNo3UpdateRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateOemNo3Async(id, body.OemNo3, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateOemNo3");

        g.MapDelete("/{id:long}", async (long id, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteOemNo3Async(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteOemNo3");

        g.MapPost("/{id:long}/restore", async (long id, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreOemNo3Async(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreOemNo3");

        g.MapPost("/reorder", async (
            OemNo3ReorderRequest body, OemNo3DictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderOemNo3sAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderOemNo3s");
    }

    // -------------------- Media (2 字段) --------------------

    private static void MapMediaEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/medias");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            MediaDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListMediasAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListMedias");
        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            MediaDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadMediasAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadMedias");
        g.MapPost("/", async (
            MediaCreateRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.MediaName)) return Results.BadRequest(new { error = "mediaName 不能为空" });
            try { var item = await svc.CreateMediaAsync(body.MediaName, body.MediaModel, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/medias/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateMedia");
        g.MapGet("/export", async (MediaDictService svc, CancellationToken ct) =>
            await ExportDictCsv(svc, x => x.MediaName, x => x.SortOrder, x => x.DeletedAt, ct)).WithName("AdminExportMedias");
        g.MapGet("/export-xlsx", async (MediaDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "value", "sortOrder", "deleted" }, x => new object?[] { x.MediaName, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportMediasXlsx");
        g.MapPost("/import", async (ImportCsvRequest body, MediaDictService svc, CancellationToken ct) =>
            await ImportDictCsv<DictMedia>(body.Csv, svc, x => x.MediaName, x => x.DeletedAt, x => x.Id, ct)).WithName("AdminImportMedias");
        g.MapPost("/import-xlsx", async (IFormFile file, MediaDictService svc, CancellationToken ct) =>
            await ImportDictRows(ParseXlsxRows(file), svc, x => x.MediaName, x => x.DeletedAt, x => x.Id, ct)).DisableAntiforgery().WithName("AdminImportMediasXlsx");
        g.MapPut("/{id:long}", async (
            long id, MediaUpdateRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateMediaAsync(id, body.MediaName, body.MediaModel, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateMedia");
        g.MapDelete("/{id:long}", async (long id, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteMediaAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteMedia");
        g.MapPost("/{id:long}/restore", async (long id, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreMediaAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreMedia");
        g.MapPost("/reorder", async (
            MediaReorderRequest body, MediaDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderMediasAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderMedias");
    }

    // -------------------- Machine (3 字段) --------------------

    private static void MapMachineEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/machines");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            MachineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListMachinesAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListMachines");
        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            MachineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadMachinesAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadMachines");
        g.MapPost("/", async (
            MachineCreateRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.MachineBrand)) return Results.BadRequest(new { error = "machineBrand 不能为空" });
            try { var item = await svc.CreateMachineAsync(body.MachineBrand, body.MachineModel, body.MachineName, body.SortOrder, body.MachineCategory, ct);
                return Results.Created($"/api/admin/dict/machines/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateMachine");
        // 🔧 fix(审查): 机型字典批量导入导出 (用户反馈: 需要扩展) — CSV 6 列: brand,model,name,category,sortOrder,deleted
        //   数字安全: 纯数字字段 ="..." 包裹 (Excel 防前导零丢失), 导入剥离 (同 OemBrand/泛型)
        g.MapGet("/export", async (MachineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListMachinesAsync(null, true, 100000, ct);
            var sb = new StringBuilder("brand,model,name,category,sortOrder,deleted\n");
            foreach (var it in items)
            {
                sb.AppendLine($"{CsvSafe(it.MachineBrand)},{CsvSafe(it.MachineModel)},{CsvSafe(it.MachineName)},{CsvSafe(it.MachineCategory)},{it.SortOrder},{(it.DeletedAt == null ? 0 : 1)}");
            }
            return Results.Text(sb.ToString(), "text/csv; charset=utf-8");
        }).WithName("AdminExportMachines");
        // 🔧 fix(审查): 机型字典 XLSX 导出 (双格式)
        g.MapGet("/export-xlsx", async (MachineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListMachinesAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "brand", "model", "name", "category", "sortOrder", "deleted" },
                x => new object?[] { x.MachineBrand, x.MachineModel, x.MachineName, x.MachineCategory, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportMachinesXlsx");
        g.MapPost("/import", async (
            ImportCsvRequest body, MachineDictService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Csv))
                return Results.BadRequest(new { error = "csv 不能为空" });
            var rows = body.Csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.Split(',')).ToList();
            return await ImportMachineRows(rows, svc, ct);
        }).WithName("AdminImportMachines");
        // 🔧 fix(审查): 机型字典 XLSX 导入 (双格式)
        g.MapPost("/import-xlsx", async (IFormFile file, MachineDictService svc, CancellationToken ct) =>
            await ImportMachineRows(ParseXlsxRows(file), svc, ct)).DisableAntiforgery().WithName("AdminImportMachinesXlsx");
        g.MapPut("/{id:long}", async (
            long id, MachineUpdateRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateMachineAsync(id, body.MachineBrand, body.MachineModel, body.MachineName, body.SortOrder, body.MachineCategory, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateMachine");
        g.MapDelete("/{id:long}", async (long id, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteMachineAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteMachine");
        g.MapPost("/{id:long}/restore", async (long id, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreMachineAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreMachine");
        g.MapPost("/reorder", async (
            MachineReorderRequest body, MachineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderMachinesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderMachines");
    }

    // -------------------- Engine (2 字段) --------------------

    private static void MapEngineEndpoints(IEndpointRouteBuilder group)
    {
        var g = group.MapGroup("/engines");
        g.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] bool? includeDeleted, [FromQuery] int? limit,
            EngineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListEnginesAsync(q, includeDeleted ?? false, limit, ct);
            var total = await svc.CountAsync(q, includeDeleted ?? false, ct);
            return Results.Ok(new { total, count = items.Count, items });
        }).WithName("AdminListEngines");
        g.MapGet("/typeahead", async (
            [FromQuery] string? q, [FromQuery] int? limit,
            EngineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.TypeaheadEnginesAsync(q, limit, ct);
            return Results.Ok(new { count = items.Count, items });
        }).WithName("AdminTypeaheadEngines");
        g.MapPost("/", async (
            EngineCreateRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.EngineBrand)) return Results.BadRequest(new { error = "engineBrand 不能为空" });
            try { var item = await svc.CreateEngineAsync(body.EngineBrand, body.EngineType, body.SortOrder, ct);
                return Results.Created($"/api/admin/dict/engines/{item.Id}", item); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminCreateEngine");
        g.MapGet("/export", async (EngineDictService svc, CancellationToken ct) =>
            await ExportDictCsv(svc, x => x.EngineBrand, x => x.SortOrder, x => x.DeletedAt, ct)).WithName("AdminExportEngines");
        g.MapGet("/export-xlsx", async (EngineDictService svc, CancellationToken ct) =>
        {
            var items = await svc.ListAsync(null, true, 100000, ct);
            return ExportDictXlsx(items, new[] { "value", "sortOrder", "deleted" }, x => new object?[] { x.EngineBrand, x.SortOrder, x.DeletedAt == null ? 0 : 1 });
        }).WithName("AdminExportEnginesXlsx");
        g.MapPost("/import", async (ImportCsvRequest body, EngineDictService svc, CancellationToken ct) =>
            await ImportDictCsv<DictEngine>(body.Csv, svc, x => x.EngineBrand, x => x.DeletedAt, x => x.Id, ct)).WithName("AdminImportEngines");
        g.MapPost("/import-xlsx", async (IFormFile file, EngineDictService svc, CancellationToken ct) =>
            await ImportDictRows(ParseXlsxRows(file), svc, x => x.EngineBrand, x => x.DeletedAt, x => x.Id, ct)).DisableAntiforgery().WithName("AdminImportEnginesXlsx");
        g.MapPut("/{id:long}", async (
            long id, EngineUpdateRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.UpdateEngineAsync(id, body.EngineBrand, body.EngineType, body.SortOrder, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminUpdateEngine");
        g.MapDelete("/{id:long}", async (long id, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.DeleteEngineAsync(id, ct); return Results.Ok(new { id, deleted = true }); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminDeleteEngine");
        g.MapPost("/{id:long}/restore", async (long id, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { return Results.Ok(await svc.RestoreEngineAsync(id, ct)); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (InvalidOperationException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminRestoreEngine");
        g.MapPost("/reorder", async (
            EngineReorderRequest body, EngineDictService svc, HttpContext ctx, CancellationToken ct) =>
        {
            try { await svc.ReorderEnginesAsync(body.Items, ct); return Results.Ok(new { updated = body.Items?.Count ?? 0 }); }
            catch (ArgumentException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
            catch (KeyNotFoundException ex) { return ProblemDetailsFactory.FromException(ctx, ex); }
        }).WithName("AdminReorderEngines");
    }

    // -------------------- schema 契约端点 --------------------

    private static void MapSchemaEndpoint(IEndpointRouteBuilder group)
    {
        // V24-F13: nullable 字段改用 EF Core metadata 判断 (而非纯反射)
        //   WHY: ReflectionExtensions.IsNullable() 对所有引用类型一律返回 true,
        //        无法识别 DictMachine.MachineCategory 配置了 .IsRequired() (DB 列 NOT NULL)
        //   方案: 从 ProductDbContext.Model 取 IEntityType.FindProperty().IsNullable,
        //        它综合 CLR 类型 + Fluent API 配置, 与 DB 列实际 NOT NULL 一致
        //   兜底: 若属性未在 EF metadata 中注册 (如导航属性), 回退到反射判断
        group.MapGet("/_schema", (ProductDbContext db) =>
        {
            var dictTypes = new[]
            {
                typeof(SakuraFilter.Core.Entities.XrefOemBrand),
                typeof(SakuraFilter.Core.Entities.DictProductName1),
                typeof(SakuraFilter.Core.Entities.DictProductName2),
                typeof(SakuraFilter.Core.Entities.DictType),
                typeof(SakuraFilter.Core.Entities.DictOemNo3),
                typeof(SakuraFilter.Core.Entities.DictMedia),
                typeof(SakuraFilter.Core.Entities.DictMachine),
                typeof(SakuraFilter.Core.Entities.DictEngine)
            };
            var schema = dictTypes.Select(t =>
            {
                var et = db.Model.FindEntityType(t);
                return new
                {
                    Entity = t.Name,
                    Table = SakuraFilter.Api.Extensions.TableNameMapper.GetPgTableName(t),
                    Fields = t.GetProperties()
                        .Select(p =>
                        {
                            var efProp = et?.FindProperty(p.Name);
                            return new
                            {
                                Name = p.Name,
                                CSharpType = p.PropertyType.ToCSharpTypeName(),
                                Nullable = efProp?.IsNullable ?? p.IsNullable(),
                                HasColumn = p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute), false).Any()
                            };
                        })
                        .ToArray()
                };
            });
            return Results.Ok(new
            {
                generatedAt = DateTime.UtcNow.ToString("O"),
                count = dictTypes.Length,
                dictionaries = schema
            });
        })
        .WithName("AdminDictSchema");
    }
}
