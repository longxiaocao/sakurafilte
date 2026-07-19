using Microsoft.Extensions.Logging.Abstractions;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;
using Xunit;

namespace SakuraFilter.Api.Tests.Integration;

/// <summary>
/// V24-F94 (v28-2): PostgresSearchProvider 集成测试
///
/// 覆盖目标:
///   - CTE UNION 拆分 SQL 语义正确性 (vs baseline OR + EXISTS)
///   - 单 token q 命中 products 5 字段 / xref 3 字段 / machine 2 字段
///   - 多 token q 用 INTERSECT 取交集 (类似 Meili matchingStrategy='all')
///   - type / d1-h3 / d7-d8 / includeDiscontinued / machineCategory 过滤
///   - AggregateSearchAsync 聚合搜索 + OEM 3 列表 + 机型列表 JSON 解析
///
/// 依赖:
///   - 本地 PG (sakurafilter_int_tests 库, 由 PgIntegrationTestBase 重置)
///   - GIN trgm 索引 (可选, 无索引时 SQL 仍能跑, 仅性能差)
///
/// 关联 spec: 28.2 (v28-2 CTE UNION 拆分 + GIN trgm 索引)
/// </summary>
[Collection("PgSequential")]
[Trait("Category", "Integration")]
public class PostgresSearchProviderIntegrationTests : PgIntegrationTestBase
{
    // WHY 位置参数构造: SearchRequest 是 record, 不能用对象初始化器

