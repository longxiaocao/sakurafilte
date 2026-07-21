using FluentAssertions;
using SakuraFilter.Api.Services;
using SakuraFilter.Search;
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

    // ===== v30-20: Meili 主路径告警纯函数 =====

    [Fact]
    public void ClassifyMeiliSeverity_P99Error_Returns_P0()
    {
        // 覆盖: meili_p99_error → P0 (Meili 主路径严重慢)
        PerfAlertClassifier.ClassifyMeiliSeverity(PerfAlertClassifier.MeiliRules.P99Error)
            .Should().Be("P0");
    }

    [Fact]
    public void ClassifyMeiliSeverity_P99Warn_Returns_P1()
    {
        // 覆盖: meili_p99_warn → P1 (提前预警)
        PerfAlertClassifier.ClassifyMeiliSeverity(PerfAlertClassifier.MeiliRules.P99Warn)
            .Should().Be("P1");
    }

    [Fact]
    public void ClassifyMeiliSeverity_FallbackRateError_Returns_P0()
    {
        // 覆盖: meili_fallback_rate_error → P0 (Meili 频繁不可用)
        PerfAlertClassifier.ClassifyMeiliSeverity(PerfAlertClassifier.MeiliRules.FallbackRateError)
            .Should().Be("P0");
    }

    [Fact]
    public void ClassifyMeiliSeverity_Unknown_Rule_Returns_P2_Fallback()
    {
        // 覆盖: 未知规则降级 P2 (兜底)
        PerfAlertClassifier.ClassifyMeiliSeverity("meili_unknown_rule")
            .Should().Be("P2");
    }

    [Fact]
    public void BuildMeiliAlertContext_Contains_All_Fields()
    {
        // 覆盖: 上下文包含所有 P99/P95/P50/FallbackRate 等关键字段, 供运维排查
        var snapshot = new MeiliSearchSnapshot(
            SampleCount: 1000,
            PrimarySuccessCount: 950,
            FallbackCount: 45,
            PrimaryErrorCount: 5,
            TotalSearchCount: 1000,
            FallbackRate: 4.5,
            PrimaryErrorRate: 0.5,
            P50Ms: 50.0,
            P95Ms: 200.0,
            P99Ms: 500.0,
            MaxMs: 2000.0,
            GeneratedAt: new DateTime(2026, 7, 22, 10, 30, 0, DateTimeKind.Utc)
        );

        var ctx = PerfAlertClassifier.BuildMeiliAlertContext(
            PerfAlertClassifier.MeiliRules.P99Error, snapshot, threshold: 1500);

        ctx.Should().ContainKey("rule");
        ctx["rule"].Should().Be(PerfAlertClassifier.MeiliRules.P99Error);
        ctx.Should().ContainKey("severity");
        ctx["severity"].Should().Be("P0");
        ctx.Should().ContainKey("threshold");
        ctx["threshold"].Should().Be(1500.0);
        ctx.Should().ContainKey("p99_ms");
        ctx["p99_ms"].Should().Be(500.0);
        ctx.Should().ContainKey("p95_ms");
        ctx["p95_ms"].Should().Be(200.0);
        ctx.Should().ContainKey("p50_ms");
        ctx["p50_ms"].Should().Be(50.0);
        ctx.Should().ContainKey("max_ms");
        ctx["max_ms"].Should().Be(2000.0);
        ctx.Should().ContainKey("fallback_rate_pct");
        ctx["fallback_rate_pct"].Should().Be(4.5);
        ctx.Should().ContainKey("primary_error_rate_pct");
        ctx["primary_error_rate_pct"].Should().Be(0.5);
        ctx.Should().ContainKey("sample_count");
        ctx["sample_count"].Should().Be(1000);
        ctx.Should().ContainKey("primary_success_count");
        ctx["primary_success_count"].Should().Be(950L);
        ctx.Should().ContainKey("fallback_count");
        ctx["fallback_count"].Should().Be(45L);
        ctx.Should().ContainKey("total_search_count");
        ctx["total_search_count"].Should().Be(1000L);
        ctx.Should().ContainKey("generated_at_utc");
        ctx["generated_at_utc"].Should().Be(snapshot.GeneratedAt);
    }

    [Fact]
    public void BuildMeiliAlertContext_Severity_Matches_Rule()
    {
        // 覆盖: 上下文中的 severity 与 ClassifyMeiliSeverity 一致
        var snapshot = new MeiliSearchSnapshot(
            SampleCount: 100, PrimarySuccessCount: 100, FallbackCount: 0, PrimaryErrorCount: 0,
            TotalSearchCount: 100, FallbackRate: 0, PrimaryErrorRate: 0,
            P50Ms: 50, P95Ms: 100, P99Ms: 150, MaxMs: 200,
            GeneratedAt: DateTime.UtcNow);

        var ctxWarn = PerfAlertClassifier.BuildMeiliAlertContext(
            PerfAlertClassifier.MeiliRules.P99Warn, snapshot, threshold: 100);
        ctxWarn["severity"].Should().Be("P1");

        var ctxFallback = PerfAlertClassifier.BuildMeiliAlertContext(
            PerfAlertClassifier.MeiliRules.FallbackRateError, snapshot, threshold: 20);
        ctxFallback["severity"].Should().Be("P0");
    }

    [Fact]
    public void BuildMeiliAlertMarkdown_Contains_Severity_And_Rule()
    {
        // 覆盖: Markdown 正文包含 severity 标记 + rule + 关键指标
        var snapshot = new MeiliSearchSnapshot(
            SampleCount: 500, PrimarySuccessCount: 450, FallbackCount: 50, PrimaryErrorCount: 0,
            TotalSearchCount: 500, FallbackRate: 10.0, PrimaryErrorRate: 0,
            P50Ms: 50, P95Ms: 300, P99Ms: 800, MaxMs: 1500,
            GeneratedAt: new DateTime(2026, 7, 22, 10, 30, 0, DateTimeKind.Utc));

        var md = PerfAlertClassifier.BuildMeiliAlertMarkdown(
            PerfAlertClassifier.MeiliRules.P99Error, snapshot, threshold: 1500);

        md.Should().Contain("[P0]");
        md.Should().Contain(PerfAlertClassifier.MeiliRules.P99Error);
        md.Should().Contain("P99=800");
        md.Should().Contain("P95=300");
        md.Should().Contain("P50=50");
        md.Should().Contain("降级率");
        md.Should().Contain("10");
        md.Should().Contain("样本数");
    }

    [Fact]
    public void BuildMeiliAlertMarkdown_P99Warn_Title_Contains_Warn()
    {
        // 覆盖: P99Warn 规则的 Markdown 标题含 WARN 标识
        var snapshot = new MeiliSearchSnapshot(
            SampleCount: 100, PrimarySuccessCount: 100, FallbackCount: 0, PrimaryErrorCount: 0,
            TotalSearchCount: 100, FallbackRate: 0, PrimaryErrorRate: 0,
            P50Ms: 50, P95Ms: 100, P99Ms: 600, MaxMs: 800,
            GeneratedAt: DateTime.UtcNow);

        var md = PerfAlertClassifier.BuildMeiliAlertMarkdown(
            PerfAlertClassifier.MeiliRules.P99Warn, snapshot, threshold: 500);

        md.Should().Contain("[P1]");
        md.Should().Contain("WARN");
    }

    [Fact]
    public void BuildMeiliAlertMarkdown_FallbackRate_Title_Contains_降级()
    {
        // 覆盖: FallbackRate 规则的 Markdown 标题含"频繁降级"
        var snapshot = new MeiliSearchSnapshot(
            SampleCount: 100, PrimarySuccessCount: 70, FallbackCount: 30, PrimaryErrorCount: 0,
            TotalSearchCount: 100, FallbackRate: 30.0, PrimaryErrorRate: 0,
            P50Ms: 50, P95Ms: 100, P99Ms: 200, MaxMs: 500,
            GeneratedAt: DateTime.UtcNow);

        var md = PerfAlertClassifier.BuildMeiliAlertMarkdown(
            PerfAlertClassifier.MeiliRules.FallbackRateError, snapshot, threshold: 20);

        md.Should().Contain("[P0]");
        md.Should().Contain("频繁降级");
    }

    [Fact]
    public void MeiliRules_Constants_Are_Stable()
    {
        // 覆盖: 规则名常量稳定 (用于 AlertCenter suppressKey 持久化, 不能随意改名)
        PerfAlertClassifier.MeiliRules.P99Error.Should().Be("meili_p99_error");
        PerfAlertClassifier.MeiliRules.P99Warn.Should().Be("meili_p99_warn");
        PerfAlertClassifier.MeiliRules.FallbackRateError.Should().Be("meili_fallback_rate_error");
    }
}
