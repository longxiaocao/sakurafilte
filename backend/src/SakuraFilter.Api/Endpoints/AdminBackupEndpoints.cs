using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SakuraFilter.Api.Extensions;

namespace SakuraFilter.Api.Endpoints;

/// <summary>
/// V3(2026-08-25) 用户反馈: 运维中心缺"备份"入口, 需查看/触发数据库备份
///   - GET  /api/admin/backup/list       列出 /backups 目录下的备份文件 (.dump)
///   - GET  /api/admin/backup/script-info 返回 backup-db.sh 在主机的预期位置 (前端展示执行指引)
///   注意: 不在 API 容器内 exec 备份脚本 — API 镜像无 docker CLI, 需挂 /var/run/docker.sock 有安全风险;
///         实际执行在主机: bash scripts/backup-db.sh --verify --upload (运维文档)
/// 挂载: compose api 服务 volumes 加 ./_backups:/backups:ro (api 容器只读访问备份目录)
/// </summary>
public static class AdminBackupEndpoints
{
    public static IEndpointRouteBuilder MapAdminBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/backup").WithTags("AdminBackup")
            .RequireAuthorization("Admin")
            .RequireRateLimiting("etl");  // 与 ETL 同限流策略 (运维类)

        group.MapGet("/list", () =>
        {
            var dir = "/backups";
            if (!Directory.Exists(dir))
                return Results.Ok(new { dir, exists = false, files = Array.Empty<object>() });

            var files = new DirectoryInfo(dir)
                .GetFiles("*.dump")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new
                {
                    name = f.Name,
                    sizeBytes = f.Length,
                    sizeHuman = FormatSize(f.Length),
                    createdAt = f.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
                })
                .ToArray();

            return Results.Ok(new { dir, exists = true, count = files.Length, files });
        });

        group.MapGet("/script-info", () =>
        {
            // 前端展示执行指引 (实际执行需在主机, 容器内 exec 有安全风险)
            return Results.Ok(new
            {
                hostCommand = "bash scripts/backup-db.sh --verify --upload",
                note = "执行需要在部署主机 (非容器内), 上传异机对象存储需配置 BACKUP_S3_ENDPOINT"
            });
        });

        return app;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:F2} {units[i]}";
    }
}
