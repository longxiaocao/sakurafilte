using System.Security.Cryptography;
using System.Text;

namespace SakuraFilter.Api.Services;

/// <summary>
/// Day 8.3 cursor HMAC 签名工具
/// 用途: 防客户端篡改 cursor (改 updatedAt/id 越权访问任意产品位置)
/// 设计:
///   - HMAC-SHA256(secret, payload) → 32 字节 → Base64URL → 截断 16 字符 (≈96 位强度)
///   - 旧格式 cursor: "&lt;ISO8601 updatedAt&gt;|&lt;mr1&gt;|&lt;sig16&gt;" (内部 AuditLog 用, 无 TTL)
///   - V2 格式 cursor: "v2:&lt;expUnixTs&gt;|&lt;tsB64&gt;|&lt;mr1B64&gt;|&lt;pageNum&gt;|&lt;sig16&gt;" (公开搜索用, 24h TTL)
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
///
/// V24-F24 (spec S3-13/S3-14/F2-2/F2-10/F2-17): V2 双签名重载
///   WHY: 旧 cursor 无 TTL + 无 pageNum + 验签顺序错误 (TTL 在 HMAC 之前) + 字段未统一 Base64Url
///   修复:
///     - 新增 SignV2 / VerifyAndExtractV2 (5 段格式 + v2: 前缀 + 24h TTL + pageNum + Base64Url)
///     - 验签顺序: 先 HMAC 后 TTL (F2-2)
///     - LEGACY_CUTOFF_TS: 旧格式公开 cursor 在 2025-07-25 后拒绝 (S3-13)
///     - pageNum > 1000 拒绝 (S3-24)
///   兼容: 旧 Sign/VerifyAndExtract 保留, 供内部 AuditLog cursor 使用 (ticks|id|sig, 无 TTL)
/// </summary>
public class CursorHmac
{
    /// <summary>
    /// V24-F24 (spec S3-13): 旧格式公开 cursor 过渡期截止时间戳
    ///   2025-07-25 00:00:00 UTC = 1753372800
    ///   过期后旧格式公开 cursor 全部拒绝 (VerifyAndExtractV2 不接受非 v2: 前缀)
    ///   注意: 旧 Sign/VerifyAndExtract 不受此常量影响 (内部 AuditLog cursor 仍可用)
    /// </summary>
    private const long LEGACY_CUTOFF_TS = 1753372800;

    /// <summary>V24-F24 (spec S3-14): V2 cursor TTL = 24 小时 (86400 秒)</summary>
    private const int V2_TTL_SECONDS = 86400;

    /// <summary>V24-F24 (spec S3-24): V2 cursor 最大 pageNum (防深翻页 DoS)</summary>
    private const int V2_MAX_PAGE_NUM = 1000;

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

    // ========== 旧签名 (供内部 AuditLog 用, ticks|id|sig 格式, 无 TTL) ==========

    /// <summary>
    /// 给 (updatedAt, mr1) 签名, 返回截断的 Base64URL 字符串
    /// Day 9.6: 始终用 CurrentKey 签名 (避免新 cursor 走 PreviousKey 导致后续 CurrentKey 切换时再失效)
    /// V2 Task 4.6: 签名载荷从 long id 改为 string mr1 (修复漏洞 6: cursor 不暴露内部自增 Id)
    ///   WHY: V2 对外主键改用 mr1, cursor 内若含 long Id 会泄露内部自增位置 (信息泄漏)
    ///   兼容: 第二个参数语义为 "唯一载荷字符串", 调用方可传 mr1 或 id.ToString() (历史记录场景)
    /// V24-F24: 此方法保留供内部 AuditLog cursor 使用 (EncodeCursor/DecodeCursor 路径)
    ///   公开搜索 cursor 应改用 SignV2 (spec S3-14)
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
    /// 验证 cursor 三段式格式: &lt;ISO8601&gt;|&lt;mr1&gt;|&lt;sig16&gt;
    /// 验证失败抛 ArgumentException (由 Endpoint 转 400)
    /// Day 9.6: 双 key 验证 — CurrentKey 不过试 PreviousKey (轮转过渡期兼容)
    /// V2 Task 4.6: 返回值从 (string, long id) 改为 (string, string mr1)
    /// V24-F24: 此方法保留供内部 AuditLog cursor 使用, 公开搜索应改用 VerifyAndExtractV2
    ///   注意: 此方法不做 LEGACY_CUTOFF_TS 检查 (内部 cursor 无过渡期概念)
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

