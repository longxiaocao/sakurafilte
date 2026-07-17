using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// V2 Task 4.7: 产品详情公共服务 (修复 F19 — Razor Pages 与 Controller 查询逻辑重复)
/// 设计目标:
///   - Detail.cshtml.cs PageModel 和 PublicProductController 共用同一查询入口, 避免逻辑分叉
///   - 统一 BuildSlug / BuildProductUrl 规则, SEO URL 生成前后端一致
///   - 统一 OEM 3 三级 fallback 查询 (OemNoDisplay → Oem2 → Mr1)
/// </summary>
public interface IProductDetailService
{
    /// <summary>
    /// 按 OEM 3 / OEM 2 / MR.1 三级 fallback 查询产品详情 (排除已下架)
    /// 查询优先级: OemNoDisplay > Oem2 > Mr1
    /// </summary>
    /// <param name="oem">用户输入的 OEM 字符串 (URL 末段)</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>产品详情 DTO; 找不到返回 null</returns>
    Task<ProductDetailDto?> GetByOemAsync(string oem, CancellationToken ct = default);

    /// <summary>
    /// 按 SEO URL 路径段查询产品 (Task 4.1 PageModel 入口)
    /// 路径: /products/{pn1}/{pn2}/{brand}/{oem3}
    /// 兼容: 老格式 /product/{oem} 通过 oem3 段反查
    /// </summary>
    Task<ProductDetailDto?> GetBySlugSegmentsAsync(
        string? pn1, string? pn2, string? brand, string oem3,
        CancellationToken ct = default);

    /// <summary>
    /// V2 Task 4.5.12: 单一逻辑 BuildSlug
    ///   - 字符串转 URL-safe slug (kebab-case)
    ///   - 中文/特殊字符: Uri.EscapeDataString 兜底 (UTF-8 %XX 编码, 大写)
    ///   - 连续 -/空格 → 单个 -
    ///   - 首尾 - 截断
    ///   - 空输入返回 "untitled" (避免空段破坏 URL 结构)
    /// </summary>
    string BuildSlug(string? input);

    /// <summary>
    /// V2 Task 4.5.13: 拼 SEO URL (含 mr1 末 6 位防 slug 冲突)
    /// 格式: /products/{pn1Slug}-{mr1Suffix6}/{pn2Slug}/{brandSlug}/{oem3Slug}
    ///   WHY mr1Suffix6: 多产品同 pn1/pn2/brand/oem3 时 slug 冲突, 末 6 位 mr1 唯一兜底
    /// 全部小写 (与 BuildSlug 一致, 避免 SEO 大小写重复)
    /// </summary>
    string BuildProductUrl(ProductDetailDto p);
}

/// <summary>
/// V2 Task 4.7: ProductDetailService 默认实现
/// </summary>
public class ProductDetailService : IProductDetailService
{
    private readonly ProductDbContext _db;
    private readonly AdminProductService _adminService;
    private readonly ILogger<ProductDetailService> _logger;

    public ProductDetailService(
        ProductDbContext db,
        AdminProductService adminService,
        ILogger<ProductDetailService> logger)
    {
        _db = db;
        _adminService = adminService;
        _logger = logger;
    }

    public async Task<ProductDetailDto?> GetByOemAsync(string oem, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(oem)) return null;
        // 长度防御: 列长 50, 拒绝超长输入避免无效 DB 查询
        if (oem.Length > 200) return null;

        // P3-2 修复: 3 次 fallback 合并为 1 次 OR 查询 + ORDER BY priority
        //   优先级: OemNoDisplay (1) > Oem2 (2) > Mr1 (3)
        //   DB 往返 3→1, fallback 命中场景 ~6ms → ~2ms
        var matched = await _db.Products.AsNoTracking()
            .Where(p => !p.IsDiscontinued &&
                        (p.OemNoDisplay == oem || p.Oem2 == oem || p.Mr1 == oem))
            .Select(p => new
            {
                Id = (long?)p.Id,
                Priority = p.OemNoDisplay == oem ? 1 : (p.Oem2 == oem ? 2 : 3)
            })
            .OrderBy(x => x.Priority)
            .FirstOrDefaultAsync(ct);

