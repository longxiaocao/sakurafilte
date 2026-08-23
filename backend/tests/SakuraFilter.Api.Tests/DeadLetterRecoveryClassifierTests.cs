using FluentAssertions;
using SakuraFilter.Api.Services;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// v30-15: DeadLetterRecoveryClassifier 纯函数单元测试
///
/// 测试目标 (覆盖: v30-15 提取自 DeadLetterRecoveryService 的纯函数):
///   - IsTransientError: 12 个瞬时错误关键词判断 (可自动恢复)
///   - ParseSettings: 死信恢复配置解析 (4 项: poll/cooling/max/batch, 带默认值兜底)
///
/// WHY 单测: 瞬时错误判断错误会导致死信漏恢复 (本可自动恢复却留死信) 或误恢复 (非瞬时错误如 schema 错也重试)
///   原逻辑混在 DeadLetterRecoveryService.RecoverInternalAsync 的 IQueryable Where 表达式树中无法直接测,
///   v30-15 提取到 public 静态类, 既可单测又保持原 IQueryable 行为不变 (EF Core 翻译 SQL)
/// </summary>
public class DeadLetterRecoveryClassifierTests
{
    // ===== IsTransientError 测试 =====

    [Theory]
    // 覆盖: 12 个瞬时错误关键词 (与 DeadLetterRecoveryService.RecoverInternalAsync IQueryable Where 一致)
    [InlineData("ConnectionRefused to meili:7700", true)]
    [InlineData("Connection refused to meili:7700", true)]
    [InlineData("Search timeout after 1000ms", true)]
    [InlineData("Operation timed out", true)]
    [InlineData("Network unreachable", true)]
    [InlineData("Host unreachable", true)]
    [InlineData("HTTP 500 Internal Server Error", true)]
    [InlineData("HTTP 502 Bad Gateway", true)]
    [InlineData("HTTP 503 Service Unavailable", true)]
    [InlineData("HTTP 504 Gateway Timeout", true)]
    [InlineData("internal server error", true)]
    [InlineData("service unavailable", true)]
    public void IsTransientError_Matches_12_Keywords(string lastError, bool expected)
    {
        // 覆盖: 12 个瞬时错误关键词 (网络/超时/5xx/服务不可用)
        DeadLetterRecoveryClassifier.IsTransientError(lastError).Should().Be(expected);
    }

    [Theory]
    // 覆盖: 大小写不敏感 (ToLower)
    [InlineData("CONNECTIONREFUSED", true)]
    [InlineData("TIMEOUT", true)]
    [InlineData("Internal Server Error", true)]
    [InlineData("network UNREACHABLE", true)]
    public void IsTransientError_Case_Insensitive(string lastError, bool expected)
    {
        // 覆盖: 大小写不敏感 (与原 IQueryable Where 的 ToLower() 一致)
        DeadLetterRecoveryClassifier.IsTransientError(lastError).Should().Be(expected);
    }

    [Theory]
    // 覆盖: 非瞬时错误不匹配 (schema/约束/数据错等, 不应自动恢复)
    [InlineData("column \"oem_2\" does not exist")]
    [InlineData("duplicate key value violates unique constraint")]
    [InlineData("null value in column \"mr_1\" violates not-null constraint")]
    [InlineData("foreign key constraint failed")]
    [InlineData("malformed JSON at position 42")]
    [InlineData("unknown error occurred during processing")]
    [InlineData("business logic error: invalid state")]
    public void IsTransientError_NonTransient_Returns_False(string lastError)
    {
        // 覆盖: 非瞬时错误 (schema/约束/数据错) 不应自动恢复, 需人工介入
        DeadLetterRecoveryClassifier.IsTransientError(lastError).Should().BeFalse();
    }

    [Fact]
    public void IsTransientError_Null_Or_Empty_Returns_False()
    {
        // 覆盖: null/空字符串返回 false (无错误信息不应自动恢复)
        DeadLetterRecoveryClassifier.IsTransientError(null).Should().BeFalse();
        DeadLetterRecoveryClassifier.IsTransientError("").Should().BeFalse();
    }

    [Fact]
    public void IsTransientError_Whitespace_Only_Returns_False()
    {
        // 覆盖: 纯空白字符串返回 false (string.IsNullOrEmpty 不认为空白是空, 但 ToLower 后无关键词匹配)
        DeadLetterRecoveryClassifier.IsTransientError("   ").Should().BeFalse();
        DeadLetterRecoveryClassifier.IsTransientError("\t\n").Should().BeFalse();
    }

    // ===== ParseSettings 测试 =====

