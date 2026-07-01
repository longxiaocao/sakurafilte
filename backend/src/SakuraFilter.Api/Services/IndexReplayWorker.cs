using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SakuraFilter.Core.DTOs;
using SakuraFilter.Core.Entities;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Search;
using SakuraFilter.Etl;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Meili 索引写入补偿 Worker (Day 5)
/// - 每 N s 扫描 search_index_pending (Day 7: 10s;Day 7.9: 改用配置)
/// - 每批 M 条 (Day 7: 500;Day 7.9: 改用配置)
/// - 对 retry_count < 5 的条目重试 (指数退避 60s/120s/300s/600s/1800s)
/// - 成功后删除条目,失败更新 next_retry_at
/// - 重试 5 次后转入 search_index_dead_letter (Day 7) 等待人工排查
/// </summary>
public class IndexReplayWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<IndexReplayWorker> _logger;
    private readonly EtlOptions _options;
    private static readonly int[] BackoffSeconds = { 60, 120, 300, 600, 1800 };
    // Day 7.9: PollInterval / BatchSize 改读 EtlOptions.IndexReplayPollSeconds / IndexReplayBatchSize
    // WHY: 与 EtlOptions 校验联动,配错启动即失败
    // 不再使用 static readonly,改为实例属性,启动后即可生效(不需重启进程)
    private const int MaxRetryCount = 5;  // Day 7: 超过此值转 dead_letter

    public IndexReplayWorker(
        IServiceProvider sp,
        ILogger<IndexReplayWorker> logger,
        IOptions<EtlOptions> etlOptions)
    {
        _sp = sp;
        _logger = logger;
        _options = etlOptions.Value;
    }

    private TimeSpan PollInterval => TimeSpan.FromSeconds(_options.IndexReplayPollSeconds);
    private int BatchSize => _options.IndexReplayBatchSize;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 等应用启动完成
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
                // Day 7: 处理重试超限条目 (转入死信队列)
                await ProcessDeadLetterAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexReplayWorker 轮询异常");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        var meili = scope.ServiceProvider.GetRequiredService<MeiliSearchProvider>();

        // 1) 取到期的待重试条目 (最多 BatchSize=500 条,避免长事务)
        var now = DateTime.UtcNow;
        var pending = await db.SearchIndexPending
            .Where(p => p.NextRetryAt <= now && p.RetryCount < MaxRetryCount)
            .OrderBy(p => p.NextRetryAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        _logger.LogInformation("待重试条目: {Count}", pending.Count);

        // 2) 按 operation 分组,批量处理
        var toIndex = pending.Where(p => p.Operation == "index").ToList();
        var toDelete = pending.Where(p => p.Operation == "delete").ToList();

        // 索引
        if (toIndex.Count > 0)
        {
            try
            {
                var docs = toIndex.Select(p => JsonSerializer.Deserialize<ProductIndexDoc>(p.Payload)!).ToList();
                await meili.IndexAsync(docs, ct);
                // 成功后批量删除
                db.SearchIndexPending.RemoveRange(toIndex);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Meili 重试索引成功: {Count} 条", toIndex.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meili 重试索引失败");
                await UpdateRetryAsync(db, toIndex, ex.Message, ct);
            }
        }

        // 删除
        if (toDelete.Count > 0)
        {
            try
            {
                var ids = toDelete.SelectMany(p => JsonSerializer.Deserialize<List<long>>(p.Payload) ?? new()).ToList();
                await meili.DeleteAsync(ids, ct);
                db.SearchIndexPending.RemoveRange(toDelete);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Meili 重试删除成功: {Count} 条", toDelete.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Meili 重试删除失败");
                await UpdateRetryAsync(db, toDelete, ex.Message, ct);
            }
        }
    }

    /// <summary>
    /// Day 7: 把 retry_count >= 5 的 pending 条目转入死信队列
    /// WHY: 反复重试无意义的条目 (Meili schema 错误、payload 损坏) 占用 pending 队列
    ///      转移到 dead_letter 等待人工排查,worker 只处理下次重试
    /// </summary>
    private async Task ProcessDeadLetterAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        var now = DateTime.UtcNow;
        var exhausted = await db.SearchIndexPending
            .Where(p => p.RetryCount >= MaxRetryCount)
            .OrderBy(p => p.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (exhausted.Count == 0) return;

        // 复制到 dead_letter
        var deadLetters = exhausted.Select(p => new SearchIndexDeadLetter
        {
            OriginalId = p.Id,
            Operation = p.Operation,
            Payload = p.Payload,
            RetryCount = p.RetryCount,
            LastError = p.LastError,
            CreatedAt = p.CreatedAt,
            MovedAt = now
        }).ToList();
        await db.SearchIndexDeadLetters.AddRangeAsync(deadLetters, ct);
        db.SearchIndexPending.RemoveRange(exhausted);
        await db.SaveChangesAsync(ct);
        _logger.LogWarning("已转死信: {Count} 条 (最后一次错误示例: {Err})",
            deadLetters.Count, deadLetters[0].LastError?.Substring(0, Math.Min(100, deadLetters[0].LastError?.Length ?? 0)));
    }

    private static async Task UpdateRetryAsync(ProductDbContext db, List<SearchIndexPending> items, string error, CancellationToken ct)
    {
        foreach (var p in items)
        {
            p.RetryCount++;
            p.LastError = error.Length > 500 ? error[..500] : error;
            p.NextRetryAt = DateTime.UtcNow.AddSeconds(
                p.RetryCount <= BackoffSeconds.Length
                    ? BackoffSeconds[p.RetryCount - 1]
                    : BackoffSeconds[^1]);
        }
        await db.SaveChangesAsync(ct);
    }
}