    // ========== V24-F24: V2 签名 (供公开搜索用, v2: 前缀 + 24h TTL + pageNum + Base64Url) ==========

    /// <summary>
    /// V24-F24 (spec S3-14): V2 cursor 签名
    ///   格式: "v2:&lt;expUnixTs&gt;|&lt;tsB64&gt;|&lt;mr1B64&gt;|&lt;pageNum&gt;|&lt;sig16&gt;"
    ///   - v2: 前缀: 标识新格式 (VerifyAndExtractV2 据此分支)
    ///   - expUnixTs: 过期时间戳 (now + 24h), 用于 TTL 检查
    ///   - tsB64: updatedAtIso 的 Base64Url 编码 (F2-17: 统一编码, 防 | 截断)
    ///   - mr1B64: mr1 的 Base64Url 编码
    ///   - pageNum: 当前页码 (S3-24: > 1000 拒绝)
    ///   - sig16: HMAC-SHA256(CurrentKey, "v2:expUnixTs|tsB64|mr1B64|pageNum") 截断 16 字符
    /// </summary>
    /// <param name="updatedAtIso">ISO 8601 updatedAt 字符串 (keyset 排序用)</param>
    /// <param name="mr1">产品 MR.1 (V2 主键, 1-10 位字母数字)</param>
    /// <param name="pageNum">当前页码 (1-based, > 1000 拒绝)</param>
    /// <returns>V2 格式 cursor 字符串</returns>
    public string SignV2(string updatedAtIso, string mr1, int pageNum)
    {
        if (string.IsNullOrEmpty(mr1))
            throw new ArgumentException("mr1 载荷不能为空", nameof(mr1));
        if (pageNum < 1)
            throw new ArgumentException($"pageNum 不能小于 1 (实际: {pageNum})", nameof(pageNum));
        if (pageNum > V2_MAX_PAGE_NUM)
            throw new ArgumentException($"CURSOR_PAGE_TOO_DEEP: pageNum={pageNum} 超过最大值 {V2_MAX_PAGE_NUM}");

        var expUnixTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + V2_TTL_SECONDS;
        var tsB64 = Base64UrlEncode(updatedAtIso);
        var mr1B64 = Base64UrlEncode(mr1);
        var payload = $"v2:{expUnixTs}|{tsB64}|{mr1B64}|{pageNum}";
        var hash = HMACSHA256.HashData(_currentKey, Encoding.UTF8.GetBytes(payload));
        var sig = ToBase64Url(hash)[..16];
        return $"{payload}|{sig}";
    }

