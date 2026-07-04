// P7.1: X-Admin-Token 双 Key 轮转 CLI
//   子命令:
//     rotate-token --new <token> [--old <token>] [--by <user>] [--dry-run] [--pg-conn <conn>]
//       写 DB (auth_token_state) + 立即从 DB 重载 (本地 instance 内存更新)
//       不修改 appsettings.json (无重启副作用, 不污染 git 跟踪)
//     status [--pg-conn <conn>]
//       打印当前 DB 状态 (current 前缀 + previous 前缀 + rotated_at + rotated_by)
//   优势:
//     - 无状态 CLI, 单文件 dotnet run 即用
//     - 不需要连接运行中的 API, 独立可执行
//     - DB 写入成功即生效 (API 实例通过 PG LISTEN 广播自动重载)
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace SakuraFilter.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        // 配置加载 (appsettings.json + 环境变量覆盖)
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var pgConn = ResolvePgConn(args, config);
        var subCommand = args[0].ToLowerInvariant();
        return subCommand switch
        {
            "rotate-token" => await RotateTokenAsync(args, pgConn),
            "status" => await ShowStatusAsync(pgConn),
            _ => Fail($"未知子命令: {subCommand}"),
        };
    }

    private static string ResolvePgConn(string[] args, IConfiguration config)
    {
        // 优先级: --pg-conn > 环境变量 > appsettings.json > 兜底
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--pg-conn") return args[i + 1];
        }
        var envConn = Environment.GetEnvironmentVariable("SakuraFilter_Postgres");
        if (!string.IsNullOrEmpty(envConn)) return envConn;
        // P0-3 修复: 移除硬编码密码兜底, 配置缺失直接抛异常 (强制从 appsettings/环境变量读取)
        return config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未配置 (检查 appsettings.json 或环境变量 ConnectionStrings__Postgres)");
    }

    private static async Task<int> RotateTokenAsync(string[] args, string pgConn)
    {
        // 解析参数
        string? newToken = null, oldToken = null, by = null;
        var dryRun = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--new":
                    if (i + 1 >= args.Length) return Fail("--new 缺少值");
                    newToken = args[++i];
                    break;
                case "--old":
                    if (i + 1 >= args.Length) return Fail("--old 缺少值");
                    oldToken = args[++i];
                    break;
                case "--by":
                    if (i + 1 >= args.Length) return Fail("--by 缺少值");
                    by = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
            }
        }
        if (string.IsNullOrEmpty(newToken)) return Fail("缺少 --new <token>");
        if (newToken.Length < 32) return Fail($"新 token 长度 {newToken.Length} < 32, 不安全");

        // 读 DB 现状
        var (dbCurrent, dbPrevious, dbRotatedAt, dbRotatedBy) = await ReadStateAsync(pgConn);
        Console.WriteLine($"[rotate-token] DB 现状:");
        Console.WriteLine($"  current = {Mask(dbCurrent)} (长度 {dbCurrent?.Length ?? 0})");
        Console.WriteLine($"  previous = {Mask(dbPrevious)} (长度 {dbPrevious?.Length ?? 0})");
        Console.WriteLine($"  rotated_at = {dbRotatedAt:O}");
        Console.WriteLine($"  rotated_by = {dbRotatedBy ?? "(无)"}");
        Console.WriteLine();
        Console.WriteLine($"[rotate-token] 计划:");
        Console.WriteLine($"  new current = {Mask(newToken)} (长度 {newToken.Length})");
        Console.WriteLine($"  new previous = {Mask(oldToken)} (长度 {oldToken?.Length ?? 0})");
        Console.WriteLine($"  by = {by ?? "(无)"}");
        Console.WriteLine($"  dry-run = {dryRun}");

        // 校验: 不允许 new == current (没意义且可能误操作)
        if (newToken == dbCurrent)
            return Fail($"新 token 与当前 DB token 相同, 拒绝 (避免无效操作)");

        // 校验: 不允许 new == previous (会回滚)
        if (oldToken is not null && newToken == oldToken)
            return Fail($"新 token 与旧 token 相同, 拒绝 (回滚操作)");

        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine("[dry-run] 不写入 DB, 操作预览完成");
            return 0;
        }

        // 写 DB (upsert) + NOTIFY
        // P7.1 BUG FIX: 之前用 $@"NOTIFY channel, ""{payload}""" 字符串拼接, PG 把 JSON 里的双引号
        //   当作 NOTIFY 字符串的边界, 触发 42601 语法错误 (反斜杠转义也不行)
        //   改用参数化调用 pg_notify('channel', $1::text), 由驱动负责引号/转义
        var now = DateTime.UtcNow;
        await using var conn = new NpgsqlConnection(pgConn);
        await conn.OpenAsync();
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
            cmd.Parameters.AddWithValue("cur", newToken);
            cmd.Parameters.AddWithValue("prev", (object?)oldToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("at", now);
            cmd.Parameters.AddWithValue("by", (object?)by ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        // PG NOTIFY 广播 — 触发其他实例重载 (参数化, 避免 JSON 引号语法错误)
        var payload = JsonSerializer.Serialize(new
        {
            current = newToken,
            previous = oldToken,
            rotatedAt = now,
            rotatedBy = by
        });
        await using (var notifyCmd = conn.CreateCommand())
        {
            notifyCmd.CommandText = "SELECT pg_notify('auth_token_rotated', @p)";
            notifyCmd.Parameters.AddWithValue("p", payload);
            await notifyCmd.ExecuteNonQueryAsync();
        }

        Console.WriteLine();
        Console.WriteLine($"[rotate-token] ✅ 成功");
        Console.WriteLine($"  DB 已更新 (auth_token_state.id=1)");
        Console.WriteLine($"  NOTIFY auth_token_rotated 已广播, 其他实例将自动重载");
        Console.WriteLine();
        Console.WriteLine($"下一步:");
        Console.WriteLine($"  1. 验证新 token: curl -H 'X-Admin-Token: {newToken[..Math.Min(8, newToken.Length)]}...' <api>/api/admin/auth/status");
        Console.WriteLine($"  2. 观察 24h, 确认没有 'PreviousKey 使用' 告警");
        Console.WriteLine($"  3. 过渡期结束后, 清空 appsettings.json:Auth:DevStaticTokenPrevious (后续 PR 改造中间件从 DB 读)");
        return 0;
    }

    private static async Task<int> ShowStatusAsync(string pgConn)
    {
        // WHY idempotent 建表: 运维查 status 时不必先启后端, 独立可用
        await EnsureTableAsync(pgConn);
        var (cur, prev, at, by) = await ReadStateAsync(pgConn);
        Console.WriteLine($"current  = {Mask(cur)} (长度 {cur?.Length ?? 0})");
        Console.WriteLine($"previous = {Mask(prev)} (长度 {prev?.Length ?? 0})");
        Console.WriteLine($"rotated_at = {at:O}");
        Console.WriteLine($"rotated_by = {by ?? "(无)"}");
        return 0;
    }

    private static async Task EnsureTableAsync(string pgConn)
    {
        // 与 AuthTokenStore.InitAsync 建表逻辑保持一致 (DDL 重复但幂等)
        await using var conn = new NpgsqlConnection(pgConn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS auth_token_state (
                id            smallint      PRIMARY KEY,
                current_key   varchar(128)  NOT NULL,
                previous_key  varchar(128),
                rotated_at    timestamptz,
                rotated_by    varchar(64)
            );";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(string? current, string? previous, DateTime? rotatedAt, string? rotatedBy)> ReadStateAsync(string pgConn)
    {
        await using var conn = new NpgsqlConnection(pgConn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT current_key, previous_key, rotated_at, rotated_by FROM auth_token_state WHERE id = 1";
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            );
        }
        return (null, null, null, null);
    }

    private static string Mask(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "(空)";
        // 只显示前 4 后 4 字符, 中间 ***
        if (token.Length <= 8) return "***";
        return $"{token[..4]}***{token[^4..]}";
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"[error] {message}");
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SakuraFilter.Cli - 运维工具");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  rotate-token --new <token> [--old <token>] [--by <user>] [--dry-run] [--pg-conn <conn>]");
        Console.WriteLine("  status [--pg-conn <conn>]");
        Console.WriteLine();
        Console.WriteLine("子命令:");
        Console.WriteLine("  rotate-token  轮转 X-Admin-Token (写 DB + 广播 NOTIFY)");
        Console.WriteLine("  status        查看 DB 当前状态 (不写)");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  --new         必填, 新 token (≥ 32 字符)");
        Console.WriteLine("  --old         可选, 旧 token (作为 previous 保留过渡)");
        Console.WriteLine("  --by          可选, 操作人 (审计用, 默认 anonymous)");
        Console.WriteLine("  --dry-run     只预览, 不写 DB");
        Console.WriteLine("  --pg-conn     可选, PG 连接串 (覆盖 appsettings.json)");
        Console.WriteLine();
        Console.WriteLine("环境变量:");
        Console.WriteLine("  SakuraFilter_Postgres   PG 连接串 (覆盖 appsettings.json)");
        Console.WriteLine();
        Console.WriteLine("轮转步骤 (4 步零停机):");
        Console.WriteLine("  1. 配 appsettings.json: DevStaticTokenPrevious=old + DevStaticToken=new");
        Console.WriteLine("  2. 部署 (滚动重启, 旧 token 仍可用)");
        Console.WriteLine("  3. 前端刷新, 用新 token 验证");
        Console.WriteLine("  4. CLI rotate-token --new <新token> --old <旧token> --by <运维>");
    }
}