    [Fact]
    public async Task SearchAsync_NoQ_ReturnsAllPublishedNotDiscontinuedProducts()
    {
        // 覆盖: 无 q 时走 base filter, 不生成 q_match CTE
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: null, Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        // 3 个上架未停产产品 (Product1/2/3), Product4 已下架被排除
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Items.Count());
    }

    [Fact]
    public async Task SearchAsync_SingleToken_MatchProductName1()
    {
        // 覆盖: 单 token q 走 q_match CTE UNION, 命中 products.product_name_1
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: "Alpha", Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        Assert.Equal(1, result.Total);
        Assert.Contains(result.Items, i => i.OemNoDisplay == "MR10001");
    }

    [Fact]
    public async Task SearchAsync_SingleToken_MatchXrefOemBrand()
    {
        // 覆盖: 单 token q 走 q_match CTE UNION, 命中 cross_references.oem_brand
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: "Bosch", Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        // Product1 的 xref.oem_brand='Bosch' 命中 (Product4 已下架被排除)
        Assert.Equal(1, result.Total);
        Assert.Contains(result.Items, i => i.OemNoDisplay == "MR10001");
    }

    [Fact]
    public async Task SearchAsync_SingleToken_MatchMachineBrand()
    {
        // 覆盖: 单 token q 走 q_match CTE UNION, 命中 machine_applications.machine_brand
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: "Caterpillar", Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        // Product1 的 machine.machine_brand='Caterpillar' 命中 (Product4 已下架被排除)
        Assert.Equal(1, result.Total);
        Assert.Contains(result.Items, i => i.OemNoDisplay == "MR10001");
    }

    [Fact]
    public async Task SearchAsync_MultiToken_IntersectMatch()
    {
        // 覆盖: 多 token q 用 INTERSECT 取交集 (类似 Meili matchingStrategy='all')
        //   q='Alpha Bosch' → 两个 token 都需命中同一 product_id
        //   Product1: product_name_1='Alpha Filter' 命中 'Alpha', xref.oem_brand='Bosch' 命中 'Bosch' → 交集命中
        //   Product2/3: 只命中一个 token, 不在交集
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: "Alpha Bosch", Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        Assert.Equal(1, result.Total);
        Assert.Contains(result.Items, i => i.OemNoDisplay == "MR10001");
    }

    [Fact]
    public async Task SearchAsync_TypeFilter_FiltersByType()
    {
        // 覆盖: type 过滤走 base filter (p.type = @type)
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: null, Type: "bearing",
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        // Product1 (bearing) + Product3 (bearing), Product2 是 filter, Product4 已下架
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task SearchAsync_IncludeDiscontinued_ReturnsDiscontinuedProducts()
    {
        // 覆盖: includeDiscontinued=true 移除 is_discontinued = false 过滤
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: null, Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: true,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        // Product1/2/3 + Product4 (已下架)
        Assert.Equal(4, result.Total);
    }

    [Fact]
    public async Task SearchAsync_DimensionFilter_FiltersByD1WithTolerance()
    {
        // 覆盖: d1 尺寸范围 filter (±tolerance)
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        // d1=50, tolerance=0 → 只命中 Product2 (d1_mm=50)
        var req = new SearchRequest(
            Q: null, Type: null,
            D1: 50m, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 0m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        Assert.Equal(1, result.Total);
        Assert.Contains(result.Items, i => i.OemNoDisplay == "MR10002");
    }

    [Fact]
    public async Task SearchAsync_Pagination_RespectsPageAndPageSize()
    {
        // 覆盖: 分页 LIMIT/OFFSET
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new SearchRequest(
            Q: null, Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 2);
        var result = await provider.SearchAsync(req);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Items.Count());
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task AggregateSearchAsync_NoQ_ReturnsAllWithOemListAndMachineList()
    {
        // 覆盖: AggregateSearchAsync 聚合搜索 + OEM 3 列表 + 机型列表 JSON 解析
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new AggregateSearchRequest(
            Q: null, Page: 1, PageSize: 100,
            Tolerance: 5m, IncludeDiscontinued: false,
            MachineCategory: null, Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null);
        var result = await provider.AggregateSearchAsync(req);

        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Hits.Count);
        // Product1 应有 1 个 OEM 3 + 1 个机型
        var p1 = result.Hits.First(h => h.Mr1 == "MR10001");
        Assert.Single(p1.OemList);
        Assert.Equal("Bosch", p1.OemList[0].OemBrand);
        Assert.Single(p1.MachineList);
        Assert.Equal("Caterpillar", p1.MachineList[0].MachineBrand);
    }

    [Fact]
    public async Task AggregateSearchAsync_MachineCategoryFilter_FiltersByCategory()
    {
        // 覆盖: 机型分类过滤 (聚合搜索独有, base filter 追加 EXISTS machine_category)
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        var req = new AggregateSearchRequest(
            Q: null, Page: 1, PageSize: 100,
            Tolerance: 5m, IncludeDiscontinued: false,
            MachineCategory: "construction", Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null);
        var result = await provider.AggregateSearchAsync(req);

        // 只 Product1 的 machine_category='construction' 命中 (Product4 已下架被排除)
        Assert.Equal(1, result.Total);
        Assert.Equal("MR10001", result.Hits[0].Mr1);
    }

    [Fact]
    public async Task SearchAsync_QWithSpecialCharacters_EscapesLikePattern()
    {
        // 覆盖: q 含 LIKE 特殊字符 (%, _, \) 时正确转义, 不破坏 SQL
        if (!IsEnabled) return;

        await using var db = CreateDbContext();
        await SeedTestDataAsync(db);
        var provider = new PostgresSearchProvider(db, NullLogger<PostgresSearchProvider>.Instance);

        // % 和 _ 是 LIKE 通配符, 应被转义为字面量
        var req = new SearchRequest(
            Q: "100%_spec", Type: null,
            D1: null, D2: null, D3: null,
            H1: null, H2: null, H3: null,
            D7Thread: null, D8Thread: null,
            Tolerance: 5m, IncludeDiscontinued: false,
            Page: 1, PageSize: 100);
        var result = await provider.SearchAsync(req);

        // 无匹配 (无产品含字面量 '100%_spec'), 但 SQL 应能正常执行不报错
        Assert.Equal(0, result.Total);
    }

    /// <summary>
    /// 种子测试数据: 4 个产品 (3 上架 + 1 下架), 每产品配 1 个 xref + 1 个 machine
    ///   Product1 (MR10001): bearing, Alpha Filter, xref Bosch, machine Caterpillar (construction)
    ///   Product2 (MR10002): filter, Beta Filter, d1_mm=50, xref Denso, machine Komatsu (industrial)
    ///   Product3 (MR10003): bearing, Gamma Filter, xref NGK, machine Hitachi (commercial)
    ///   Product4 (MR10004): 下架, Delta Filter, xref Bosch, machine Caterpillar
    /// </summary>
    private async Task SeedTestDataAsync(ProductDbContext db)
    {
        // WHY 显式列举字段: 避免新增字段时漏种子
        // WHY 不硬编码 ProductId: TRUNCATE RESTART IDENTITY 后实际 Id 由 PG 分配 (可能非 1-4)
        //   用 navigation property 关联, EF Core 自动设置 ProductId
        // WHY MR1 用纯字母数字 (无连字符): chk_mr_1_format 约束要求 1-10 位 [A-Za-z0-9]
        var p1 = new Product
        {
            Mr1 = "MR10001", OemNoDisplay = "MR10001", OemNoNormalized = "001",
            ProductName1 = "Alpha Filter", ProductName2 = "Alpha Premium", Oem2 = "OEM-A",
            Type = "bearing", IsPublished = true, IsDiscontinued = false,
            D1Mm = 10m, D2Mm = 20m, H1Mm = 5m,
            Remark = "Alpha series bearing", UpdatedAt = DateTime.UtcNow
        };
        var p2 = new Product
        {
            Mr1 = "MR10002", OemNoDisplay = "MR10002", OemNoNormalized = "002",
            ProductName1 = "Beta Filter", ProductName2 = "Beta Standard", Oem2 = "OEM-B",
            Type = "filter", IsPublished = true, IsDiscontinued = false,
            D1Mm = 50m, D2Mm = 60m, H1Mm = 15m,
            Remark = "Beta series filter", UpdatedAt = DateTime.UtcNow.AddSeconds(-1)
        };
        var p3 = new Product
        {
            Mr1 = "MR10003", OemNoDisplay = "MR10003", OemNoNormalized = "003",
            ProductName1 = "Gamma Filter", ProductName2 = "Gamma Plus", Oem2 = "OEM-C",
            Type = "bearing", IsPublished = true, IsDiscontinued = false,
            D1Mm = 30m, D2Mm = 40m, H1Mm = 10m,
            Remark = "Gamma series bearing", UpdatedAt = DateTime.UtcNow.AddSeconds(-2)
        };
        var p4 = new Product
        {
            Mr1 = "MR10004", OemNoDisplay = "MR10004", OemNoNormalized = "004",
            ProductName1 = "Delta Filter", ProductName2 = "Delta Plus", Oem2 = "OEM-D",
            Type = "bearing", IsPublished = true, IsDiscontinued = true,  // 下架
            D1Mm = 30m, D2Mm = 40m, H1Mm = 10m,
            Remark = "Delta series discontinued", UpdatedAt = DateTime.UtcNow.AddSeconds(-3)
        };
        db.Products.AddRange(p1, p2, p3, p4);

        // WHY 先 SaveChanges: 让 PG 分配 product.Id, 再用实际 Id 关联 xref / machine
        await db.SaveChangesAsync();

        db.CrossReferences.AddRange(
            new CrossReference
            {
                ProductId = p1.Id, OemBrand = "Bosch", OemNo3 = "BOSCH-001", Oem2 = "OEM-X",
                IsPublished = true, IsDiscontinued = false, SortOrder = 0
            },
            new CrossReference
            {
                ProductId = p2.Id, OemBrand = "Denso", OemNo3 = "DENSO-002", Oem2 = "OEM-Y",
                IsPublished = true, IsDiscontinued = false, SortOrder = 0
            },
            new CrossReference
            {
                ProductId = p3.Id, OemBrand = "NGK", OemNo3 = "NGK-003", Oem2 = "OEM-Z",
                IsPublished = true, IsDiscontinued = false, SortOrder = 0
            },
            new CrossReference
            {
                ProductId = p4.Id, OemBrand = "Bosch", OemNo3 = "BOSCH-004", Oem2 = "OEM-W",
                IsPublished = true, IsDiscontinued = false, SortOrder = 0
            }
        );

        db.MachineApplications.AddRange(
            new MachineApplication
            {
                ProductId = p1.Id, MachineBrand = "Caterpillar", MachineModel = "CAT-320",
                MachineCategory = "construction"
            },
            new MachineApplication
            {
                ProductId = p2.Id, MachineBrand = "Komatsu", MachineModel = "PC200",
                MachineCategory = "industrial"
            },
            new MachineApplication
            {
                ProductId = p3.Id, MachineBrand = "Hitachi", MachineModel = "ZX200",
                MachineCategory = "commercial"
            },
            new MachineApplication
            {
                ProductId = p4.Id, MachineBrand = "Caterpillar", MachineModel = "CAT-320",
                MachineCategory = "construction"
            }
        );

        await db.SaveChangesAsync();
    }
}
