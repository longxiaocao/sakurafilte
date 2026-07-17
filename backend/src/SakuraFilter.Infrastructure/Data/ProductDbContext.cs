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

    // V2: 分区 6 占位空表
    public DbSet<Partition6Placeholder> Partition6Placeholders => Set<Partition6Placeholder>();

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
            // V2: oem_no_normalized 降级为普通索引(允许 NULL/重复),MR.1 为内部主键
            e.Property(p => p.OemNoNormalized).HasMaxLength(50);
            e.Property(p => p.OemNoDisplay).HasMaxLength(50).IsRequired();
            e.Property(p => p.Type).HasMaxLength(50).IsRequired();
            e.Property(p => p.Remark).HasColumnType("text");
            e.Property(p => p.ImageKey).HasMaxLength(500);
            e.Property(p => p.ImageStatus).HasMaxLength(20).HasDefaultValue("pending");
            e.Property(p => p.IsPublished).HasDefaultValue(true);
            e.Property(p => p.IsDiscontinued).HasDefaultValue(false);
            e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
            // 乐观锁 xmin (uint, PG 系统列 xid)
            e.Property(p => p.RowVersion).IsRowVersion().IsConcurrencyToken();
            // V2: oem_no_normalized 改普通部分索引(降级,允许 NULL)
            e.HasIndex(p => p.OemNoNormalized)
                .HasDatabaseName("idx_products_oem_no_normalized")
                .HasFilter("oem_no_normalized IS NOT NULL");
            // V2: MR.1 UNIQUE 部分索引(内部主键)
            e.HasIndex(p => p.Mr1)
                .IsUnique()
                .HasDatabaseName("idx_products_mr_1_unique")
                .HasFilter("mr_1 IS NOT NULL");
            // V2: MR.1 格式 CHECK 约束(1-10 位字母数字)
            e.HasCheckConstraint("chk_mr_1_format", "mr_1 IS NULL OR mr_1 ~ '^[A-Za-z0-9]{1,10}$'");
            e.HasIndex(p => p.OemNoDisplay);
            e.HasIndex(p => p.Type);
            e.HasIndex(p => new { p.Type, p.D1Mm }).HasDatabaseName("idx_products_type_d1");
            e.HasIndex(p => new { p.Type, p.D2Mm }).HasDatabaseName("idx_products_type_d2");
            e.HasIndex(p => new { p.Type, p.H1Mm }).HasDatabaseName("idx_products_type_h1");
            e.HasIndex(p => p.D1Mm);
            e.HasIndex(p => p.D2Mm);
            e.HasIndex(p => p.H1Mm);
            e.HasIndex(p => p.Oem2);
        });

        // CrossReference
        mb.Entity<CrossReference>(e =>
        {
            e.ToTable("cross_references");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductId).IsRequired();
            e.Property(x => x.ProductName1).HasMaxLength(100);
            // V2: oem_brand / oem_no_3 升级为 NOT NULL(对外展示主键)
            e.Property(x => x.OemBrand).HasMaxLength(100).IsRequired();
            e.Property(x => x.OemNo3).HasMaxLength(200).IsRequired();
            // V2: 新增字段
            e.Property(x => x.Oem2).HasMaxLength(100);
            e.Property(x => x.SortOrder).HasDefaultValue(0);
            e.Property(x => x.MachineType).HasMaxLength(50).HasDefaultValue("others");
            e.Property(x => x.IsPublished).HasDefaultValue(true);
            // V2: xmin 乐观锁令牌(OEM 3 排序并发控制)
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            // V2: machine_type CHECK 约束
            e.HasCheckConstraint("chk_xref_machine_type",
                "machine_type IS NULL OR machine_type IN ('agriculture', 'commercial', 'construction', 'industrial', 'others')");
            e.HasIndex(x => x.ProductId);
            // V2: OEM 3 优先排序查询索引(仅未下架 + 已发布)
            e.HasIndex(x => new { x.OemBrand, x.SortOrder, x.OemNo3 })
                .HasDatabaseName("idx_xrefs_brand_oem3_sort")
                .HasFilter("is_discontinued = false AND is_published = true");
            // V2: (oem_brand, oem_no_3) 唯一约束(仅未下架,允许下架后重新上架)
            e.HasIndex(x => new { x.OemBrand, x.OemNo3 })
                .IsUnique()
                .HasDatabaseName("uq_xrefs_brand_oem3")
                .HasFilter("is_discontinued = false");
        });

        // MachineApplication
        mb.Entity<MachineApplication>(e =>
        {
            e.ToTable("machine_applications");
            e.HasKey(m => m.Id);
            e.Property(m => m.MachineBrand).HasMaxLength(200);
            e.Property(m => m.MachineModel).HasMaxLength(200);
            e.Property(m => m.ModelName).HasMaxLength(100);
            // V2: machine_category(与 cross_references.machine_type 枚举一致)
            e.Property(m => m.MachineCategory).HasMaxLength(50).HasDefaultValue("others");
            e.HasCheckConstraint("chk_machine_apps_category",
                "machine_category IS NULL OR machine_category IN ('agriculture', 'commercial', 'construction', 'industrial', 'others')");
            e.HasIndex(m => m.ProductId);
            e.HasIndex(m => new { m.MachineBrand, m.MachineModel });
            // V2: 机型分类树级联索引
            e.HasIndex(m => new { m.MachineCategory, m.MachineBrand, m.MachineModel })
                .HasDatabaseName("idx_machine_apps_category");
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

        // ProductImage (V2: 主图/详情图分层,DROP 旧 product_id+slot UNIQUE)
        mb.Entity<ProductImage>(e =>
        {
            e.ToTable("product_images");
            e.HasKey(i => i.Id);
            e.Property(i => i.ImageKey).HasMaxLength(500).IsRequired();
            e.Property(i => i.ContentType).HasMaxLength(50);
            e.Property(i => i.UploadedBy).HasMaxLength(100);
            e.Property(i => i.Slot).IsRequired();
            e.Property(i => i.DisplayOrder).HasDefaultValue(0);
            // V2: 新增字段
            e.Property(i => i.OemNo3).HasMaxLength(200);
            e.Property(i => i.ImageRole).HasMaxLength(20).IsRequired().HasDefaultValue("detail");
            // V2: image_role CHECK 约束
            e.HasCheckConstraint("chk_image_role", "image_role IN ('primary', 'detail')");
            // V2: slot 与 image_role 一致性 CHECK
            e.HasCheckConstraint("chk_image_role_slot",
                "(image_role = 'primary' AND slot = 1) OR (image_role = 'detail' AND slot BETWEEN 2 AND 6)");
            // V2: 主图唯一索引(按 oem_no_3)
            e.HasIndex(i => i.OemNo3)
                .IsUnique()
                .HasDatabaseName("uq_product_images_primary")
                .HasFilter("image_role = 'primary' AND oem_no_3 IS NOT NULL");
            // V2: 详情图唯一索引(按 product_id + slot)
            e.HasIndex(i => new { i.ProductId, i.Slot })
                .IsUnique()
                .HasDatabaseName("uq_product_images_detail_slot")
                .HasFilter("image_role = 'detail'");
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

        // V2: Partition6Placeholder 占位空表(不参与任何业务查询)
        mb.Entity<Partition6Placeholder>(e =>
        {
            e.ToTable("partition6_placeholder");
            e.HasKey(p => p.Id);
            e.Property(p => p.CreatedAt).HasDefaultValueSql("now()");
        });
    }
}