    [Fact]
    public void ParseSettings_Valid_Values_Returns_Parsed()
    {
        // 覆盖: 4 个配置项正常值解析
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.recovery_poll_minutes"] = "15",
            ["dead_letter.recovery_cooling_minutes"] = "30",
            ["dead_letter.recovery_max_count"] = "5",
            ["dead_letter.recovery_batch_size"] = "100",
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.PollMinutes.Should().Be(15);
        s.CoolingMinutes.Should().Be(30);
        s.MaxCount.Should().Be(5);
        s.BatchSize.Should().Be(100);
    }

    [Fact]
    public void ParseSettings_Missing_Keys_Returns_Defaults()
    {
        // 覆盖: 4 个 key 全部缺失时返回默认值 (5/10/3/50)
        //   场景: 首次启动 system_settings 未初始化, 或运维未配置
        var settings = new Dictionary<string, string?>();

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.PollMinutes.Should().Be(5);
        s.CoolingMinutes.Should().Be(10);
        s.MaxCount.Should().Be(3);
        s.BatchSize.Should().Be(50);
    }

    [Theory]
    // 覆盖: 无效值 (空/null/非数字/0/负数) 返回默认值
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("  ")]
    public void ParseSettings_Invalid_PollMinutes_Returns_Default(string invalidValue)
    {
        // 覆盖: poll_minutes 无效值返回默认 5 (int.TryParse 失败或 n <= 0)
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.recovery_poll_minutes"] = invalidValue,
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.PollMinutes.Should().Be(5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("0")]
    [InlineData("-10")]
    public void ParseSettings_Invalid_CoolingMinutes_Returns_Default(string invalidValue)
    {
        // 覆盖: cooling_minutes 无效值返回默认 10
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.recovery_cooling_minutes"] = invalidValue,
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.CoolingMinutes.Should().Be(10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not_a_number")]
    [InlineData("0")]
    [InlineData("-5")]
    public void ParseSettings_Invalid_MaxCount_Returns_Default(string invalidValue)
    {
        // 覆盖: max_count 无效值返回默认 3
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.recovery_max_count"] = invalidValue,
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.MaxCount.Should().Be(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("0")]
    [InlineData("-100")]
    public void ParseSettings_Invalid_BatchSize_Returns_Default(string invalidValue)
    {
        // 覆盖: batch_size 无效值返回默认 50
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.recovery_batch_size"] = invalidValue,
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.BatchSize.Should().Be(50);
    }

    [Fact]
    public void ParseSettings_Partial_Keys_Mixes_Parsed_And_Defaults()
    {
        // 覆盖: 部分 key 存在部分缺失, 存在的用解析值, 缺失的用默认值
        //   场景: 运维只配置了 poll_minutes 和 max_count, 其他用默认
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.recovery_poll_minutes"] = "2",
            ["dead_letter.recovery_max_count"] = "10",
            // cooling_minutes + batch_size 缺失
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.PollMinutes.Should().Be(2);
        s.CoolingMinutes.Should().Be(10);  // 默认
        s.MaxCount.Should().Be(10);
        s.BatchSize.Should().Be(50);  // 默认
    }

    [Fact]
    public void ParseSettings_Large_Values_Accepted()
    {
        // 覆盖: 大数值 (边界值) 应被接受 (int.TryParse 范围内)
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.recovery_poll_minutes"] = "1440",  // 1 天
            ["dead_letter.recovery_cooling_minutes"] = "10080",  // 1 周
            ["dead_letter.recovery_max_count"] = "1000",
            ["dead_letter.recovery_batch_size"] = "10000",
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.PollMinutes.Should().Be(1440);
        s.CoolingMinutes.Should().Be(10080);
        s.MaxCount.Should().Be(1000);
        s.BatchSize.Should().Be(10000);
    }

    [Fact]
    public void ParseSettings_Ignores_Unrelated_Keys()
    {
        // 覆盖: 字典中含其他 key (如 auto_recovery_enabled) 不影响解析
        var settings = new Dictionary<string, string?>
        {
            ["dead_letter.auto_recovery_enabled"] = "true",
            ["dead_letter.recovery_poll_minutes"] = "7",
            ["other.unrelated.key"] = "999",
            ["perf.alert.p95_warn_ms"] = "1000",
        };

        var s = DeadLetterRecoveryClassifier.ParseSettings(settings);
        s.PollMinutes.Should().Be(7);
        s.CoolingMinutes.Should().Be(10);  // 默认
        s.MaxCount.Should().Be(3);  // 默认
        s.BatchSize.Should().Be(50);  // 默认
    }
}
