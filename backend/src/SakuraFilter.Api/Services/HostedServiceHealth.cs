using System.Collections.Concurrent;

namespace SakuraFilter.Api.Services;

// P1-5.1: 后台服务心跳健康检查
//   - 8 个 BackgroundService 在主循环内定期 ReportAlive
//   - /health/ready 暴露 stale 列表 (超过 5min 未心跳视为卡死)
//   - 仅暴露状态,不参与 allOk 判定 (不剔除流量)
//   WHY: 之前 BackgroundService 卡死 (死锁/异常吞掉) 完全不可见,
//        运维只能从日志事后排查。心跳机制让卡死可在 /health/ready 提前发现

/// <summary>
/// 后台服务心跳状态接口 (P1-5.1)
/// </summary>
public interface IHostedServiceStatus
{
    /// <summary>后台服务上报存活</summary>
    void ReportAlive(string serviceName);

    /// <summary>获取最后心跳时间 (UTC), null 表示从未上报</summary>
    DateTime? LastHeartbeat(string serviceName);

    /// <summary>获取所有被跟踪的服务名</summary>
    IReadOnlyCollection<string> TrackedServices { get; }

    /// <summary>获取超过阈值未心跳的服务 (stale)</summary>
    IReadOnlyList<string> GetStaleServices(TimeSpan threshold);
}

/// <summary>
/// 心跳状态实现 (线程安全 ConcurrentDictionary)
/// </summary>
public class HostedServiceStatus : IHostedServiceStatus
{
    private readonly ConcurrentDictionary<string, DateTime> _heartbeats = new();
    private readonly ILogger<HostedServiceStatus> _logger;

    public HostedServiceStatus(ILogger<HostedServiceStatus> logger)
    {
        _logger = logger;
    }

    public void ReportAlive(string serviceName)
    {
        var now = DateTime.UtcNow;
        _heartbeats[serviceName] = now;
    }

    public DateTime? LastHeartbeat(string serviceName)
    {
        return _heartbeats.TryGetValue(serviceName, out var t) ? t : null;
    }

    public IReadOnlyCollection<string> TrackedServices => _heartbeats.Keys.ToArray();

    public IReadOnlyList<string> GetStaleServices(TimeSpan threshold)
    {
        var cutoff = DateTime.UtcNow - threshold;
        return _heartbeats
            .Where(kv => kv.Value < cutoff)
            .Select(kv => kv.Key)
            .OrderBy(s => s)
            .ToList();
    }
}

/// <summary>
/// 心跳扩展方法 (P1-5.1)
/// WHY: 长延迟服务 (24h 轮询的清理类服务) 在等待期间会被 /health/ready 误判为 stale,
///      提供此扩展让长 Task.Delay 分段上报心跳
/// </summary>
public static class HostedServiceStatusExtensions
{
    /// <summary>
    /// 等待指定时间,期间定期上报心跳 (默认 4 分钟一次,避免 5min stale 阈值)
    /// </summary>
    public static async Task WaitWithHeartbeatAsync(
        this IHostedServiceStatus status,
        string serviceName,
        TimeSpan delay,
        CancellationToken ct = default,
        TimeSpan? heartbeatInterval = null)
    {
        var interval = heartbeatInterval ?? TimeSpan.FromMinutes(4);
        var deadline = DateTime.UtcNow + delay;
        while (DateTime.UtcNow < deadline)
        {
            status.ReportAlive(serviceName);
            var remaining = deadline - DateTime.UtcNow;
            var wait = remaining < interval ? remaining : interval;
            if (wait <= TimeSpan.Zero) break;
            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
