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
///
/// Day 9.6: 双 key 轮转支持 (无侵入)
///   WHY: 单纯换 key 后, 旧 cursor 全部失效 → 翻页中断 → 用户体验断崖
///   解法:
///     - 启动时加载 CurrentKey + PreviousKey(可选) 两个 key
///     - 编码 cursor 始终用 CurrentKey
///     - 验签按 CurrentKey → PreviousKey 顺序尝试, 任一通过即可
///     - 轮转步骤: ops 配 PreviousKey = 旧 key + CurrentKey = 新 key → 部署 → 等 24h 旧 cursor 全部失效 → 清空 PreviousKey
///   副作用: 验签 O(n) (n=2, 几乎无开销)
/// </summary>
public class CursorHmac
{
    private readonly byte[] _currentKey;
    private readonly byte[]? _previousKey;
    private readonly ILogger<CursorHmac> _logger;

    public CursorHmac(IConfiguration config, ILogger<CursorHmac> logger)
    {
        _logger = logger;
        var current = config["Search:CursorHmacKey"];
        if (string.IsNullOrWhiteSpace(current))
        {
            // 不允许无 secret 启动, 避免"忘配 key 默默通过"的安全事故
            throw new InvalidOperationException(
                "Search:CursorHmacKey 未配置, 必须在 appsettings.json 或环境变量 Search__CursorHmacKey 提供");
        }
        if (current.Length < 32)
        {
            // 太短的 key HMAC 抗碰撞能力不足, 强制要求 ≥ 32 字符
            throw new InvalidOperationException(
                $"Search:CursorHmacKey 长度 {current.Length} < 32, 不安全, 请使用至少 32 字符的随机串");
        }
        _currentKey = Encoding.UTF8.GetBytes(current);

        // Day 9.6: 可选 previous key (轮转过渡期, 验签时也接受它)
        var previous = config["Search:CursorHmacKeyPrevious"];
        if (!string.IsNullOrWhiteSpace(previous))
        {
            if (previous.Length < 32)
            {
                throw new InvalidOperationException(
                    $"Search:CursorHmacKeyPrevious 长度 {previous.Length} < 32, 不安全");
            }
            if (previous == current)
            {
                _logger.LogWarning("CursorHmac CurrentKey 与 PreviousKey 相同, PreviousKey 忽略");
            }
            else
            {
                _previousKey = Encoding.UTF8.GetBytes(previous);
                _logger.LogInformation("CursorHmac 双 key 模式: 当前 key 长度 {Cur}, 旧 key 长度 {Prev} 字节",
                    _currentKey.Length, _previousKey.Length);
                _logger.LogInformation("提示: 轮转过渡期 (建议 24h) 后清空 Search:CursorHmacKeyPrevious 减小验签开销");
            }
        }

        _logger.LogInformation("CursorHmac 初始化完成, key 长度 {Len} 字节", _currentKey.Length);
    }

    /// <summary>
    /// 给 (updatedAt, mr1) 签名, 返回截断的 Base64URL 字符串
    /// Day 9.6: 始终用 CurrentKey 签名 (避免新 cursor 走 PreviousKey 导致后续 CurrentKey 切换时再失效)
    /// V2 Task 4.6: 签名载荷从 long id 改为 string mr1 (修复漏洞 6: cursor 不暴露内部自增 Id)
    ///   WHY: V2 对外主键改用 mr1, cursor 内若含 long Id 会泄露内部自增位置 (信息泄漏)
    ///   兼容: 第二个参数语义为 "唯一载荷字符串", 调用方可传 mr1 或 id.ToString() (历史记录场景)
    /// </summary>
    public string Sign(string updatedAtIso, string mr1)
    {
        if (string.IsNullOrEmpty(mr1))
            throw new ArgumentException("mr1 载荷不能为空", nameof(mr1));
        var payload = $"{updatedAtIso}|{mr1}";
        var hash = HMACSHA256.HashData(_currentKey, Encoding.UTF8.GetBytes(payload));
        return ToBase64Url(hash)[..16]; // 截断到 16 字符, 约 96 位安全强度, 够用且 URL 友好
    }

    /// <summary>
    /// 验证 cursor 三段式格式: <ISO8601>|<mr1>|<sig16>
    /// 验证失败抛 ArgumentException (由 Endpoint 转 400)
    /// Day 9.6: 双 key 验证 — CurrentKey 不过试 PreviousKey (轮转过渡期兼容)
    /// V2 Task 4.6: 返回值从 (string, long id) 改为 (string, string mr1)
    /// </summary>
    public (string updatedAtIso, string mr1) VerifyAndExtract(string cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            throw new ArgumentException("cursor 为空");

        var parts = cursor.Split('|', 3);
        if (parts.Length != 3)
            throw new ArgumentException($"cursor 格式错, 期望 <ISO8601 updatedAt>|<mr1>|<sig16>, 实际: {cursor}");

        var updatedAtIso = parts[0];
        var mr1 = parts[1];
        var sig = parts[2];

        if (string.IsNullOrEmpty(mr1))
            throw new ArgumentException($"cursor mr1 段为空, 实际: {mr1}");

        // Day 9.6: 双 key 验证
        // 先试 CurrentKey (绝大多数情况), 不过再试 PreviousKey
        if (VerifyKey(_currentKey, updatedAtIso, mr1, sig))
            return (updatedAtIso, mr1);
        if (_previousKey != null && VerifyKey(_previousKey, updatedAtIso, mr1, sig))
        {
            _logger.LogDebug("cursor 用 PreviousKey 验签通过 (轮转过渡期)");
            return (updatedAtIso, mr1);
        }
        throw new ArgumentException("cursor 签名验证失败, 可能被篡改或使用过期 secret");
    }

    /// <summary>
    /// 用指定 key 重算签名并常时比较 (抗时序攻击)
    /// V2 Task 4.6: 载荷类型从 long id 改为 string mr1
    /// </summary>
    private static bool VerifyKey(byte[] key, string updatedAtIso, string mr1, string sig)
    {
        var payload = $"{updatedAtIso}|{mr1}";
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        var expected = ToBase64Url(hash)[..16];
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(sig),
            Encoding.ASCII.GetBytes(expected));
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
