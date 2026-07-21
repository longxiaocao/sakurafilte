using FluentAssertions;
using SakuraFilter.Api.Services;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// v30-13: PerfAlertClassifier 纯函数单元测试
///
/// 测试目标 (覆盖: v30-13 提取自 PerfAlertService 的纯函数):
///   - ParseDouble: system_settings 字典解析 double (正常值 + 无效值 + 缺失 key + 默认值)
///   - BuildSuppressKey: 抑制 key 格式 (level|rule)
///   - IsSuppressed: 抑制窗口判断 (窗口内 true / 窗口外 false / 无记录 false)
///   - UpdateSuppression: 更新抑制状态 (写入 / 覆盖)
///
/// WHY 单测: 告警抑制错误会导致 P0 性能故障刷屏日志, 或漏报关键告警
///   原为 PerfAlertService private 方法/逻辑, v30-13 提取到 public 静态类
/// </summary>
public class PerfAlertClassifierTests
{
    // ===== ParseDouble 测试 =====

    [Fact]
    public void ParseDouble_Valid_Value_Returns_Parsed()
    {
        // 覆盖: 正常 double 字符串解析
        var settings = new Dictionary<string, string?>
        {
            ["perf.alert.p95_warn_ms"] = "1000",
            ["perf.alert.p95_error_ms"] = "3000.5",
            ["perf.alert.error_rate_pct"] = "5.25",
        };

        PerfAlertClassifier.ParseDouble(settings, "perf.alert.p95_warn_ms", 999).Should().Be(1000.0);
        PerfAlertClassifier.ParseDouble(settings, "perf.alert.p95_error_ms", 999).Should().Be(3000.5);
        PerfAlertClassifier.ParseDouble(settings, "perf.alert.error_rate_pct", 999).Should().Be(5.25);
    }

    [Fact]
    public void ParseDouble_Missing_Key_Returns_Default()
    {
        // 覆盖: key 不存在时返回默认值 (system_settings 未配置时兜底)
        var settings = new Dictionary<string, string?>();
        PerfAlertClassifier.ParseDouble(settings, "perf.alert.p95_warn_ms", 1000).Should().Be(1000.0);
        PerfAlertClassifier.ParseDouble(settings, "any.missing.key", 42.5).Should().Be(42.5);
    }

