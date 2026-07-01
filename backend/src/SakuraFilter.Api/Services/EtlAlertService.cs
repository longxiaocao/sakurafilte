using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

/// <summary>
/// ETL 失败告警服务 (Day 7.9)
/// - 每 N s 扫描 etl_progress_log (status='failed' AND alert_sent=false)
/// - POST 到配置的 webhook URL (钉钉/飞书/Slack/自定义)
/// - 推送成功后置 alert_sent=true,避免重复告警
/// - 推送失败:不置位,下次轮询重试
///
/// WHY 独立 BackgroundService 而非 EtlProgress.Fail() 内联:
///   - 解耦告警可靠性与 ETL 业务逻辑
///   - webhook 暂时不可用时不影响 ETL 完结
///   - 失败可重试 (不置位)
///   - 告警策略可独立调整 (间隔、批大小、目标 URL)
///
/// WHY 用 system_settings 而非 appsettings:
///   - 运维可在线修改 webhook URL (例如切换钉钉群),不必重启
///   - 与现有 retention_* 配置同源
/// </summary>
public class EtlAlertService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<EtlAlertService> _logger;
    private readonly IHttpClientFactory _httpFactory;

    // 默认配置 (启动时若 system_settings 中无则插入)
    private static readonly (string Key, string Value, string Description)[] Defaults = new[]
    {
        ("alert.enabled", "false", "ETL 失败告警全局开关 (true/false, 默认关闭)"),
        ("alert.webhook_url", "", "告警 webhook URL (钉钉/飞书/Slack/自定义 POST)"),
        ("alert.poll_seconds", "60", "轮询周期 (秒),失败时按此间隔重试"),
        ("alert.batch_size", "50", "单批推送上限,避免一次推太多"),
    };

    public EtlAlertService(
        IServiceProvider sp,
        ILogger<EtlAlertService> logger,
        IHttpClientFactory httpFactory)
    {
        _sp = sp;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时确保默认配置存在
        await EnsureDefaultSettingsAsync(stoppingToken);

        // 自适应轮询: 失败多时按 poll_seconds,空闲时按 5x
        //   简化: 始终按 poll_seconds 轮询 (避免自适应逻辑复杂度)
        int pollSec = 60;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                pollSec = await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ETL 告警任务异常,下一轮重试");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSec), stoppingToken);
        }
    }

    private async Task EnsureDefaultSettingsAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        foreach (var (key, value, desc) in Defaults)
        {
            var exists = await db.SystemSettings.AnyAsync(s => s.Key == key, ct);
            if (!exists)
            {
                db.SystemSettings.Add(new Core.Entities.SystemSetting
                {
                    Key = key,
                    Value = value,
                    Description = desc,
                    UpdatedAt = DateTime.UtcNow
                });
                _logger.LogInformation("插入 ETL 告警默认配置: {Key} = {Value}", key, value);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        // 1) 读配置
        var settings = await db.SystemSettings
            .Where(s => s.Key.StartsWith("alert."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var enabled = settings.GetValueOrDefault("alert.enabled") == "true";
        var webhookUrl = settings.GetValueOrDefault("alert.webhook_url") ?? "";
        var pollSec = int.TryParse(settings.GetValueOrDefault("alert.poll_seconds"), out var ps) && ps > 0 ? ps : 60;
        var batchSize = int.TryParse(settings.GetValueOrDefault("alert.batch_size"), out var bs) && bs > 0 ? bs : 50;

        if (!enabled)
        {
            _logger.LogDebug("ETL 告警已禁用 (alert.enabled != true),跳过");
            return pollSec;
        }
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning("ETL 告警已启用但 alert.webhook_url 为空,跳过 (请配置)");
            return pollSec;
        }

        // 2) 取出未告警的失败记录
        var failed = await db.EtlProgressLogs
            .Where(l => l.Status == "failed" && !l.AlertSent)
            .OrderBy(l => l.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        if (failed.Count == 0)
        {
            _logger.LogDebug("无未告警的 ETL 失败记录");
            return pollSec;
        }

        _logger.LogInformation("发现 {Count} 条未告警的失败记录,开始推送 webhook", failed.Count);

        // 3) 逐条推送 (避免一条失败影响整批)
        int pushed = 0, failed_push = 0;
        foreach (var item in failed)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var payload = BuildPayload(item);
                using var http = _httpFactory.CreateClient("EtlAlert");
                var resp = await http.PostAsJsonAsync(webhookUrl, payload, ct);
                if (resp.IsSuccessStatusCode)
                {
                    item.AlertSent = true;
                    pushed++;
                }
                else
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("webhook 推送失败: id={Id} status={Status} body={Body}",
                        item.Id, (int)resp.StatusCode, body.Length > 200 ? body[..200] : body);
                    failed_push++;
                    // 不置 alert_sent,下次轮询重试
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "webhook 推送异常: id={Id}", item.Id);
                failed_push++;
            }
        }

        if (pushed > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        _logger.LogInformation("本轮告警推送: 成功 {Pushed} / 失败 {Failed} / 候选 {Total}",
            pushed, failed_push, failed.Count);

        return pollSec;
    }

    /// <summary>
    /// 构造 webhook payload (通用 JSON,支持钉钉/飞书/Slack/自定义)
    /// WHY 用通用结构: 不同 webhook 接收格式不同,通用 JSON 由接收端 adapter 解析
    /// </summary>
    private static object BuildPayload(Core.Entities.EtlProgressLog item)
    {
        return new
        {
            @event = "etl.failed",
            timestamp = DateTime.UtcNow.ToString("o"),
            etl = new
            {
                id = item.Id,
                entity_type = item.EntityType,
                mode = item.Mode,
                file_path = item.FilePath,
                read_count = item.ReadCount,
                inserted_count = item.InsertedCount,
                updated_count = item.UpdatedCount,
                skipped_count = item.SkippedCount,
                skipped_missing_oem = item.SkippedMissingOem,
                skipped_null_field = item.SkippedNullField,
                skipped_duplicate = item.SkippedDuplicate,
                error_count = item.ErrorCount,
                indexed_count = item.IndexedCount,
                index_pending_count = item.IndexPendingCount,
                last_error = item.LastError,
                started_at = item.StartedAt.ToString("o"),
                finished_at = item.FinishedAt.ToString("o"),
                duration_sec = item.DurationSec,
            },
            text = $"[ETL FAILED] {item.EntityType} {item.Mode} {item.FilePath} | err={item.LastError?.Substring(0, Math.Min(120, item.LastError?.Length ?? 0))}"
        };
    }
}
