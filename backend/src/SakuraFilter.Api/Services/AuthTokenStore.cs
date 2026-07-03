// P7.1: X-Admin-Token 双 Key 轮转存储 (DB + PG NOTIFY 广播)
// 设计:
//   - 单行表 auth_token_state (id=1), 存 current_key + previous_key + rotated_at + rotated_by
//   - 启动时从 DB 读覆盖 IConfiguration 值 (实现零停机轮转)
//   - 多实例部署: A 实例 rotate → 写 DB + NOTIFY 'auth_token_rotated' → B/C 实例 LISTEN 收到后重载内存
//   - API 层 DevTokenAuthMiddleware 注入 IAuthTokenStore, 不再直读 IConfiguration
//   - CLI 工具 SakuraFilter.Cli rotate-token 通过本服务的静态方法操作 DB
// WHY DB 存储 + 广播:
//   - DB 持久化: 进程重启后新值不丢
//   - NOTIFY 实时: 多实例秒级生效, 不需要滚动重启
//   - 不用 Redis 依赖: 保持系统离线能力 (与 EtlProgressBroadcaster 一致)
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Services;

public interface IAuthTokenStore
{
    /// <summary>当前 token (主)</summary>
    string Current { get; }
    /// <summary>前一个 token (过渡期可空, 用于客户端无感切换)</summary>
    string? Previous { get; }
    /// <summary>上次轮转时间 (UTC)</summary>
    DateTime? LastRotatedAt { get; }
    /// <summary>上次轮转操作人 (CLI 传入 --by, 后端留痕)</summary>
    string? LastRotatedBy { get; }
    /// <summary>是否从 DB 加载 (true) 还是用了 appsettings.json 兜底 (false)</summary>
    bool LoadedFromDb { get; }

    /// <summary>应用双 key 切换: 旧 current → previous, 新的为 current。写 DB + NOTIFY 广播</summary>
    Task RotateAsync(string newCurrent, string? newPrevious, string? by, CancellationToken ct);

    /// <summary>从 DB 重新加载 (PG NOTIFY 触发其他实例重载时调用)</summary>
    Task ReloadFromDbAsync(CancellationToken ct);

    /// <summary>启动时初始化 (建表 + 加载)</summary>
    Task InitAsync(CancellationToken ct = default);
}

public class AuthTokenStore : IAuthTokenStore
{
    private readonly ILogger<AuthTokenStore> _logger;
    private readonly IConfiguration _config;
    private readonly string _pgConn;
    // 内存快照 (读多写少, 用 volatile 引用 + lock 保护)
    private TokenSnapshot _snapshot;
    private readonly object _lock = new();
    private readonly AuthTokenBroadcaster _broadcaster;
    private bool _started;

    public AuthTokenStore(IConfiguration config, ILogger<AuthTokenStore> logger, AuthTokenBroadcaster broadcaster)
    {
        _config = config;
        _logger = logger;
        _broadcaster = broadcaster;
        // 从 IConfiguration 拿兜底值 (appsettings.json)
        var fallbackCurrent = config["Auth:DevStaticToken"] ?? "";
        var fallbackPrevious = config["Auth:DevStaticTokenPrevious"];
        _pgConn = config.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=spike_test_v3;Username=postgres;Password=784533";
        _snapshot = new TokenSnapshot(fallbackCurrent, string.IsNullOrEmpty(fallbackPrevious) ? null : fallbackPrevious, null, null, false);
    }

    public string Current { get { lock (_lock) return _snapshot.Current; } }
    public string? Previous { get { lock (_lock) return _snapshot.Previous; } }
    public DateTime? LastRotatedAt { get { lock (_lock) return _snapshot.RotatedAt; } }
    public string? LastRotatedBy { get { lock (_lock) return _snapshot.RotatedBy; } }
    public bool LoadedFromDb { get { lock (_lock) return _snapshot.LoadedFromDb; } }

