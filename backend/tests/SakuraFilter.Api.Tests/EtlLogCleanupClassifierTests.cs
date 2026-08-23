using FluentAssertions;
using SakuraFilter.Api.Services;
using Xunit;

namespace SakuraFilter.Api.Tests;

/// <summary>
/// v30-16: EtlLogCleanupClassifier 纯函数单元测试
///
/// 测试目标 (覆盖: v30-16 提取自 EtlLogCleanupService 的纯函数):
///   - ParseSettings: ETL 日志清理配置解析 (2 项: retentionDays + batchSize, 带默认值兜底)
///
/// WHY 单测: 配置解析错误会导致 BackgroundService 崩溃 (原 int.Parse) 或清理策略失效
///   (retentionDays 过大删除全部历史, batchSize 过小清理过慢)
///   原逻辑混在 EtlLogCleanupService.RunOnceAsync 中无法直接测, v30-16 提取到 public 静态类
///
/// 顺手修复 P1: int.Parse (无效配置崩溃) → TryParse + 默认值 (无效配置降级)
/// </summary>
public class EtlLogCleanupClassifierTests
{
    // ===== ParseSettings 正常值 =====

    [Fact]
    public void ParseSettings_Valid_Values_Returns_Parsed()
    {
        // 覆盖: 2 个配置项正常值解析
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.retention_days"] = "180",
            ["etl_log.cleanup_batch_size"] = "10000",
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(180);
        s.BatchSize.Should().Be(10000);
    }

    [Fact]
    public void ParseSettings_Missing_Keys_Returns_Defaults()
    {
        // 覆盖: 2 个 key 全部缺失时返回默认值 (90/5000)
        //   场景: 首次启动 system_settings 未初始化, 或运维未配置
        var settings = new Dictionary<string, string?>();

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(90);
        s.BatchSize.Should().Be(5000);
    }

    // ===== RetentionDays 边界 =====

    [Theory]
    // 覆盖: RetentionDays 无效值 (空/null/非数字) 返回默认 90
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("  ")]
    public void ParseSettings_Invalid_RetentionDays_Returns_Default(string invalidValue)
    {
        // 覆盖: retention_days 无效值返回默认 90 (TryParse 失败)
        //   修复 P1: 原代码 int.Parse 会抛 FormatException 崩溃 BackgroundService
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.retention_days"] = invalidValue,
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(90);
    }

    [Fact]
    public void ParseSettings_Zero_RetentionDays_Passthrough()
    {
        // 覆盖: RetentionDays=0 透传 (0=永久保留, 下游 RunOnceAsync 用 <= 0 判断跳过)
        //   WHY 透传而非默认值: 0 是合法配置 (永久保留), 不应被覆盖为默认 90
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.retention_days"] = "0",
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(0);
    }

    [Fact]
    public void ParseSettings_Negative_RetentionDays_Passthrough()
    {
        // 覆盖: RetentionDays=-1 透传 (负数, 下游 RunOnceAsync 用 <= 0 判断跳过, 等同永久保留)
        //   WHY 透传而非默认值: 保持原代码行为 (int.Parse 解析负数, 下游 <= 0 跳过)
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.retention_days"] = "-1",
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(-1);
    }

    // ===== BatchSize 边界 =====

    [Theory]
    // 覆盖: BatchSize 无效值 (空/null/非数字/0/负数) 返回默认 5000
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("0")]
    [InlineData("-100")]
    [InlineData("  ")]
    public void ParseSettings_Invalid_BatchSize_Returns_Default(string invalidValue)
    {
        // 覆盖: batch_size 无效值返回默认 5000 (TryParse 失败或 n <= 0)
        //   修复 P1: 原代码负数会导致 EF Core Take(batchSize) 抛异常
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.cleanup_batch_size"] = invalidValue,
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.BatchSize.Should().Be(5000);
    }

    // ===== 混合场景 =====

    [Fact]
    public void ParseSettings_Partial_Keys_Mixes_Parsed_And_Defaults()
    {
        // 覆盖: 部分 key 存在部分缺失, 存在的用解析值, 缺失的用默认值
        //   场景: 运维只配置了 retention_days, batch_size 用默认
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.retention_days"] = "30",
            // batch_size 缺失
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(30);
        s.BatchSize.Should().Be(5000);  // 默认
    }

    [Fact]
    public void ParseSettings_Large_Values_Accepted()
    {
        // 覆盖: 大数值 (边界值) 应被接受 (int.TryParse 范围内)
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.retention_days"] = "3650",  // 10 年
            ["etl_log.cleanup_batch_size"] = "100000",
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(3650);
        s.BatchSize.Should().Be(100000);
    }

    [Fact]
    public void ParseSettings_Ignores_Unrelated_Keys()
    {
        // 覆盖: 字典中含其他 key 不影响解析
        var settings = new Dictionary<string, string?>
        {
            ["etl_log.retention_enabled"] = "true",
            ["etl_log.retention_days"] = "60",
            ["other.unrelated.key"] = "999",
            ["dead_letter.retention_days"] = "7",  // 不同前缀, 不应影响
        };

        var s = EtlLogCleanupClassifier.ParseSettings(settings);
        s.RetentionDays.Should().Be(60);
        s.BatchSize.Should().Be(5000);  // 默认
    }
}
