using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace SakuraFilter.Etl;

/// <summary>
/// ETL 配置选项 (Day 7.8)
/// - Bind 自 appsettings.json 的 "Etl" section
/// - ValidateOnStart: 启动阶段校验,运行期不再发现配置错
/// - 范围校验通过 IValidateOptions<EtlOptions> 实现,失败时启动失败而非运行期报错
/// </summary>
public class EtlOptions
{
    /// <summary>ETL 错误环形缓冲容量 (Day 7.6) — 失败风暴时观察分布用</summary>
    public int RecentErrorBuffer { get; set; } = 5;

    /// <summary>Meili 索引补偿轮询周期 (秒)</summary>
    public int IndexReplayPollSeconds { get; set; } = 10;

    /// <summary>Meili 索引补偿单批处理大小</summary>
    public int IndexReplayBatchSize { get; set; } = 500;
}

/// <summary>
/// EtlOptions 校验器 (Day 7.8)
/// WHY 手动校验而非 [Range] DataAnnotations:
///   1. 不依赖 Microsoft.Extensions.Options.DataAnnotations 包 (项目内未引用)
///   2. 错误消息更友好,直接告诉运维"实际值 X 不在范围 [1, 100]"
///   3. 启动失败时堆栈清晰,便于定位
/// </summary>
public class EtlOptionsValidator : IValidateOptions<EtlOptions>
{
    public ValidateOptionsResult Validate(string? name, EtlOptions options)
    {
        var failures = new List<string>();

        if (options.RecentErrorBuffer < 1 || options.RecentErrorBuffer > 100)
            failures.Add($"Etl:RecentErrorBuffer 必须在 [1, 100],实际 {options.RecentErrorBuffer}");

        if (options.IndexReplayPollSeconds < 1 || options.IndexReplayPollSeconds > 3600)
            failures.Add($"Etl:IndexReplayPollSeconds 必须在 [1, 3600],实际 {options.IndexReplayPollSeconds}");

        if (options.IndexReplayBatchSize < 1 || options.IndexReplayBatchSize > 10000)
            failures.Add($"Etl:IndexReplayBatchSize 必须在 [1, 10000],实际 {options.IndexReplayBatchSize}");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