    /// <summary>
    /// 启动时调用: 从 DB 加载 (覆盖兜底)
    /// 失败时保持 appsettings.json 兜底值
    /// 包含 idempotent CREATE TABLE (部署不依赖 SQL migration, 独立可工作)
    /// </summary>
    public async Task InitAsync(CancellationToken ct = default)
    {
        // BUG FIX: 去掉 _started 守卫 — 之前用 _started=true 阻止重复 InitAsync, 导致
        //   ReloadFromDbAsync (PG NOTIFY 触发) 永远被忽略, 轮转后 store 不更新
        //   改成: 建表只做一次 (确保 idempotent), 加载每次都做
        try
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            // P7.1: idempotent 建表 — 即使没手动跑 SQL migration 也能工作
            //   上线后应改为正式的 SQL migration (EF Core 或手写), 这里只是兜底
            //   表设计: 单行 (id=1), 存 current_key + previous_key + 审计字段
            if (!_started)
            {
                await using (var createCmd = conn.CreateCommand())
                {
                    createCmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS auth_token_state (
                            id            smallint      PRIMARY KEY,
                            current_key   varchar(128)  NOT NULL,
                            previous_key  varchar(128),
                            rotated_at    timestamptz,
                            rotated_by    varchar(64)
                        );
                        INSERT INTO auth_token_state (id, current_key)
                        VALUES (1, @cur)
                        ON CONFLICT (id) DO NOTHING;";
                    createCmd.Parameters.AddWithValue("cur", _config["Auth:DevStaticToken"] ?? "");
                    await createCmd.ExecuteNonQueryAsync(ct);
                }
                _started = true;
            }
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT current_key, previous_key, rotated_at, rotated_by FROM auth_token_state WHERE id = 1";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var current = reader.GetString(0);
                var previous = reader.IsDBNull(1) ? null : reader.GetString(1);
                DateTime? rotatedAt = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                string? rotatedBy = reader.IsDBNull(3) ? null : reader.GetString(3);
                lock (_lock)
                {
                    _snapshot = new TokenSnapshot(current, previous, rotatedAt, rotatedBy, true);
                }
                _logger.LogInformation("[AuthTokenStore] 从 DB 加载 (current={CurrentLen}字, previous={PrevLen}字, rotatedAt={At}, by={By})",
                    current.Length, previous?.Length ?? 0, rotatedAt, rotatedBy);
            }
            else
            {
                _logger.LogInformation("[AuthTokenStore] DB 无 auth_token_state 行, 使用 appsettings.json 兜底");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AuthTokenStore] DB 加载失败, 使用 appsettings.json 兜底");
        }
    }

    /// <summary>
    /// 应用轮转: DB upsert + 内存更新 + PG NOTIFY 广播 (触发其他实例重载)
    /// </summary>
    public async Task RotateAsync(string newCurrent, string? newPrevious, string? by, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(newCurrent) || newCurrent.Length < 32)
            throw new ArgumentException("新 token 长度必须 ≥ 32", nameof(newCurrent));

        // 写 DB (upsert)
        var now = DateTime.UtcNow;
        await using var conn = new NpgsqlConnection(_pgConn);
        await conn.OpenAsync(ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO auth_token_state (id, current_key, previous_key, rotated_at, rotated_by)
                VALUES (1, @cur, @prev, @at, @by)
                ON CONFLICT (id) DO UPDATE SET
                    current_key = EXCLUDED.current_key,
                    previous_key = EXCLUDED.previous_key,
                    rotated_at = EXCLUDED.rotated_at,
                    rotated_by = EXCLUDED.rotated_by";
            cmd.Parameters.AddWithValue("cur", newCurrent);
            cmd.Parameters.AddWithValue("prev", (object?)newPrevious ?? DBNull.Value);
            cmd.Parameters.AddWithValue("at", now);
            cmd.Parameters.AddWithValue("by", (object?)by ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 内存更新
        lock (_lock)
        {
            _snapshot = new TokenSnapshot(newCurrent, newPrevious, now, by, true);
        }

        // PG NOTIFY 广播 — 触发其他实例重载
        // P7.1 BUG FIX: 之前用字符串拼接 NOTIFY ... "{payload}", PG 报 42601 (JSON 内引号)
        //   改用参数化 pg_notify('channel', @payload) — 驱动负责转义
        await using (var notifyCmd = conn.CreateCommand())
        {
            var payload = JsonSerializer.Serialize(new
            {
                current = newCurrent,
                previous = newPrevious,
                rotatedAt = now,
                rotatedBy = by
            });
            notifyCmd.CommandText = "SELECT pg_notify('auth_token_rotated', @p)";
            notifyCmd.Parameters.AddWithValue("p", payload);
            await notifyCmd.ExecuteNonQueryAsync(ct);
        }

        _logger.LogInformation("[AuthTokenStore] 轮转完成 current={CurrentLen}字 previous={PrevLen}字 by={By}",
            newCurrent.Length, newPrevious?.Length ?? 0, by);
    }

    /// <summary>
    /// 接收广播后, 重新从 DB 加载 (不信任广播内容, 二次校验)
    /// </summary>
    public async Task ReloadFromDbAsync(CancellationToken ct)
    {
        await InitAsync(ct);
    }
}

internal record TokenSnapshot(string Current, string? Previous, DateTime? RotatedAt, string? RotatedBy, bool LoadedFromDb);
