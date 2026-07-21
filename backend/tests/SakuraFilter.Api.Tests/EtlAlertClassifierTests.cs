using FluentAssertions;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Entities;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// v30-12: EtlAlertClassifier 纯函数单元测试
///
/// 测试目标 (覆盖: v30-12 提取自 EtlAlertService 的 4 个纯函数):
///   - ClassifySeverity: P0/P1/P2 严重度分类 (13 个 P0 关键词 + 10 个 P1 关键词 + 空/P2 兜底)
///   - FirstNonEmpty: webhook URL 4 候选优先级选择
///   - BuildSuppressionKey: 告警抑制 key (entity_type|error_class 前 50 字符)
///   - BuildPayload: webhook JSON payload 结构 + 字段映射
///
/// WHY 单测: 告警路由错误会让 P0 故障漏报或 P2 噪音刷屏
///   原为 EtlAlertService private static 方法无法直接测, v30-12 提取到 public 静态类
/// </summary>
public class EtlAlertClassifierTests
{
    // ===== ClassifySeverity 测试 =====

    [Theory]
    // 覆盖: P0 关键词 13 个 (ConnectionRefused/timeout/5xx/network/dns 等)
    [InlineData("ConnectionRefused to meili:7700", "P0")]
    [InlineData("Connection refused to meili:7700", "P0")]
    [InlineData("Search timeout after 1000ms", "P0")]
    [InlineData("Operation timed out", "P0")]
    [InlineData("HTTP 500 Internal Server Error", "P0")]
    [InlineData("HTTP 502 Bad Gateway", "P0")]
    [InlineData("HTTP 503 Service Unavailable", "P0")]
    [InlineData("HTTP 504 Gateway Timeout", "P0")]
    [InlineData("internal server error", "P0")]
    [InlineData("Network unreachable", "P0")]
    [InlineData("DNS resolution failed", "P0")]
    [InlineData("Host unreachable", "P0")]
    // 覆盖: P0 关键词大小写不敏感 (ToLowerInvariant)
    [InlineData("CONNECTIONREFUSED", "P0")]
    [InlineData("TIMEOUT", "P0")]
    public void ClassifySeverity_P0_Keywords_Returns_P0(string lastError, string expected)
    {
        // 覆盖: P0 严重度关键词 (Meili/网络/服务可用性)
        var item = MakeLog(lastError);
        EtlAlertClassifier.ClassifySeverity(item).Should().Be(expected);
    }

    [Theory]
    // 覆盖: P1 关键词 10 个 (column/schema/malformed/invalid/null/constraint/duplicate/violates/type/cast)
    [InlineData("column \"oem_2\" does not exist", "P1")]
    [InlineData("schema mismatch: expected products", "P1")]
    [InlineData("malformed JSON at position 42", "P1")]
    [InlineData("invalid input syntax for type uuid", "P1")]
    [InlineData("null value in column \"mr_1\" violates not-null constraint", "P1")]
    [InlineData("foreign key constraint failed", "P1")]
    [InlineData("duplicate key value violates unique constraint", "P1")]
    [InlineData("violates not-null constraint", "P1")]
    [InlineData("type mismatch: expected int", "P1")]
    [InlineData("cast failed: string to int", "P1")]
    public void ClassifySeverity_P1_Keywords_Returns_P1(string lastError, string expected)
    {
        // 覆盖: P1 严重度关键词 (数据 schema/列名/字段错)
        var item = MakeLog(lastError);
        EtlAlertClassifier.ClassifySeverity(item).Should().Be(expected);
    }

    [Fact]
    public void ClassifySeverity_P2_When_LastError_Empty()
    {
        // 覆盖: LastError 为 null 或空字符串时返回 P2 (兜底)
        EtlAlertClassifier.ClassifySeverity(MakeLog(null!)).Should().Be("P2");
        EtlAlertClassifier.ClassifySeverity(MakeLog("")).Should().Be("P2");
    }

    [Fact]
    public void ClassifySeverity_P2_When_No_Keyword_Match()
    {
        // 覆盖: 无关键词匹配时返回 P2 (兜底)
        var item = MakeLog("unknown error occurred during processing");
        EtlAlertClassifier.ClassifySeverity(item).Should().Be("P2");
    }