    [Theory]
    // 覆盖: 无效字符串 (空/null/非数字) 返回默认值
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("12.3.4")]
    [InlineData("0x100")]
    public void ParseDouble_Invalid_Value_Returns_Default(string? invalidValue)
    {
        var settings = new Dictionary<string, string?>
        {
            ["perf.alert.p95_warn_ms"] = invalidValue,
        };
        PerfAlertClassifier.ParseDouble(settings, "perf.alert.p95_warn_ms", 1000).Should().Be(1000.0);
    }

    [Fact]
    public void ParseDouble_Negative_And_Zero_Values()
    {
        // 覆盖: 负数 + 0 + 科学计数法 (都是合法 double)
        var settings = new Dictionary<string, string?>
        {
            ["neg"] = "-100.5",
            ["zero"] = "0",
            ["sci"] = "1e3",  // 1000
        };
        PerfAlertClassifier.ParseDouble(settings, "neg", 0).Should().Be(-100.5);
        PerfAlertClassifier.ParseDouble(settings, "zero", 0).Should().Be(0.0);
        PerfAlertClassifier.ParseDouble(settings, "sci", 0).Should().Be(1000.0);
    }

    // ===== BuildSuppressKey 测试 =====

    [Theory]
    [InlineData("ERROR", "p95_error", "ERROR|p95_error")]
    [InlineData("WARN", "p95_warn", "WARN|p95_warn")]
    [InlineData("ERROR", "error_rate", "ERROR|error_rate")]
    [InlineData("ERROR", "max_ms", "ERROR|max_ms")]
    public void BuildSuppressKey_Combines_Level_And_Rule(string level, string rule, string expected)
    {
        // 覆盖: key 格式 = "level|rule"
        PerfAlertClassifier.BuildSuppressKey(level, rule).Should().Be(expected);
    }

    // ===== IsSuppressed 测试 =====

    [Fact]
    public void IsSuppressed_No_Record_Returns_False()
    {
        // 覆盖: 无历史记录时不抑制 (首次告警允许发送)
        var suppressedKeys = new Dictionary<string, DateTime>();
        PerfAlertClassifier.IsSuppressed(suppressedKeys, "ERROR", "p95_error", DateTime.UtcNow, TimeSpan.FromMinutes(5))
            .Should().BeFalse();
    }

    [Fact]
    public void IsSuppressed_Within_Window_Returns_True()
    {
        // 覆盖: 在抑制窗口内 (5min) 返回 true, 避免刷屏
        var now = DateTime.UtcNow;
        var suppressedKeys = new Dictionary<string, DateTime>
        {
            ["ERROR|p95_error"] = now.AddSeconds(-30),  // 30s 前告警过
        };
        PerfAlertClassifier.IsSuppressed(suppressedKeys, "ERROR", "p95_error", now, TimeSpan.FromMinutes(5))
            .Should().BeTrue();
    }

    [Fact]
    public void IsSuppressed_Outside_Window_Returns_False()
    {
        // 覆盖: 超过抑制窗口 (5min) 返回 false, 允许再次发送
        var now = DateTime.UtcNow;
        var suppressedKeys = new Dictionary<string, DateTime>
        {
            ["ERROR|p95_error"] = now.AddMinutes(-6),  // 6min 前告警过 (超 5min 窗口)
        };
        PerfAlertClassifier.IsSuppressed(suppressedKeys, "ERROR", "p95_error", now, TimeSpan.FromMinutes(5))
            .Should().BeFalse();
    }

    [Fact]
    public void IsSuppressed_Different_Rule_Not_Suppressed()
    {
        // 覆盖: 同 level 不同 rule 不互相抑制
        //   WHY: P95_ERROR 和 error_rate 是独立规则, 不应互相抑制
        var now = DateTime.UtcNow;
        var suppressedKeys = new Dictionary<string, DateTime>
        {
            ["ERROR|p95_error"] = now,  // 刚告警过 p95_error
        };
        PerfAlertClassifier.IsSuppressed(suppressedKeys, "ERROR", "error_rate", now, TimeSpan.FromMinutes(5))
            .Should().BeFalse();  // error_rate 未告警过, 不抑制
    }

    [Fact]
    public void IsSuppressed_Different_Level_Not_Suppressed()
    {
        // 覆盖: 同 rule 不同 level 不互相抑制
        //   WHY: WARN 和 ERROR 是独立级别, P95 WARN 不应抑制 P95 ERROR
        var now = DateTime.UtcNow;
        var suppressedKeys = new Dictionary<string, DateTime>
        {
            ["WARN|p95_warn"] = now,
        };
        PerfAlertClassifier.IsSuppressed(suppressedKeys, "ERROR", "p95_warn", now, TimeSpan.FromMinutes(5))
            .Should().BeFalse();
    }

    // ===== UpdateSuppression 测试 =====

    [Fact]
    public void UpdateSuppression_Writes_New_Record()
    {
        // 覆盖: 首次写入抑制状态
        var suppressedKeys = new Dictionary<string, DateTime>();
        var now = DateTime.UtcNow;
        PerfAlertClassifier.UpdateSuppression(suppressedKeys, "ERROR", "p95_error", now);
        suppressedKeys.Should().ContainKey("ERROR|p95_error");
        suppressedKeys["ERROR|p95_error"].Should().Be(now);
    }

    [Fact]
    public void UpdateSuppression_Overwrites_Existing_Record()
    {
        // 覆盖: 已有记录时覆盖 (刷新抑制窗口起点)
        var oldTime = DateTime.UtcNow.AddMinutes(-3);
        var newTime = DateTime.UtcNow;
        var suppressedKeys = new Dictionary<string, DateTime>
        {
            ["ERROR|p95_error"] = oldTime,
        };
        PerfAlertClassifier.UpdateSuppression(suppressedKeys, "ERROR", "p95_error", newTime);
        suppressedKeys["ERROR|p95_error"].Should().Be(newTime, "新告警应刷新抑制窗口起点");
    }

    [Fact]
    public void UpdateSuppression_Does_Not_Affect_Other_Keys()
    {
        // 覆盖: 更新一个 key 不影响其他 key
        var now = DateTime.UtcNow;
        var suppressedKeys = new Dictionary<string, DateTime>
        {
            ["ERROR|p95_error"] = now,
            ["WARN|p95_warn"] = now,
        };
        PerfAlertClassifier.UpdateSuppression(suppressedKeys, "ERROR", "max_ms", now);
        suppressedKeys.Should().HaveCount(3);
        suppressedKeys.Should().ContainKey("ERROR|p95_error");
        suppressedKeys.Should().ContainKey("WARN|p95_warn");
        suppressedKeys.Should().ContainKey("ERROR|max_ms");
    }
}
