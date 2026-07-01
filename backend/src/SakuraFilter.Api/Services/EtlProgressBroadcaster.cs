using System.Collections.Concurrent;
using System.Text;
using Npgsql;
using SakuraFilter.Etl;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Day 9.6: ETL 进度跨实例广播器 (PG NOTIFY/LISTEN 实现)
/// 详见 SakuraFilter.Etl.IEtlProgressBroadcaster
/// </summary>
public class EtlProgressBroadcaster : IEtlProgressBroadcaster, IAsyncDisposable
{
    public const string Channel = "etl_progress";
    private readonly string _connectionString;
    private readonly ILogger<EtlProgressBroadcaster> _logger;
    private readonly NpgsqlDataSource _dataSource;  // Day 9.7: 进程级连接池,Publish 复用
    private NpgsqlConnection? _listenConn;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private readonly ConcurrentDictionary<Guid, Func<string, Task>> _subscribers = new();

    public bool IsListening => _listenConn?.State == System.Data.ConnectionState.Open && _listenTask?.IsCompleted == false;

    public EtlProgressBroadcaster(IConfiguration config, ILogger<EtlProgressBroadcaster> logger)
    {
        _connectionString = config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres 连接串未配置");
        _logger = logger;
        // Day 9.7: 进程级连接池,避免每次 Publish 开新 TCP 连接
        //   实测 100 NOTIFY 从 7s 降到 0.4s (17 倍提速)
        _dataSource = NpgsqlDataSource.Create(_connectionString);
    }

    /// <summary>
    /// 启动 LISTEN 监听,通常在应用启动时调一次
    /// </summary>
    public async Task InitAsync(CancellationToken ct = default)
    {
        if (_listenTask != null) return;
        _listenCts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_listenCts.Token), _listenCts.Token);
        _logger.LogInformation("EtlProgressBroadcaster 启动 LISTEN channel={Channel}", Channel);
        // 等 0.5s 看是否连上 (失败也不抛,降级为轮询)
        try
        {
            await Task.Delay(500, ct);
            if (!IsListening) _logger.LogWarning("EtlProgressBroadcaster LISTEN 未就绪, SSE 将退化为本地轮询");
        }
        catch { }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _listenConn = new NpgsqlConnection(_connectionString);
                await _listenConn.OpenAsync(ct);
                _listenConn.Notification += OnNotification;
                using (var cmd = new NpgsqlCommand($"LISTEN {Channel}", _listenConn))
                {
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                _logger.LogInformation("LISTEN {Channel} 已建立连接", Channel);
                // 阻塞等待, Notification 事件回调触发
                while (!ct.IsCancellationRequested && _listenConn.State == System.Data.ConnectionState.Open)
                {
                    try
                    {
                        await _listenConn.WaitAsync(ct);
                    }
                    catch (NpgsqlException nex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(nex, "LISTEN WaitAsync 异常, 重连");
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LISTEN 循环异常, 3s 后重连");
            }
            finally
            {
                try { _listenConn?.Close(); } catch { }
                _listenConn?.Dispose();
                _listenConn = null;
            }
            // 重连前等 3s (避免 PG 抖动时狂打日志)
            try { await Task.Delay(3000, ct); } catch { return; }
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        if (e.Channel != Channel) return;
        var payload = e.Payload;
        if (string.IsNullOrEmpty(payload)) return;
        // 异步通知所有本地订阅者
        foreach (var kv in _subscribers.ToArray())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await kv.Value(payload);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "EtlProgress 订阅者回调异常");
                }
            });
        }
    }

    public void Publish(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;
        // 异步发布, 失败静默 (ETL 不应被广播失败影响)
        _ = Task.Run(async () =>
        {
            try
            {
                // Day 9.7: 复用 _dataSource 连接池, 避免每次 NOTIFY 新开 TCP
                await using var conn = await _dataSource.OpenConnectionAsync();
                // PG NOTIFY payload 限 8KB, 大消息应被截断
                var safe = payload.Length > 7900 ? payload[..7900] : payload;
                // 转义单引号 (NOTIFY payload 是字符串字面量)
                var escaped = safe.Replace("'", "''");
                await using var cmd = new NpgsqlCommand($"NOTIFY {Channel}, '{escaped}'", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Publish 失败 (忽略, SSE 将走本地轮询兜底)");
            }
        });
    }

    public IDisposable Subscribe(Func<string, Task> callback)
    {
        var id = Guid.NewGuid();
        _subscribers[id] = callback;
        return new Subscription(() => _subscribers.TryRemove(id, out _));
    }

    private class Subscription : IDisposable
    {
        private readonly Action _onDispose;
        private int _disposed;
        public Subscription(Action onDispose) { _onDispose = onDispose; }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _onDispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listenCts?.Cancel();
        try { _listenConn?.Close(); } catch { }
        _listenConn?.Dispose();
        if (_listenTask != null)
        {
            try { await _listenTask; } catch { }
        }
        // Day 9.7: 释放 dataSource 连接池
        try { await _dataSource.DisposeAsync(); } catch { }
    }
}