    [Fact]
    public void ClassifySeverity_P0_Takes_Precedence_Over_P1()
    {
        // 覆盖: 同时含 P0+P1 关键词时返回 P0 (P0 优先, Meili 故障比 schema 错更严重)
        var item = MakeLog("ConnectionRefused while writing to column oem_2");
        EtlAlertClassifier.ClassifySeverity(item).Should().Be("P0");
    }

    // ===== FirstNonEmpty 测试 =====

    [Fact]
    public void FirstNonEmpty_Returns_First_Non_Null_Or_Whitespace()
    {
        // 覆盖: 4 候选按优先级选第一个非空
        EtlAlertClassifier.FirstNonEmpty("a", "b", "c", "d").Should().Be("a");
        EtlAlertClassifier.FirstNonEmpty("", "b", "c", "d").Should().Be("b");
        EtlAlertClassifier.FirstNonEmpty(null!, " ", "c", "d").Should().Be("c");
        EtlAlertClassifier.FirstNonEmpty("", "  ", "", "d").Should().Be("d");
    }

    [Fact]
    public void FirstNonEmpty_Returns_Empty_When_All_Empty()
    {
        // 覆盖: 所有候选都为空时返回空字符串 (调用方需处理)
        EtlAlertClassifier.FirstNonEmpty().Should().Be("");
        EtlAlertClassifier.FirstNonEmpty("").Should().Be("");
        EtlAlertClassifier.FirstNonEmpty("", "  ", null!).Should().Be("");
    }

    [Fact]
    public void FirstNonEmpty_Handles_Single_Candidate()
    {
        // 覆盖: 单候选场景
        EtlAlertClassifier.FirstNonEmpty("only").Should().Be("only");
        EtlAlertClassifier.FirstNonEmpty("").Should().Be("");
    }

    // ===== BuildSuppressionKey 测试 =====

    [Fact]
    public void BuildSuppressionKey_Combines_EntityType_And_ErrorClass()
    {
        // 覆盖: key 格式 = "entity_type|error_class"
        var item = MakeLog("Meili ConnectionRefused", entityType: "products");
        EtlAlertClassifier.BuildSuppressionKey(item).Should().Be("products|Meili ConnectionRefused");
    }

    [Fact]
    public void BuildSuppressionKey_Truncates_Error_To_50_Chars()
    {
        // 覆盖: LastError > 50 字符时截断到 50 字符 (避免 key 过长 Dictionary 性能退化)
        var longError = new string('x', 100);
        var item = MakeLog(longError, entityType: "xrefs");
        var key = EtlAlertClassifier.BuildSuppressionKey(item);
        key.Should().Be($"xrefs|{new string('x', 50)}");
        key.Length.Should().BeLessThanOrEqualTo(56);  // "xrefs|" (6) + 50
    }

    [Fact]
    public void BuildSuppressionKey_Handles_Null_LastError()
    {
        // 覆盖: LastError 为 null 时 error_class 为空字符串 (不抛 NullReferenceException)
        var item = MakeLog(null!, entityType: "apps");
        EtlAlertClassifier.BuildSuppressionKey(item).Should().Be("apps|");
    }

    [Fact]
    public void BuildSuppressionKey_Same_Error_Same_Key()
    {
        // 覆盖: 同 entity_type + 同 error 生成相同 key (抑制窗口内不重发)
        var item1 = MakeLog("Meili timeout", entityType: "products");
        var item2 = MakeLog("Meili timeout", entityType: "products");
        EtlAlertClassifier.BuildSuppressionKey(item1)
            .Should().Be(EtlAlertClassifier.BuildSuppressionKey(item2));
    }

    // ===== BuildPayload 测试 =====

    [Fact]
    public void BuildPayload_Includes_Required_Fields()
    {
        // 覆盖: payload 必须包含 event/timestamp/etl/text 4 个顶层字段
        var item = MakeLog("test error", entityType: "products", mode: "full-load",
            filePath: "/data/products.xlsx", id: 42);
        var payload = EtlAlertClassifier.BuildPayload(item);

        // 用反射验证 anonymous object 字段 (匿名类型字段编译时确定)
        var t = payload.GetType();
        t.GetProperty("event")!.GetValue(payload).Should().Be("etl.failed");
        t.GetProperty("timestamp")!.GetValue(payload).Should().NotBeNull();
        t.GetProperty("etl")!.GetValue(payload).Should().NotBeNull();
        t.GetProperty("text")!.GetValue(payload).Should().NotBeNull();
    }

