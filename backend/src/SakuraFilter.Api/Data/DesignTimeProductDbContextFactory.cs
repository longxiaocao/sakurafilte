using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using SakuraFilter.Infrastructure.Data;

namespace SakuraFilter.Api.Data;

/// <summary>
/// EF Core 设计时 DbContext 工厂 (仅 dotnet ef migrations add/remove 使用)
///   WHY: Program.cs 用 NpgsqlDataSource 单例注册 ProductDbContext, 设计时 DI 无法解析
///   规则: 不参与运行时, 仅 dotnet ef CLI 工具调用
///   位置: 必须在 --startup-project 所在程序集 (SakuraFilter.Api)
/// </summary>
public class DesignTimeProductDbContextFactory : IDesignTimeDbContextFactory<ProductDbContext>
{
    public ProductDbContext CreateDbContext(string[] args)
    {
        // 从 SakuraFilter.Api 项目的 appsettings.json 读取连接字符串
        var basePath = Directory.GetCurrentDirectory();
        var cfg = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connStr = cfg.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未配置 (appsettings.json)");

        // WHY EnableLegacyTimestampBehavior: 与运行时一致, 避免 DateTime Kind 反序列化异常
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var opts = new DbContextOptionsBuilder<ProductDbContext>()
            .UseNpgsql(connStr)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ProductDbContext(opts);
    }
}
