namespace SakuraFilter.Etl;

/// <summary>
/// Day 9.6: ETL 进度跨实例广播器接口
/// 用途: 多实例部署时, A 实例上 ETL 进度变化 → 所有实例的 SSE 客户端都能收到
///
/// 设计: PostgreSQL NOTIFY/LISTEN (零新依赖, 项目已用 PG)
///   - Publish(json): 用 NpgsqlCommand 执行 "NOTIFY etl_progress, '<json>'"
///   - Subscribe(callback): 注册本地回调, NOTIFY 收到时调用
///
/// 优雅降级:
///   - PG NOTIFY 失败 (连接断开) → 记录日志, 静默失败 → SSE 退化为本地轮询
///   - 无 broadcaster 时 SSE 用 1s 轮询 GetActiveTaskInfo (旧行为)
/// </summary>
public interface IEtlProgressBroadcaster
{
    /// <summary>
    /// 启动 LISTEN 后台 task,通常在应用启动时调一次
    /// </summary>
    Task InitAsync(CancellationToken ct = default);

    /// <summary>
    /// 广播进度变化,失败静默 (ETL 主流程不应被广播失败影响)
    /// </summary>
    void Publish(string payload);

    /// <summary>
    /// 订阅广播,返回 IDisposable 用于取消订阅
    /// </summary>
    IDisposable Subscribe(Func<string, Task> callback);

    /// <summary>是否已连上 PG LISTEN (false 时 SSE 应退化为轮询)</summary>
    bool IsListening { get; }
}