    [Fact]
    public void BuildPayload_Etl_Nested_Object_Maps_All_Fields()
    {
        // 覆盖: etl 嵌套对象必须映射 EtlProgressLog 全部字段 (含 Day 9.5 cancel_reason/reason_code)
        var item = MakeLog("test error", entityType: "products", mode: "full-load",
            filePath: "/data/products.xlsx", id: 42,
            cancelReason: "user_cancelled", reasonCode: "INCR_DUPLICATE");
        var payload = EtlAlertClassifier.BuildPayload(item);
        var etl = payload.GetType().GetProperty("etl")!.GetValue(payload)!;
        var t = etl.GetType();

        // 验证字段存在且值映射正确
        t.GetProperty("id")!.GetValue(etl).Should().Be(42L);
        t.GetProperty("entity_type")!.GetValue(etl).Should().Be("products");
        t.GetProperty("mode")!.GetValue(etl).Should().Be("full-load");
        t.GetProperty("file_path")!.GetValue(etl).Should().Be("/data/products.xlsx");
        t.GetProperty("last_error")!.GetValue(etl).Should().Be("test error");
        t.GetProperty("cancel_reason")!.GetValue(etl).Should().Be("user_cancelled");
        t.GetProperty("reason_code")!.GetValue(etl).Should().Be("INCR_DUPLICATE");
        // cancelled_at 为 null 时不报错 (ToString("o") 应返回 null)
        t.GetProperty("cancelled_at")!.GetValue(etl).Should().BeNull();
    }

    [Fact]
    public void BuildPayload_Text_Truncates_Long_Error_To_120_Chars()
    {
        // 覆盖: text 字段 LastError 截断到 120 字符 (避免 webhook 文本过长)
        var longError = new string('y', 200);
        var item = MakeLog(longError, entityType: "products", mode: "full-load",
            filePath: "/data/products.xlsx");
        var payload = EtlAlertClassifier.BuildPayload(item);
        var text = (string)payload.GetType().GetProperty("text")!.GetValue(payload)!;
        text.Should().Contain("yyy");
        text.Length.Should().BeLessThan(200);  // 截断后小于原长度
    }

    [Fact]
    public void BuildPayload_Handles_Null_LastError_In_Text()
    {
        // 覆盖: LastError 为 null 时 text 字段不抛 NullReferenceException
        var item = MakeLog(null!, entityType: "products", mode: "full-load",
            filePath: "/data/products.xlsx");
        var payload = EtlAlertClassifier.BuildPayload(item);
        var text = (string)payload.GetType().GetProperty("text")!.GetValue(payload)!;
        text.Should().Contain("[ETL FAILED]");
        text.Should().Contain("err=");  // err= 后为空 (Substring 安全处理)
    }

    // ===== 辅助方法 =====

    /// <summary>
    /// 构造测试用 EtlProgressLog (默认值 + 可覆盖字段)
    /// </summary>
    private static EtlProgressLog MakeLog(
        string lastError,
        string entityType = "products",
        string mode = "full-load",
        string filePath = "/tmp/test.xlsx",
        long id = 1,
        string? cancelReason = null,
        string? reasonCode = null)
    {
        return new EtlProgressLog
        {
            Id = id,
            EntityType = entityType,
            Mode = mode,
            FilePath = filePath,
            ReadCount = 100,
            InsertedCount = 80,
            UpdatedCount = 15,
            SkippedCount = 5,
            SkippedMissingOem = 2,
            SkippedNullField = 1,
            SkippedDuplicate = 2,
            ErrorCount = 0,
            IndexedCount = 95,
            IndexPendingCount = 0,
            LastError = lastError,
            StartedAt = DateTime.UtcNow.AddSeconds(-30),
            FinishedAt = DateTime.UtcNow,
            DurationSec = 30,
            CancelReason = cancelReason,
            CancelledAt = null,
            ReasonCode = reasonCode,
        };
    }
}
