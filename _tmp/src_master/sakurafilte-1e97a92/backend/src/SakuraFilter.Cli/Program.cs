// P7.1: X-Admin-Token 双 Key 轮转 CLI
//   子命令:
//     rotate-token --new <token> [--old <token>] [--by <user>] [--dry-run] [--pg-conn <conn>]
//       写 DB (auth_token_state) + 立即从 DB 重载 (本地 instance 内存更新)
//       不修改 appsettings.json (无重启副作用, 不污染 git 跟踪)
//     status [--pg-conn <conn>]
//       打印当前 DB 状态 (current 前缀 + previous 前缀 + rotated_at + rotated_by)
// V24-F89 (v27-2): cleanup-orphan-images 子命令
//   枚举 S3/MinIO/OSS 存储桶中所有对象, 与 DB product_images.image_key 比对, 删除孤儿
//   选项 C (spec 26.4.1): 不引入 BackgroundService, 一次性 CLI 脚本, 适合低频运维场景
//     --pg-conn <conn>     PG 连接串 (覆盖 appsettings.json)
//     --storage <minio|oss>  存储类型 (默认 minio)
//     --endpoint <url>     存储端点 (MinIO: http://localhost:9000, OSS: oss-cn-hangzhou.aliyuncs.com)
//     --bucket <name>      存储桶名
//     --access-key <key>   访问 key
//     --secret-key <key>   密钥
//     --prefix <prefix>    仅扫描指定前缀 (默认 "products/", 避免误删非产品对象)
//     --dry-run            只列出孤儿, 不删除
//     --batch-size <n>     每批删除数量 (默认 100, 防止单次 DELETE 过多触发限流)
//   使用场景:
//     1. V24-F84 SafeDeleteOldImageAsync 3 次重试失败产生的孤儿
//     2. 历史数据迁移遗留的废弃 key
//     3. 测试环境清理 (CI 每次跑后清桶)
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Minio;
using Npgsql;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Storage;

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
            "cleanup-orphan-images" => await CleanupOrphanImagesAsync(args, config, pgConn),
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
        Console.WriteLine("  cleanup-orphan-images [--storage <minio|oss>] [--endpoint <url>] [--bucket <name>]");
        Console.WriteLine("                        [--access-key <k>] [--secret-key <k>] [--prefix <p>]");
        Console.WriteLine("                        [--dry-run] [--batch-size <n>] [--pg-conn <conn>]");
        Console.WriteLine();
        Console.WriteLine("子命令:");
        Console.WriteLine("  rotate-token           轮转 X-Admin-Token (写 DB + 广播 NOTIFY)");
        Console.WriteLine("  status                 查看 DB 当前状态 (不写)");
        Console.WriteLine("  cleanup-orphan-images  扫描存储桶与 DB 比对, 删除孤儿对象 (V24-F89 v27-2)");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  --new         必填, 新 token (≥ 32 字符)");
        Console.WriteLine("  --old         可选, 旧 token (作为 previous 保留过渡)");
        Console.WriteLine("  --by          可选, 操作人 (审计用, 默认 anonymous)");
        Console.WriteLine("  --dry-run     只预览, 不写 DB / 不删对象");
        Console.WriteLine("  --pg-conn     可选, PG 连接串 (覆盖 appsettings.json)");
        Console.WriteLine("  --storage     存储类型: minio (默认) | oss");
        Console.WriteLine("  --endpoint    存储端点 (MinIO 默认 http://localhost:9000)");
        Console.WriteLine("  --bucket      存储桶名 (默认 sakurafilter)");
        Console.WriteLine("  --access-key  访问 key (默认 minioadmin)");
        Console.WriteLine("  --secret-key  密钥 (默认 minioadmin)");
        Console.WriteLine("  --prefix      扫描前缀 (默认 products/, 避免误删非产品对象)");
        Console.WriteLine("  --batch-size  每批删除数量 (默认 100, 防限流)");
        Console.WriteLine();
        Console.WriteLine("环境变量:");
        Console.WriteLine("  SakuraFilter_Postgres   PG 连接串 (覆盖 appsettings.json)");
        Console.WriteLine("  Storage__Provider       存储类型 (minio/oss, 与后端配置一致)");
        Console.WriteLine("  Minio__Endpoint / Minio__AccessKey / Minio__SecretKey / Minio__Bucket");
        Console.WriteLine("  Oss__Endpoint / Oss__AccessKey / Oss__SecretKey / Oss__Bucket");
        Console.WriteLine();
        Console.WriteLine("轮转步骤 (4 步零停机):");
        Console.WriteLine("  1. 配 appsettings.json: DevStaticTokenPrevious=old + DevStaticToken=new");
        Console.WriteLine("  2. 部署 (滚动重启, 旧 token 仍可用)");
        Console.WriteLine("  3. 前端刷新, 用新 token 验证");
        Console.WriteLine("  4. CLI rotate-token --new <新token> --old <旧token> --by <运维>");
        Console.WriteLine();
        Console.WriteLine("孤儿清理典型用法:");
        Console.WriteLine("  # 预览 (不删除, 只列出孤儿)");
        Console.WriteLine("  dotnet run -- cleanup-orphan-images --dry-run");
        Console.WriteLine("  # 实际删除");
        Console.WriteLine("  dotnet run -- cleanup-orphan-images --prefix products/");
    }

    // ============================================================================
    // V24-F89 (v27-2): cleanup-orphan-images 子命令实现
    // ============================================================================

    private static async Task<int> CleanupOrphanImagesAsync(string[] args, IConfiguration config, string pgConn)
    {
        // 解析参数 (?? 兜底确保非 null, 避免后续 storage 构造时 CS8602 警告)
        var storageType = ResolveArg(args, "--storage", config["Storage:Provider"] ?? "minio") ?? "minio";
        // MinIO endpoint 不含 scheme (用 UseSSL 控制), OSS endpoint 是完整域名
        //   WHY: MinIO SDK WithEndpoint 要求 "host:port" 格式, 含 http:// 会抛 InvalidEndpointException
        //   OSS SDK OssClient(endpoint, ...) 接受完整域名 (如 oss-cn-hangzhou.aliyuncs.com)
        var defaultEndpoint = storageType == "oss" ? "oss-cn-hangzhou.aliyuncs.com" : "localhost:9000";
        var endpoint = ResolveArg(args, "--endpoint",
            storageType == "oss" ? config["Oss:Endpoint"] : config["Minio:Endpoint"]) ?? defaultEndpoint;
        // 兼容用户误传 http:// 前缀 (MinIO 场景自动剥离)
        if (storageType != "oss" && (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                      endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            endpoint = endpoint.Substring(endpoint.IndexOf("://") + 3);
        }
        var bucket = ResolveArg(args, "--bucket",
            storageType == "oss" ? config["Oss:Bucket"] : config["Minio:Bucket"]) ?? "sakurafilter";
        var accessKey = ResolveArg(args, "--access-key",
            storageType == "oss" ? config["Oss:AccessKey"] : config["Minio:AccessKey"]) ?? "minioadmin";
        var secretKey = ResolveArg(args, "--secret-key",
            storageType == "oss" ? config["Oss:SecretKey"] : config["Minio:SecretKey"]) ?? "minioadmin";
        var prefix = ResolveArg(args, "--prefix", "products/") ?? "products/";
        var dryRun = Array.IndexOf(args, "--dry-run") >= 0;
        var batchSizeStr = ResolveArg(args, "--batch-size", "100");
        if (!int.TryParse(batchSizeStr, out var batchSize) || batchSize <= 0)
            return Fail($"--batch-size 值无效: {batchSizeStr}");
        batchSize = Math.Min(batchSize, 1000);  // 上限 1000 防 OSS 限流

        Console.WriteLine($"[cleanup-orphan-images] 配置:");
        Console.WriteLine($"  storage     = {storageType}");
        Console.WriteLine($"  endpoint    = {endpoint}");
        Console.WriteLine($"  bucket      = {bucket}");
        Console.WriteLine($"  prefix      = {prefix}");
        Console.WriteLine($"  dry-run     = {dryRun}");
        Console.WriteLine($"  batch-size  = {batchSize}");
        Console.WriteLine($"  pg-conn     = {Mask(pgConn)}");
        Console.WriteLine();

        // 1. 构造 IObjectStorage 实例 (直接 new, 不走 DI)
        var storageTypeNorm = storageType.ToLowerInvariant();
        IObjectStorage storage = storageTypeNorm switch
        {
            "minio" => CreateMinioStorage(endpoint, bucket, accessKey, secretKey),
            "oss" => CreateOssStorage(endpoint, bucket, accessKey, secretKey, config["Oss:CdnEndpoint"]),
            _ => throw new ArgumentException($"不支持的 storage 类型: {storageType}")
        };

        // 2. 从 DB 拉取所有 product_images.image_key + products.image_key (主图兼容字段)
        //   WHY 同时拉两个表: products.image_key 是 V1 兼容字段, 仍可能有引用
        //   DISTINCT 去重: 同一 key 可能出现在 product_images + products 两表
        Console.WriteLine("[1/3] 从 DB 拉取所有 image_key...");
        var dbKeys = new HashSet<string>();
        await using (var conn = new NpgsqlConnection(pgConn))
        {
            await conn.OpenAsync();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT image_key FROM product_images WHERE image_key IS NOT NULL
                    UNION
                    SELECT image_key FROM products WHERE image_key IS NOT NULL";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    dbKeys.Add(reader.GetString(0));
                }
            }
        }
        Console.WriteLine($"  DB 共 {dbKeys.Count} 个有效 image_key");

        // 3. 枚举存储桶中所有对象 (按 prefix 过滤)
        Console.WriteLine($"[2/3] 枚举存储桶 {bucket}/{prefix} ...");
        List<string> objectKeys;
        try
        {
            objectKeys = (await storage.ListAsync(prefix ?? "", CancellationToken.None)).ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] 枚举存储桶失败: {ex.Message}");
            Console.Error.WriteLine("  请检查 endpoint/bucket/access-key/secret-key 是否正确, 以及存储服务是否可达");
            return 1;
        }
        Console.WriteLine($"  存储桶共 {objectKeys.Count} 个对象 (前缀 {prefix})");

        // 4. 比对找孤儿
        Console.WriteLine("[3/3] 比对找孤儿...");
        var orphans = objectKeys.Where(k => !dbKeys.Contains(k)).ToList();
        Console.WriteLine($"  发现 {orphans.Count} 个孤儿对象");
        Console.WriteLine();

        if (orphans.Count == 0)
        {
            Console.WriteLine("✅ 无孤儿对象, 操作完成");
            return 0;
        }

        // 列出前 20 个孤儿预览
        var previewCount = Math.Min(20, orphans.Count);
        Console.WriteLine($"孤儿预览 (前 {previewCount} 个, 共 {orphans.Count} 个):");
        for (var i = 0; i < previewCount; i++)
        {
            Console.WriteLine($"  {i + 1}. {orphans[i]}");
        }
        if (orphans.Count > previewCount)
        {
            Console.WriteLine($"  ... 还有 {orphans.Count - previewCount} 个未显示");
        }
        Console.WriteLine();

        if (dryRun)
        {
            Console.WriteLine($"[dry-run] 不删除对象, 操作预览完成");
            return 0;
        }

        // 5. 批量删除 (按 batch_size 分批, 每批后短暂 sleep 防限流)
        Console.WriteLine($"开始删除 {orphans.Count} 个孤儿对象 (batch_size={batchSize})...");
        var deleted = 0;
        var failed = 0;
        for (var i = 0; i < orphans.Count; i++)
        {
            var key = orphans[i];
            try
            {
                await storage.DeleteAsync(key);
                deleted++;
                if (deleted % batchSize == 0)
                {
                    Console.WriteLine($"  已删除 {deleted}/{orphans.Count}...");
                    await Task.Delay(500);  // 防限流
                }
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"  [warn] 删除失败 key={key}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"✅ 清理完成: 删除 {deleted} 个, 失败 {failed} 个 (总计 {orphans.Count} 个孤儿)");
        return failed > 0 ? 2 : 0;  // 部分失败返回 2 (非 0 但区分于参数错误 1)
    }

    /// <summary>解析 --key value 形式的命令行参数</summary>
    private static string? ResolveArg(string[] args, string key, string? defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key) return args[i + 1];
        }
        return defaultValue;
    }

    private static MinioStorage CreateMinioStorage(string endpoint, string bucket, string accessKey, string secretKey)
    {
        // WHY new MinioClient: CLI 不走 DI, 直接构造 (与 Production ServiceProvider 配置一致)
        //   endpoint 格式: host:port (不含 scheme), WithSSL 控制协议
        //   如用户传 https://minio.example.com:9000, 调用前已剥离 scheme 为 minio.example.com:9000
        var useSSL = false;  // CLI 默认不启用 SSL (本地 MinIO 场景), 生产环境通过 --endpoint + 显式配置
        var client = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(useSSL)
            .Build();
        return new MinioStorage(client, bucket, endpoint);
    }

    private static AliyunOssStorage CreateOssStorage(
        string endpoint, string bucket, string accessKey, string secretKey, string? cdnEndpoint)
    {
        var client = new Aliyun.OSS.OssClient(endpoint, accessKey, secretKey);
        return new AliyunOssStorage(client, bucket, endpoint, cdnEndpoint);
    }
}
