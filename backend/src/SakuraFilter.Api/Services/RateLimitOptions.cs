namespace SakuraFilter.Api.Services;

/// <summary>
/// Day 8.4: API 限流配置
/// 用法: Bind 自 appsettings.json "RateLimit" section
///   - GlobalPermitsPerMinute: 全局默认 (管理类路径)
///   - SearchPermitsPerMinute: /api/search 前台搜索
///   - EtlPermitsPerMinute: /api/etl ETL 触发
///   - AuthPermitsPerMinute: /api/auth/login 登录防暴力破解 (按 IP 分区)
/// 设计: .NET 8 内置 RateLimiter (System.Threading.RateLimiting)
///   - sliding window + token bucket 混合
///   - 超过限流返回 429 + Retry-After 头
///   - 限流不针对 IP, 简单按全局计数 (MVP 阶段; 生产应按用户/IP 区分)
/// </summary>
public class RateLimitOptions
{
    public bool Enabled { get; set; } = true;
    public int GlobalPermitsPerMinute { get; set; } = 600;
    public int SearchPermitsPerMinute { get; set; } = 300;
    public int EtlPermitsPerMinute { get; set; } = 30;
    public int AuthPermitsPerMinute { get; set; } = 5;
}
