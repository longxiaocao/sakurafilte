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
    private readonly string _pgConn;
    private NpgsqlConnection? _listenConn;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    public bool IsListening { get; private set; }

    public AuthTokenBroadcaster(IServiceProvider services, ILogger<AuthTokenBroadcaster> logger, IConfiguration config)
    {
        _services = services;
        _logger = logger;
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

                // 保持连接 + 处理断连重试
                while (!ct.IsCancellationRequested && _listenConn.State == System.Data.ConnectionState.Open)
                {
                    await _listenConn.WaitAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                IsListening = false;
                _logger.LogWarning(ex, "[AuthTokenBroadcaster] 连接失败, 5s 后重试");
                try { _listenConn?.Dispose(); } catch { }
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { return; }
            }
        }
        IsListening = false;
    }
}
