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
