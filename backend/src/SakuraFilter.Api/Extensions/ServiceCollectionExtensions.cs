using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Minio;
using Aliyun.OSS;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using SakuraFilter.Api.Services;
using SakuraFilter.Core.Interfaces;
using SakuraFilter.Infrastructure.Data;
using SakuraFilter.Infrastructure.Storage;
using SakuraFilter.Search;
using SakuraFilter.Etl;
using SakuraFilter.Core.DTOs;

namespace SakuraFilter.Api.Extensions;

/// <summary>
/// 统一服务注册扩展。
/// 将 Program.cs 中的 DI 注册逻辑按职责拆分为私有方法，
/// 避免启动文件过于臃肿，违反单一职责。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSakuraFilterServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        services.AddApiDocumentation();
        services.AddAuthServices(configuration);
        services.AddValidationServices();
        services.AddDatabaseServices(configuration);
        services.AddSearchServices(configuration);
        services.AddEtlServices(configuration);
        services.AddStorageServices(configuration);
        services.AddCorsServices(configuration);
        services.AddRateLimitServices(configuration);
        services.AddBusinessServices();
        services.AddHostedServices();
        services.AddInfrastructureSingletons(configuration, env);
        return services;
    }

    // -------------------- Swagger / 文档 --------------------

    private static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddControllers();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "SakuraFilter API",
                Version = "1.0.0",
                Description = "工业/汽车滤清器产品管理平台 API\r\n\r\n" +
                              "## 模块说明\r\n" +
                              "- **认证**: JWT 登录/刷新/登出/用户管理\r\n" +
                              "- **公开搜索**: 前台产品搜索/详情 (无需认证)\r\n" +
                              "- **后台产品管理**: CRUD (需 admin 角色)\r\n" +
                              "- **字典管理**: 8 类字典 CRUD (需认证)\r\n" +
                              "- **ETL**: 数据导入/状态/进度 (X-Admin-Token)\r\n" +
                              "- **运维**: 健康检查/指标/性能告警\r\n\r\n" +
                              "## 认证方式\r\n" +
                              "1. **JWT Bearer** (推荐): 登录 /api/auth/login 获取 token, 放入 Authorization: Bearer {token}\r\n" +
                              "2. **X-Admin-Token** (ETL/CI 备用): 放入 X-Admin-Token: {token}",
                Contact = new Microsoft.OpenApi.Models.OpenApiContact
                {
                    Name = "SakuraFilter Team",
                    Email = "admin@sakurafilter.dev"
                },
                License = new Microsoft.OpenApi.Models.OpenApiLicense
                {
                    Name = "Proprietary"
                }
            });

            c.TagActionsBy(api =>
            {
                if (!string.IsNullOrEmpty(api.GroupName)) return new[] { api.GroupName };
                api.ActionDescriptor.RouteValues.TryGetValue("controller", out var c);
                return new[] { c ?? "Default" };
            });
            c.DocInclusionPredicate((_, _) => true);

            c.AddSecurityDefinition("X-Admin-Token", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Name = "X-Admin-Token",
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = "Day 8.4: dev 静态 token, 从 appsettings.json 的 Auth:DevStaticToken 读"
            });
            c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "X-Admin-Token"
                        }
                    },
                    Array.Empty<string>()
                }
            });
            c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = "JWT Bearer token. 格式: Bearer {token} (登录 /api/auth/login 获取)"
            });
            c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
            else
            {
                Console.WriteLine($"[Swagger] XML doc not found: {xmlPath} (确保 csproj 启用 GenerateDocumentationFile)");
            }
        });
        return services;
    }

    // -------------------- 认证 / 授权 --------------------

    private static IServiceCollection AddAuthServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<UserService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                        configuration["Jwt:SigningKey"]
                            ?? throw new InvalidOperationException("Jwt:SigningKey not configured"))),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization(options =>
        {
            options.AddPolicy("Admin", p => p.RequireRole("admin"));
            options.AddPolicy("Operator", p => p.RequireRole("admin", "operator"));
        });
        return services;
    }

    // -------------------- 验证 / XSS --------------------

    private static IServiceCollection AddValidationServices(this IServiceCollection services)
    {
        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<Program>();
        services.AddSingleton<XssSanitizer>();
        return services;
    }

    // -------------------- 数据库 --------------------

    private static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Npgsql 6+: 默认只接受 DateTime Kind=Utc, 反序列化 "2007-01-01" 这类无时区字符串会抛异常
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var pgConn = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未配置 (检查 appsettings.json 或环境变量 ConnectionStrings__Postgres)");
        services.AddDbContext<ProductDbContext>(opt => opt.UseNpgsql(pgConn));
        return services;
    }

    // -------------------- 搜索 --------------------

    private static IServiceCollection AddSearchServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MeiliSearchOptions>(configuration.GetSection("MeiliSearch"));
        services.AddScoped<PostgresSearchProvider>();
        services.AddScoped<MeiliSearchProvider>();
        services.AddScoped<ISearchProvider, ResilientSearchProvider>();
        return services;
    }

    // -------------------- ETL --------------------

    private static IServiceCollection AddEtlServices(this IServiceCollection services, IConfiguration configuration)
    {
        var pgConn = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres 未配置");

        services.AddOptions<EtlOptions>()
            .Bind(configuration.GetSection("Etl"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EtlOptions>, EtlOptionsValidator>();

        services.AddSingleton(sp => new EtlImportService(
            pgConn,
            sp.GetRequiredService<ILogger<EtlImportService>>(),
            sp,
            sp.GetRequiredService<IOptions<EtlOptions>>(),
            sp.GetRequiredService<IEtlProgressBroadcaster>()));
        services.AddSingleton<IEtlProgressBroadcaster, EtlProgressBroadcaster>();
        return services;
    }

    // -------------------- 对象存储 (MinIO / Aliyun OSS) --------------------

    private static IServiceCollection AddStorageServices(this IServiceCollection services, IConfiguration configuration)
    {
        var storageProvider = configuration["Storage:Provider"]?.ToLowerInvariant() ?? "minio";
        services.AddSingleton<IObjectStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<StorageProviderMarker>>();
            if (storageProvider == "aliyun-oss")
            {
                var config = configuration.GetSection("Aliyun");
                var endpoint = config["Endpoint"] ?? "oss-cn-hangzhou.aliyuncs.com";
                var accessKeyId = config["AccessKeyId"] ?? "";
                var accessKeySecret = config["AccessKeySecret"] ?? "";
                if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(accessKeySecret))
                {
                    throw new InvalidOperationException(
                        "Aliyun:AccessKeyId / AccessKeySecret 不能为空, 配置 appsettings.json 或环境变量 Aliyun__AccessKeyId / Aliyun__AccessKeySecret");
                }
                var ossClient = new OssClient(endpoint, accessKeyId, accessKeySecret);
                logger.LogInformation("[Storage] Provider=aliyun-oss, Endpoint={Endpoint}, Bucket={Bucket}, Cdn={Cdn}",
                    endpoint, config["BucketName"], config["CdnEndpoint"]);
                return new AliyunOssStorage(
                    ossClient,
                    config["BucketName"] ?? "sakurafilter-prod",
                    config["PublicEndpoint"] ?? $"https://{config["BucketName"]}.{endpoint}",
                    config["CdnEndpoint"]);
            }

            var minioConfig = configuration.GetSection("Minio");
            var minioEndpoint = minioConfig["Endpoint"] ?? "localhost:9000";
            var useSSL = bool.TryParse(minioConfig["UseSSL"], out var ssl) && ssl;
            var minioAccessKey = minioConfig["AccessKey"]
                ?? throw new InvalidOperationException("Minio:AccessKey 未配置 (检查 appsettings.json 或环境变量 Minio__AccessKey)");
            var minioSecretKey = minioConfig["SecretKey"]
                ?? throw new InvalidOperationException("Minio:SecretKey 未配置 (检查 appsettings.json 或环境变量 Minio__SecretKey)");
            var minioClient = new MinioClient()
                .WithEndpoint(minioEndpoint)
                .WithCredentials(minioAccessKey, minioSecretKey)
                .WithSSL(useSSL)
                .Build();
            logger.LogInformation("[Storage] Provider=minio, Endpoint={Endpoint}, Bucket={Bucket}", minioEndpoint, minioConfig["BucketName"]);
            return new MinioStorage(
                minioClient,
                minioConfig["BucketName"] ?? "sakurafilter",
                minioConfig["PublicEndpoint"] ?? $"http://{minioEndpoint}"
            );
        });
        return services;
    }

    // -------------------- CORS --------------------

    private static IServiceCollection AddCorsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173", "http://localhost:5174", "http://localhost:3000" };
        services.AddCors(o => o.AddPolicy("SakuraFilterCors", p =>
            p.WithOrigins(allowedOrigins)
             .AllowAnyMethod()
             .AllowAnyHeader()
             .AllowCredentials()));
        return services;
    }

    // -------------------- 限流 --------------------

    private static IServiceCollection AddRateLimitServices(this IServiceCollection services, IConfiguration configuration)
    {
        var rateLimitConfig = configuration.GetSection("RateLimit").Get<RateLimitOptions>()
            ?? new RateLimitOptions();
        if (!rateLimitConfig.Enabled)
        {
            return services;
        }
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                }
                await context.HttpContext.Response.WriteAsync(
                    "{\"type\":\"https://tools.ietf.org/html/rfc6585#section-4\"," +
                    "\"title\":\"Too Many Requests\"," +
                    "\"status\":429," +
                    "\"detail\":\"请求频率超限, 请稍后重试\"}", ct);
            };
            options.AddPolicy("global", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "global",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitConfig.GlobalPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy("search", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "search",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitConfig.SearchPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy("etl", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "etl",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitConfig.EtlPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy("auth", ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitConfig.AuthPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });
        return services;
    }

    // -------------------- 业务服务 (Scoped) --------------------

    private static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        services.AddScoped<AdminProductService>();
        services.AddScoped<AdminProductImageService>();
        services.AddScoped<PublicTypeaheadService>();
        services.AddScoped<OemBrandDictService>();
        services.AddScoped<ProductName1DictService>();
        services.AddScoped<ProductName2DictService>();
        services.AddScoped<TypeDictService>();
        services.AddScoped<OemNo3DictService>();
        services.AddScoped<MediaDictService>();
        services.AddScoped<MachineDictService>();
        services.AddScoped<EngineDictService>();
        return services;
    }

    // -------------------- 后台服务 (HostedService) --------------------

    private static IServiceCollection AddHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<HistoryCleanupService>();
        services.AddHostedService<EtlLogCleanupService>();
        services.AddHostedService<DeadLetterCleanupService>();
        services.AddHostedService<DeadLetterRecoveryService>();
        services.AddHostedService<IndexReplayWorker>();
        services.AddHostedService<EtlAlertService>();
        services.AddSingleton<PerfAlertService>();
        services.AddHostedService(sp => sp.GetRequiredService<PerfAlertService>());
        services.AddHttpClient("EtlAlert", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHostedService<BusinessMetricsRefreshWorker>();
        services.AddSingleton<AuthTokenBroadcaster>();
        services.AddHostedService(sp => sp.GetRequiredService<AuthTokenBroadcaster>());
        return services;
    }

    // -------------------- 基础设施单例 (跨模块共享) --------------------

    private static IServiceCollection AddInfrastructureSingletons(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
    {
        services.AddSingleton<CursorHmac>();
        services.AddSingleton<PerfMetrics>();
        services.AddSingleton<IHostedServiceStatus, HostedServiceStatus>();
        services.AddSingleton<BusinessMetrics>();
        services.AddSingleton<IAuthTokenStore, AuthTokenStore>();
        return services;
    }
}

/// <summary>
/// 类型 marker: 用于 ILogger&lt;T&gt; 泛型实参（避免与同名静态类冲突）。
/// </summary>
internal sealed class StorageProviderMarker
{
}