    /// <summary>
    /// V24-F24 (spec S3-13/S3-14/F2-2): V2 cursor 验签
    ///   验签顺序 (F2-2 修复): (1) 格式检查 (2) HMAC 验签 (3) TTL 检查 (4) pageNum 检查
    ///   WHY 先 HMAC 后 TTL: 防攻击者构造 v2:{未来时间戳}|x|y|z 让 TTL 通过触发业务解析
    ///   双 key 验签 (S3-9): CurrentKey 不过试 PreviousKey
    ///   旧格式拒绝 (S3-13): 非 v2: 前缀直接 CURSOR_INVALID (LEGACY_CUTOFF_TS 已过)
    /// </summary>
    /// <param name="cursor">V2 格式 cursor 字符串</param>
    /// <returns>(updatedAtIso, mr1, pageNum) 三元组</returns>
    /// <exception cref="ArgumentException">验签失败 / TTL 过期 / pageNum 过大</exception>
    public (string updatedAtIso, string mr1, int pageNum) VerifyAndExtractV2(string cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            throw new ArgumentException("CURSOR_INVALID: cursor 为空");

        // S3-13: 旧格式 (非 v2: 前缀) 直接拒绝
        //   WHY: LEGACY_CUTOFF_TS = 2025-07-25 已过, 旧格式公开 cursor 全部拒绝
        //   内部 AuditLog cursor 走 VerifyAndExtract (非此方法), 不受影响
        if (!cursor.StartsWith("v2:", StringComparison.Ordinal))
            throw new ArgumentException("CURSOR_INVALID: 非 v2: 前缀 (旧格式已废弃)");

        var parts = cursor.Split('|');
        // v2:expUnixTs | tsB64 | mr1B64 | pageNum | sig → 5 段
        if (parts.Length != 5)
            throw new ArgumentException($"CURSOR_INVALID: 期望 5 段, 实际 {parts.Length} 段");

        var prefixWithExp = parts[0];  // "v2:1234567890"
        var tsB64 = parts[1];
        var mr1B64 = parts[2];
        var pageNumStr = parts[3];
        var sig = parts[4];

        // F2-2 修复: 先验签, 后 TTL
        //   重组 payload (不含 sig), 用 CurrentKey → PreviousKey 验签
        var payload = $"{prefixWithExp}|{tsB64}|{mr1B64}|{pageNumStr}";
        if (!VerifyKeyV2(_currentKey, payload, sig)
            && !(_previousKey != null && VerifyKeyV2(_previousKey, payload, sig)))
            throw new ArgumentException("CURSOR_INVALID: 签名验证失败 (可能被篡改或使用过期 secret)");

        // 验签通过后再做 TTL 检查
        if (!long.TryParse(prefixWithExp.AsSpan("v2:".Length), out var expUnixTs))
            throw new ArgumentException("CURSOR_INVALID: expUnixTs 解析失败");
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expUnixTs)
            throw new ArgumentException("CURSOR_EXPIRED: cursor 已过期 (TTL=24h)");

        // S3-24: pageNum > 1000 拒绝 (防深翻页 DoS)
        if (!int.TryParse(pageNumStr, out var pageNum))
            throw new ArgumentException("CURSOR_INVALID: pageNum 解析失败");
        if (pageNum < 1)
            throw new ArgumentException($"CURSOR_INVALID: pageNum={pageNum} < 1");
        if (pageNum > V2_MAX_PAGE_NUM)
            throw new ArgumentException($"CURSOR_PAGE_TOO_DEEP: pageNum={pageNum} 超过最大值 {V2_MAX_PAGE_NUM}");

        // 解码 Base64Url 字段
        var updatedAtIso = Base64UrlDecode(tsB64);
        var mr1 = Base64UrlDecode(mr1B64);
        if (string.IsNullOrEmpty(mr1))
            throw new ArgumentException("CURSOR_INVALID: mr1 解码后为空");

        return (updatedAtIso, mr1, pageNum);
    }

    /// <summary>V2 cursor 验签辅助: 用指定 key 重算签名并常时比较</summary>
    private static bool VerifyKeyV2(byte[] key, string payload, string sig)
    {
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        var expected = ToBase64Url(hash)[..16];
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(sig),
            Encoding.ASCII.GetBytes(expected));
    }

    // ========== Base64Url 编解码辅助 (F2-17: 统一编码, 防 | 截断) ==========

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// F2-17: 字符串 → Base64Url 编码 (防 | 截断 cursor)
    ///   WHY: updatedAtIso / mr1 若直接拼接到 cursor, 含 | 字符会破坏 Split('|') 解析
    ///   Base64Url 编码后只含 [A-Za-z0-9_-], 不含 |, 安全拼接
    /// </summary>
    private static string Base64UrlEncode(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        return ToBase64Url(bytes);
    }

    /// <summary>
    /// F2-17: Base64Url → 字符串解码 (Base64UrlEncode 的逆操作)
    /// </summary>
    private static string Base64UrlDecode(string s)
    {
        var base64 = s.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
            case 0: break;  // 已对齐
            case 1: throw new ArgumentException($"CURSOR_INVALID: Base64Url 长度 % 4 == 1 (非法)");
        }
        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }
}
