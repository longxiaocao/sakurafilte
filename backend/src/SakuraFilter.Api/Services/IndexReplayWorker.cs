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
    private readonly IHostedServiceStatus _hostedStatus;
    private static readonly int[] BackoffSeconds = { 60, 120, 300, 600, 1800 };
    // Day 7.9: PollInterval / BatchSize 改读 EtlOptions.IndexReplayPollSeconds / IndexReplayBatchSize
    // WHY: 与 EtlOptions 校验联动,配错启动即失败
    // 不再使用 static readonly,改为实例属性,启动后即可生效(不需重启进程)
    private const int MaxRetryCount = 5;  // Day 7: 超过此值转 dead_letter

    public IndexReplayWorker(
        IServiceProvider sp,
        ILogger<IndexReplayWorker> logger,
        IOptions<EtlOptions> etlOptions,
        IHostedServiceStatus hostedStatus)
    {
        _sp = sp;
        _logger = logger;
        _options = etlOptions.Value;
        _hostedStatus = hostedStatus;
    }

    private TimeSpan PollInterval => TimeSpan.FromSeconds(_options.IndexReplayPollSeconds);
    private int BatchSize => _options.IndexReplayBatchSize;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 等应用启动完成
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(IndexReplayWorker));
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
                // V2 (Task 0.4): 反序列化 Mr1IndexDoc (嵌套结构,替代 ProductIndexDoc)
                var docs = toIndex.Select(p => JsonSerializer.Deserialize<Mr1IndexDoc>(p.Payload)!).ToList();
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
                // V2 (Task 0.4): payload 存 List<string> mr1s (替代 List<long> ids)
                var mr1s = toDelete.SelectMany(p => JsonSerializer.Deserialize<List<string>>(p.Payload) ?? new()).ToList();
                await meili.DeleteAsync(mr1s, ct);
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
    /// Day 7.10.1: 改 status='active' 而非删除
    ///   若 payload 相同且最近 dead_letter 已 recovered, 复用并递增 recovery_count
    ///   (跨恢复-重试-重新死信循环,计数不丢失,max_recovery_count 限位有效)
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

        // Day 7.10.1 BUG FIX: 同一 payload 已 recovered 的死信, 复用其 recovery_count
        //   WHY: 之前方案删除死信行后, 新入队的死信 recovery_count=0, max 限位失效
        //   现在死信行 status='recovered' 保留, 新失败时检查同 payload 最近 dead_letter
        //   找到则更新其 status='active' + retry_count/last_error, 不新增行
        // P1-1 修复: 批量预拉候选, 避免 N+1 (原 foreach 内 FirstOrDefaultAsync, 500 条触发 500 次 SQL)
        //   新方案: 1 次 SQL 拉所有候选 → 内存按 (Operation, Payload) 精确匹配 → 500 次 SQL 降为 1 次
        var candidateOps = exhausted.Select(p => p.Operation).Distinct().ToList();
        var candidatePayloads = exhausted.Select(p => p.Payload).Distinct().ToList();
        var candidates = await db.SearchIndexDeadLetters
            .Where(d => d.Status == "recovered"
                        && candidateOps.Contains(d.Operation)
                        && candidatePayloads.Contains(d.Payload))
            .ToListAsync(ct);
        // 内存按 (Operation, Payload) 分组取最近 RecoveredAt 的一条 (精确匹配, 避免 Contains 交叉)
        var existingDict = new Dictionary<(string, string), SearchIndexDeadLetter>();
        foreach (var d in candidates)
        {
            var key = (d.Operation, d.Payload);
            if (!existingDict.TryGetValue(key, out var existing)
                || (d.RecoveredAt ?? DateTime.MinValue) > (existing.RecoveredAt ?? DateTime.MinValue))
            {
                existingDict[key] = d;
            }
        }

        var deadLetters = new List<SearchIndexDeadLetter>();
        foreach (var p in exhausted)
        {
            // 从内存字典查找同 operation + payload 的最近 recovered 死信 (O(1) 查找)
            if (existingDict.TryGetValue((p.Operation, p.Payload), out var existingDead))
            {
                // 复用: 更新 retry_count + last_error + status 重置为 active
                //   RecoveryCount 保持不变 (入死信不递增, 恢复时才 +1)
                //   若这里把 pending.retry_count 写进去会污染字段语义
                existingDead.RetryCount = p.RetryCount;
                existingDead.LastError = p.LastError;
                existingDead.Status = "active";
                existingDead.MovedAt = now;
                existingDead.RecoveredAt = null;          // 清除旧 recovered 标记
                existingDead.RecoveredToPendingId = null; // 由下次恢复时回填
                _logger.LogWarning("死信复用: id={Id} recovery_count={Rc} 再次失败, 重置为 active (retry_count={Ret})",
                    existingDead.Id, existingDead.RecoveryCount, p.RetryCount);
            }
            else
            {
                // 新死信: 正常创建
                deadLetters.Add(new SearchIndexDeadLetter
                {
                    OriginalId = p.Id,
                    Operation = p.Operation,
                    Payload = p.Payload,
                    RetryCount = p.RetryCount,
                    LastError = p.LastError,
                    CreatedAt = p.CreatedAt,
                    MovedAt = now,
                    Status = "active",
                });
            }
        }
        if (deadLetters.Count > 0)
            await db.SearchIndexDeadLetters.AddRangeAsync(deadLetters, ct);
        db.SearchIndexPending.RemoveRange(exhausted);
        await db.SaveChangesAsync(ct);
        _logger.LogWarning("已转死信: {New} 新建 + {Reused} 复用, 共 {Total} 条 (最后一次错误示例: {Err})",
            deadLetters.Count, exhausted.Count - deadLetters.Count, exhausted.Count,
            (exhausted[0].LastError?.Substring(0, Math.Min(100, exhausted[0].LastError?.Length ?? 0)) ?? ""));
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
