using System.Security.Cryptography;
using System.Text;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Day 8.3 cursor HMAC 签名工具
/// 用途: 防客户端篡改 cursor (改 updatedAt/id 越权访问任意产品位置)
/// 设计:
///   - HMAC-SHA256(secret, payload) → 32 字节 → Base64URL → 截断 16 字符 (≈96 位强度)
///   - cursor 格式: "<ISO8601 updatedAt>|<id>|<sig16>"
///   - 旧格式 (无 sig) 视作非法, 直接 400 拒绝
///   - secret 来自 appsettings.json Search:CursorHmacKey (生产请用环境变量覆盖)
/// </summary>
public class CursorHmac
{
    private readonly byte[] _key;
    private readonly ILogger<CursorHmac> _logger;

    public CursorHmac(IConfiguration config, ILogger<CursorHmac> logger)
    {
        _logger = logger;
        var secret = config["Search:CursorHmacKey"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // 不允许无 secret 启动, 避免"忘配 key 默默通过"的安全事故
            throw new InvalidOperationException(
                "Search:CursorHmacKey 未配置, 必须在 appsettings.json 或环境变量 Search__CursorHmacKey 提供");
        }
        if (secret.Length < 32)
        {
            // 太短的 key HMAC 抗碰撞能力不足, 强制要求 ≥ 32 字符
            throw new InvalidOperationException(
                $"Search:CursorHmacKey 长度 {secret.Length} < 32, 不安全, 请使用至少 32 字符的随机串");
        }
        _key = Encoding.UTF8.GetBytes(secret);
        _logger.LogInformation("CursorHmac 初始化完成, key 长度 {Len} 字节", _key.Length);
    }

    /// <summary>
    /// 给 (updatedAt, id) 签名, 返回截断的 Base64URL 字符串
    /// </summary>
    public string Sign(string updatedAtIso, long id)
    {
        var payload = $"{updatedAtIso}|{id}";
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return ToBase64Url(hash)[..16]; // 截断到 16 字符, 约 96 位安全强度, 够用且 URL 友好
    }

    /// <summary>
    /// 验证 cursor 三段式格式: <ISO8601>|<id>|<sig16>
    /// 验证失败抛 ArgumentException (由 Endpoint 转 400)
    /// </summary>
    public (string updatedAtIso, long id) VerifyAndExtract(string cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            throw new ArgumentException("cursor 为空");

        var parts = cursor.Split('|', 3);
        if (parts.Length != 3)
            throw new ArgumentException($"cursor 格式错, 期望 <ISO8601 updatedAt>|<id>|<sig16>, 实际: {cursor}");

        var updatedAtIso = parts[0];
        var idStr = parts[1];
        var sig = parts[2];

        if (!long.TryParse(idStr, out var id))
            throw new ArgumentException($"cursor id 段解析失败, 实际: {idStr}");

        // 重算签名比对
        var expected = Sign(updatedAtIso, id);
        // CryptographicOperations.FixedTimeEquals 抗时序攻击
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(sig),
                Encoding.ASCII.GetBytes(expected)))
        {
            throw new ArgumentException("cursor 签名验证失败, 可能被篡改或使用过期 secret");
        }
        return (updatedAtIso, id);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
