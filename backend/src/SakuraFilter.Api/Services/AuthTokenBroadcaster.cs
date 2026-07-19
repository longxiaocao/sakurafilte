// P7.1: Auth Token 跨实例广播 (PG LISTEN)
//   - 单例服务, 启动时开 PG LISTEN 'auth_token_rotated'
//   - 收到 NOTIFY 时调用 AuthTokenStore.ReloadFromDbAsync 重载
//   - 失败时 (PG 不可用) 静默降级, 不影响主流程
//   WHY 与 EtlProgressBroadcaster 分离:
//     - 关注点不同 (auth vs etl progress)
//     - 失败降级策略不同 (auth 失败需立即告警, etl 可降级为本地轮询)
//     - 后续审计/告警可单独扩展
using System.Text.Json;
using Npgsql;

namespace SakuraFilter.Api.Services;

public class AuthTokenBroadcaster : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AuthTokenBroadcaster> _logger;
    private readonly IHostedServiceStatus _hostedStatus;
    private readonly string _pgConn;
    private NpgsqlConnection? _listenConn;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    // 连续失败计数, 驱动指数退避重连 (5s -> 10s -> 20s -> 40s -> 60s 封顶)
    private int _consecutiveFailures;
    public bool IsListening { get; private set; }

    public AuthTokenBroadcaster(IServiceProvider services, ILogger<AuthTokenBroadcaster> logger, IConfiguration config, IHostedServiceStatus hostedStatus)
    {
        _services = services;
        _logger = logger;
        _hostedStatus = hostedStatus;
        // P0-3 修复: 移除硬编码密码兜底, 配置缺失直接抛异常
        _pgConn = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未配置 (检查 appsettings.json 或环境变量 ConnectionStrings__Postgres)");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_listenTask != null)
        {
            try { await _listenTask; } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_listenConn != null)
            {
                await _listenConn.DisposeAsync();
            }
        }
        catch { }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            _hostedStatus.ReportAlive(nameof(AuthTokenBroadcaster));
            try
            {
                _listenConn = new NpgsqlConnection(_pgConn);
                await _listenConn.OpenAsync(ct);
                await using (var cmd = _listenConn.CreateCommand())
                {
                    cmd.CommandText = "LISTEN auth_token_rotated";
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                IsListening = true;
                _consecutiveFailures = 0;  // 重连成功重置计数, 让退避从 5s 重新开始
                _logger.LogInformation("[AuthTokenBroadcaster] LISTEN auth_token_rotated 已启动");

                _listenConn.Notification += async (sender, e) =>
                {
                    if (e.Payload is null) return;
                    _logger.LogInformation("[AuthTokenBroadcaster] 收到广播: {Payload}", e.Payload);
                    try
                    {
                        // 解析 payload 用于审计
                        using var doc = JsonDocument.Parse(e.Payload);
                        var root = doc.RootElement;
                        var rotatedBy = root.TryGetProperty("rotatedBy", out var by) ? by.GetString() : null;
                        _logger.LogInformation("[AuthTokenBroadcaster] 触发 ReloadFromDb (by={By})", rotatedBy);
                        using var scope = _services.CreateScope();
                        var store = scope.ServiceProvider.GetRequiredService<IAuthTokenStore>();
                        await store.ReloadFromDbAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[AuthTokenBroadcaster] ReloadFromDb 失败");
                    }
                };

                // P2-6 修复: 阻塞等通知期间必须周期性报心跳, 否则 /health/ready 5min 后误判 stale
                //   WHY: 之前只在循环顶部 _hostedStatus.ReportAlive 一次, 长连接等通知时心跳陈旧
                //   策略: WaitAsync 用 CancellationToken 配合 Task.WhenAny + Task.Delay, 4min 报一次心跳
                while (!ct.IsCancellationRequested && _listenConn.State == System.Data.ConnectionState.Open)
                {
                    _hostedStatus.ReportAlive(nameof(AuthTokenBroadcaster));
                    try
                    {
                        // 4 分钟超时让出, 然后再报心跳 + 重新等 (避免 5min stale 阈值)
                        var waitTask = _listenConn.WaitAsync(ct);
                        var heartbeatTask = Task.Delay(TimeSpan.FromMinutes(4), ct);
                        var completed = await Task.WhenAny(waitTask, heartbeatTask);
                        if (completed == waitTask)
                        {
                            // WaitAsync 完成 (连接断/出错), 退出内层 while 由外层重试
                            break;
                        }
                        // heartbeatTask 触发, 继续循环 (报心跳 + 再等)
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                // P2-5 修复: 正常退出路径 (WaitAsync 返回但连接非 Open) 也需 Dispose 旧连接, 防止泄漏
                try { _listenConn?.Dispose(); } catch { }
                _listenConn = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                IsListening = false;
                // 指数退避 + jitter: 5s -> 10s -> 20s -> 40s -> 60s 封顶, 加 0~25% jitter 防多实例同步重连
                //   V24-F87 (P2-8): 加 jitter 防 PG 长时间不可用恢复后多实例同步重连产生连接风暴 (thundering herd)
                //   WHY: 纯指数退避在固定失败次数后所有实例同步重试, 加 jitter 分散重连时间
                var baseDelaySec = Math.Min(60, 5 * (int)Math.Pow(2, _consecutiveFailures));
                var jitterSec = (int)(baseDelaySec * 0.25 * Random.Shared.NextDouble());
                var delaySec = baseDelaySec + jitterSec;
                _consecutiveFailures++;
                _logger.LogWarning(ex, "[AuthTokenBroadcaster] 连接失败, {Delay}s 后重试 (第 {N} 次, base={Base} jitter={Jitter})",
                    delaySec, _consecutiveFailures, baseDelaySec, jitterSec);
                try { _listenConn?.Dispose(); } catch { }
                // P2-5 修复: Dispose 后置 null, 防止外部访问已 Dispose 对象 (ObjectDisposedException)
                _listenConn = null;
                try { await Task.Delay(TimeSpan.FromSeconds(delaySec), ct); } catch { return; }
            }
        }
        IsListening = false;
    }
}
