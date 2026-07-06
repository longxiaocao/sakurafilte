namespace SakuraFilter.Api.DTOs;

// P5.5: 前端性能埋点批量上报 DTO
//   ts: 客户端时间戳 (ISO8601 string, 简化绑定, 后端不强制解析)
//   字段全部 nullable + 服务层 ?? 兜底 (与 AdminProductSearchRequest 一致)
public record FrontendPerfSample(string? Path, string? Method, int? StatusCode, double? DurationMs, string? Ts);
public record FrontendPerfBatch(List<FrontendPerfSample>? Samples);
