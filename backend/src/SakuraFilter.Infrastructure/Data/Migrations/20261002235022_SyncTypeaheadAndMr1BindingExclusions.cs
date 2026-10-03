using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <summary>
    /// 快照同步迁移 (2026-10-03, P2 技术债): 空 DDL, 仅推进 EF 模型快照。
    ///
    /// WHY: 本仓库采用 EF Core migrations + 手写 SQL migrations 双轨制。
    ///   以下模型差异均已由 SQL 迁移在生产库落地, 但 EF 快照 (ProductDbContextModelSnapshot) 未同步,
    ///   导致 `dotnet ef migrations has-pending-model-changes` 恒为 true, 该门禁失去意义:
    ///     - product_images.show_dimension          → 026_product_images_show_dimension.sql
    ///     - machine_applications.product_id 改可空 → 020_allow_null_product_id_in_apps.sql
    ///     - etl_progress_log.auto_generated_mr1    → 041_etl_progress_log_add_auto_generated_mr1.sql (原 026, 2026-10-03 改名消除编号冲突)
    ///     - cross_references.is_whitelisted        → 027_cross_references_is_whitelisted.sql
    ///     - typeahead_dict / machine_mr1_bindings  → 023 / 039 建表, 已标记 ExcludeFromMigrations
    ///
    ///   这些列/表在生产库中已存在, 若按 EF 生成的 DDL 执行会因 42701 column already exists 失败。
    ///   故 Up()/Down() 置空: 本迁移只负责把快照推进到当前模型, 实际 DDL 仍由 SQL 迁移负责。
    ///   全新库: EF 迁移建表后由 SQL 迁移补齐上述列 (023/026/027/039 均为幂等脚本), 语义一致。
    /// </summary>
    public partial class SyncTypeaheadAndMr1BindingExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 有意为空 —— 见类注释。DDL 由 SQL 迁移 020/023/026/027/039/040/041 负责。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 有意为空 —— 见类注释。
        }
    }
}
