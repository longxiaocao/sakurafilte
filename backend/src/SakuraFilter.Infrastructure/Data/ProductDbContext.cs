using Microsoft.EntityFrameworkCore;
using SakuraFilter.Core.Entities;

namespace SakuraFilter.Infrastructure.Data;

/// <summary>
/// 产品数据库上下文
/// Day 4 阶段:简化为单表(不分区),Day 5 再加分区
/// </summary>
public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<CrossReference> CrossReferences => Set<CrossReference>();
    public DbSet<MachineApplication> MachineApplications => Set<MachineApplication>();
    public DbSet<ProductHistory> ProductHistory => Set<ProductHistory>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();  // Day 8.1
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<SearchIndexPending> SearchIndexPending => Set<SearchIndexPending>();
    public DbSet<SearchIndexDeadLetter> SearchIndexDeadLetters => Set<SearchIndexDeadLetter>();
    public DbSet<EtlProgressLog> EtlProgressLogs => Set<EtlProgressLog>();  // Day 7.7
    public DbSet<XrefOemBrand> XrefOemBrands => Set<XrefOemBrand>();  // Day 10: P1.3 OEM 品牌字典
    // Day 10+ P2.2: 6 个新字典 (复用 P2.1 IDictService + BaseDictService 抽象)
    public DbSet<DictProductName1> DictProductName1s => Set<DictProductName1>();
    public DbSet<DictProductName2> DictProductName2s => Set<DictProductName2>();
    public DbSet<DictType> DictTypes => Set<DictType>();
    public DbSet<DictOemNo3> DictOemNo3s => Set<DictOemNo3>();
    public DbSet<DictMedia> DictMedias => Set<DictMedia>();           // 多字段 2
    public DbSet<DictMachine> DictMachines => Set<DictMachine>();     // 多字段 3
    public DbSet<DictEngine> DictEngines => Set<DictEngine>();         // 多字段 2

    // JWT 认证体系 (3 张表: users / refresh_tokens / login_audit_logs)
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAuditLog> LoginAuditLogs => Set<LoginAuditLog>();

    // P2-1 告警系统: alert_history / alert_rules / security_events
    public DbSet<AlertHistory> AlertHistories => Set<AlertHistory>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // 启用 snake_case 命名,让 PascalCase 实体自动转 snake_case 列
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Day 10+ P2.1: 集中注册所有 IEntityTypeConfiguration<T> 分文件配置
        //   这样后续 P2.2 新增字典 (Product Name 1/2, Type, OEM 3, Media, Machine) 时
        //   只加 Entity + Configuration 文件, DbContext 不动 (零侵入)
        mb.ApplyConfigurationsFromAssembly(typeof(ProductDbContext).Assembly);

        // Product
        mb.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(p => p.Id);
            e.Property(p => p.OemNoNormalized).HasMaxLength(50).IsRequired();
            e.Property(p => p.OemNoDisplay).HasMaxLength(50).IsRequired();
            e.Property(p => p.Type).HasMaxLength(50).IsRequired();
            e.Property(p => p.Remark).HasColumnType("text");
            e.Property(p => p.ImageKey).HasMaxLength(500);
            e.Property(p => p.ImageStatus).HasMaxLength(20).HasDefaultValue("pending");
            // Day 9.12 v8: is_published 默认 true (Product.cs C# 默认值)
            //   WHY: ETL INSERT 不显式插入 is_published 列, PG 列无默认值时 23502 违反 NOT NULL
            //        之前本地数据库有旧 SQL migration 建过 DEFAULT true,CI 上 Migrate 无默认值 → ETL failed
            e.Property(p => p.IsPublished).HasDefaultValue(true);
            // Day 9.12 v9: is_discontinued 默认 false, created_at 默认 now()
            //   WHY: ETL INSERT 不插入这两列, PG 列 NOT NULL 无默认值 → 23502
            //        CI 上逐个报错 (is_published → is_discontinued → created_at), 一次性全修复避免迭代
            e.Property(p => p.IsDiscontinued).HasDefaultValue(false);
            e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            // E2E BD.3 修复 v2: 乐观锁 xmin (uint, PG 系统列 xid)
            //   WHY: 不能用 byte[] + [Timestamp], 因为 PG xmin 类型是 xid (uint32), Npgsql 抛 InvalidCastException
            //   方案: uint 类型 + Fluent API IsRowVersion(), EF Core 在 UPDATE 时 SET WHERE xmin = @orig
            //   防止 ApplyConfigurationsFromAssembly 加载的 IEntityTypeConfiguration<Product> 覆盖
            e.Property(p => p.RowVersion).IsRowVersion().IsConcurrencyToken();
            // Day 9.12 v7: OemNoNormalized 必须为 UNIQUE 索引
            //   WHY: EtlImportService.ImportProductsAsync INSERT 用 ON CONFLICT (oem_no_normalized) DO NOTHING/UPDATE
            //        无 UNIQUE 约束时 PG 报 42P10: there is no unique or exclusion constraint matching the ON CONFLICT specification
            //        之前本地数据库有旧 SQL migration 建的 uq_products_oem_normalized,CI 上 Migrate 只建普通索引 → ETL failed
            e.HasIndex(p => p.OemNoNormalized).IsUnique();
            e.HasIndex(p => p.OemNoDisplay);
            e.HasIndex(p => p.Type);
            // WHY: 复合索引 (type, dX_mm) 让"按 Type 过滤 + ±5mm 范围"走 Index Scan,实测 0.2ms
            //      1M 数据实测比物理 LIST 分区性能差 < 5%,但省去 EF Core 复合 PK 改造
            e.HasIndex(p => new { p.Type, p.D1Mm }).HasDatabaseName("idx_products_type_d1");
            e.HasIndex(p => new { p.Type, p.D2Mm }).HasDatabaseName("idx_products_type_d2");
            e.HasIndex(p => new { p.Type, p.H1Mm }).HasDatabaseName("idx_products_type_h1");
            // 单字段索引保留(无 Type 过滤的纯范围查询)
            e.HasIndex(p => p.D1Mm);
            e.HasIndex(p => p.D2Mm);
            e.HasIndex(p => p.H1Mm);
            // P3-2 改进: 为 Oem2 / Mr1 加索引, 优化 PublicProductController.GetBySlug 的 OR 查询
            //   WHY: GetBySlug 用 (OemNoDisplay == x || Oem2 == x || Mr1 == x) OR 查询,
            //        OemNoDisplay 已有索引, Oem2/Mr1 无索引时 PG 走 BitmapOr + 顺序扫描,
            //        百万级数据下最坏 ~50ms, 加索引后全部走 Index Scan, ~2ms
            //        (按需索引: 仅 GetBySlug 等公开端点使用, 写入开销可接受)
            e.HasIndex(p => p.Oem2);
            e.HasIndex(p => p.Mr1);
        });

        // CrossReference
        mb.Entity<CrossReference>(e =>
        {
            e.ToTable("cross_references");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName1).HasMaxLength(100);
            e.Property(x => x.OemBrand).HasMaxLength(100);
            e.Property(x => x.OemNo3).HasMaxLength(100);
            e.HasIndex(x => x.ProductId);
            e.HasIndex(x => new { x.OemBrand, x.OemNo3 });
        });

        // MachineApplication
        mb.Entity<MachineApplication>(e =>
        {
            e.ToTable("machine_applications");
            e.HasKey(m => m.Id);
            e.Property(m => m.MachineBrand).HasMaxLength(200);
            e.Property(m => m.MachineModel).HasMaxLength(200);
            e.Property(m => m.ModelName).HasMaxLength(100);
            e.HasIndex(m => m.ProductId);
            e.HasIndex(m => new { m.MachineBrand, m.MachineModel });
        });

        // ProductHistory
        mb.Entity<ProductHistory>(e =>
        {
            e.ToTable("product_history");
            e.HasKey(h => h.Id);
            e.Property(h => h.ChangeType).HasMaxLength(20);
            e.Property(h => h.ChangedFields).HasColumnType("jsonb");
            e.Property(h => h.ChangedBy).HasMaxLength(100);
            e.HasIndex(h => new { h.ProductId, h.ChangedAt });
        });

        // ProductImage (Day 8.1: 规格分区 4, 1-6 张图)
        mb.Entity<ProductImage>(e =>
        {
            e.ToTable("product_images");
            e.HasKey(i => i.Id);
            e.Property(i => i.ImageKey).HasMaxLength(500).IsRequired();
            e.Property(i => i.ContentType).HasMaxLength(50);
            e.Property(i => i.UploadedBy).HasMaxLength(100);
            e.Property(i => i.Slot).IsRequired();
            e.Property(i => i.DisplayOrder).HasDefaultValue(0);
            // UNIQUE (product_id, slot) 由 SQL migration 保证 (slot=1-6 互斥)
            e.HasIndex(i => new { i.ProductId, i.Slot }).IsUnique();
            e.HasIndex(i => i.ProductId);
        });

        // SystemSetting
        mb.Entity<SystemSetting>(e =>
        {
            e.ToTable("system_settings");
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(100);
            // WHY: 改用 TEXT 而非 JSONB,允许存任意字符串 (cron 表达式、纯数字、布尔语义)
            e.Property(s => s.Value).HasColumnType("text");
        });

        // SearchIndexPending (Day 5: Meili 写入补偿队列)
        mb.Entity<SearchIndexPending>(e =>
        {
            e.ToTable("search_index_pending");
            e.HasKey(p => p.Id);
            e.Property(p => p.Operation).HasMaxLength(20).IsRequired();
            e.Property(p => p.Payload).HasColumnType("jsonb");
            e.HasIndex(p => new { p.NextRetryAt, p.RetryCount }).HasDatabaseName("idx_pending_retry");
        });

        // SearchIndexDeadLetter (Day 7: 重试超限转移目标)
        mb.Entity<SearchIndexDeadLetter>(e =>
        {
            e.ToTable("search_index_dead_letter");
            e.HasKey(p => p.Id);
            e.Property(p => p.Operation).HasMaxLength(20).IsRequired();
            e.Property(p => p.Payload).HasColumnType("jsonb");
            e.HasIndex(p => p.MovedAt);
            e.HasIndex(p => p.Operation);
            // Day 7.10.1: status 列过滤,active 是 worker 候选
            e.Property(p => p.Status).HasMaxLength(20).IsRequired();
            e.HasIndex(p => p.Status);
            // worker 扫描索引: (status=active, recovery_count < max)
            e.HasIndex(p => new { p.Status, p.RecoveryCount, p.LastRecoveryAt })
                .HasDatabaseName("idx_dead_letter_active_recovery")
                .HasFilter("status = 'active'");
        });

        // EtlProgressLog (Day 7.7: ETL 历史快照)
        mb.Entity<EtlProgressLog>(e =>
        {
            e.ToTable("etl_progress_log");
            e.HasKey(p => p.Id);
            e.Property(p => p.EntityType).HasMaxLength(20).IsRequired();
            e.Property(p => p.Mode).HasMaxLength(20).IsRequired();
            e.Property(p => p.FilePath).HasColumnType("text");
            e.Property(p => p.Status).HasMaxLength(20).IsRequired();
            e.HasIndex(p => new { p.EntityType, p.FinishedAt });
            e.HasIndex(p => p.Status);
        });

        // XrefOemBrand 配置已抽出到 Configurations/XrefOemBrandConfiguration.cs (Day 10+ P2.1)
        //   DbContext 顶部 ApplyConfigurationsFromAssembly 自动注册
    }
}