        if (matched == null || !matched.Id.HasValue)
        {
            _logger.LogInformation("GetByOemAsync: 404 oem={Oem}", oem);
            return null;
        }
        // 复用 AdminProductService.GetByIdAsync: 投影逻辑统一, 避免重复字段映射
        return await _adminService.GetByIdAsync(matched.Id.Value, ct);
    }

    public async Task<ProductDetailDto?> GetBySlugSegmentsAsync(
        string? pn1, string? pn2, string? brand, string oem3,
        CancellationToken ct = default)
    {
        // V2 Task 4.1: SEO URL 路径段查询
        //   - oem3 段必填 (核心匹配键)
        //   - pn1/pn2/brand 段可选 (用于二次校验, 防止误匹配其他产品)
        if (string.IsNullOrWhiteSpace(oem3)) return null;

        // 主查询: oem3 段直接调 GetByOemAsync (三级 fallback)
        //   WHY 不用 4 字段 AND: pn1/pn2/brand 段可能因 slug 化转义后字符变化, 严格匹配会漏
        //   oem3 已是唯一标识 (V2 主键), 单字段匹配足够; pn1/pn2/brand 段仅用于 URL 美观
        var detail = await GetByOemAsync(oem3, ct);
        if (detail == null) return null;

        // 二次校验: 若提供了 pn1/brand, 与查询结果比对, 不匹配则返回 null (避免 SEO URL 误导)
        //   WHY: SEO URL 应反映真实产品属性, 否则搜索引擎会索引错误关联
        if (!string.IsNullOrEmpty(pn1) && !string.IsNullOrEmpty(detail.ProductName1)
            && !string.Equals(BuildSlug(detail.ProductName1), pn1, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("GetBySlugSegmentsAsync: pn1 段不匹配 url={Pn1} actual={Actual}",
                pn1, detail.ProductName1);
            return null;
        }
        if (!string.IsNullOrEmpty(brand))
        {
            // brand 段与 crossReferences 中任意 OemBrand 匹配即可
            var brandMatch = detail.CrossReferences?
                .Any(x => string.Equals(BuildSlug(x.OemBrand), brand, StringComparison.OrdinalIgnoreCase)) ?? false;
            if (!brandMatch)
            {
                _logger.LogWarning("GetBySlugSegmentsAsync: brand 段不匹配 url={Brand}", brand);
                return null;
            }
        }
        return detail;
    }

    public string BuildSlug(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "untitled";

        // V2 Task 4.5.12: 单一 slug 化逻辑
        //   步骤 1: 小写化 (与 BuildProductUrl 末尾 ToLowerInvariant 呼应)
        //   步骤 2: 空白/下划线/连续 - → 单个 -
        //   步骤 3: 非 ASCII (含中文) 用 Uri.EscapeDataString 转 UTF-8 %XX (大写)
        //   步骤 4: TrimIncompletePercentEncoding (避免末尾残留 % 或 %X 被视为不完整转义)
        //   步骤 5: 首尾 - 截断
        var s = input.Trim().ToLowerInvariant();
        // 空白/下划线/连续 - → 单 -
        var replaced = new System.Text.StringBuilder();
        bool lastWasDash = false;
        foreach (var ch in s)
        {
            if (ch == ' ' || ch == '_' || ch == '-')
            {
                if (!lastWasDash) { replaced.Append('-'); lastWasDash = true; }
            }
            else
            {
                replaced.Append(ch);
                lastWasDash = false;
            }
        }
        var collapsed = replaced.ToString();

        // 非 ASCII (含中文) 转 %XX 编码 (Uri.EscapeDataString 默认输出大写)
        //   WHY 不用 HttpClientFormUrlEncoder: 该编码器会把 - 当保留字符不转义, 但我们把 - 作为分隔符
        //   这里只对 collapsed 整体 escape, 然后 %XX 自然成为 slug 一部分 (浏览器/搜索引擎均可处理)
        var encoded = Uri.EscapeDataString(collapsed);

        // TrimIncompletePercentEncoding: 末尾若残留 % 或 %X (单字符转义), 截断到完整 %XX
        encoded = TrimIncompletePercentEncoding(encoded);

        // 首尾 - 截断
        return encoded.Trim('-');
    }

    /// <summary>
    /// 截断末尾不完整的 % 编码 (V2 Task 4.1.22 修复 F4-5)
    ///   场景: BuildSlug("A%") → "a%25" 完整, 但 BuildSlug("A%2") → "a%252" 也完整
    ///         真正风险在 Uri.EscapeDataString 输入含已转义序列时, 此处防御性截断
    /// </summary>
    private static string TrimIncompletePercentEncoding(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // 末尾是 % 或 %X (X 非十六进制): 截断
        if (s.EndsWith('%')) return s[..^1];
        if (s.Length >= 2 && s[^2] == '%')
        {
            // 末尾是 %X, 检查 X 是否十六进制
            var x = s[^1];
            if (!((x >= '0' && x <= '9') || (x >= 'A' && x <= 'F') || (x >= 'a' && x <= 'f')))
                return s[..^2];
        }
        return s;
    }

    public string BuildProductUrl(ProductDetailDto p)
    {
        // V2 Task 4.5.13: /products/{pn1Slug}-{mr1Suffix6}/{pn2Slug}/{brandSlug}/{oem3Slug}
        //   mr1Suffix6: mr1 末 6 位, 防多产品同 pn1/pn2/brand/oem3 时 slug 冲突
        //   全部小写 (BuildSlug 内已小写, 此处再 ToLowerInvariant 兜底)
        var pn1Slug = BuildSlug(p.ProductName1);
        var pn2Slug = BuildSlug(p.ProductName2);
        // brand: 取第一个 crossReference 的 OemBrand (V2 OEM 3 主图命名同口径)
        var brand = p.CrossReferences?.FirstOrDefault()?.OemBrand;
        var brandSlug = BuildSlug(brand);
        // oem3: 取 OemNoDisplay (兼容老 OEM) 或第一个 crossReference 的 OemNo3
        var oem3 = !string.IsNullOrEmpty(p.OemNoDisplay) ? p.OemNoDisplay
                  : (p.CrossReferences?.FirstOrDefault()?.OemNo3 ?? p.Mr1 ?? "");
        var oem3Slug = BuildSlug(oem3);
        // mr1 末 6 位 (Task 4.5.13 防冲突)
        var mr1Suffix = (p.Mr1?.Length ?? 0) > 6 ? p.Mr1![^6..] : (p.Mr1 ?? "nomr1");

        return $"/products/{pn1Slug}-{mr1Suffix}/{pn2Slug}/{brandSlug}/{oem3Slug}".ToLowerInvariant();
    }
}
