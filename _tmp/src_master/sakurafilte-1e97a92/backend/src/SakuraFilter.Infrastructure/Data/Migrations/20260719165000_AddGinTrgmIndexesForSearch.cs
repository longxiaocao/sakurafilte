using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// V24-F94 (v28-2): 为 PostgresSearchProvider 搜索 SQL 添加 GIN trgm 索引
    /// WHY: v28-1 验证显示 baseline SQL (OR + EXISTS xref/machine) 让 PG 优化器不选 GIN trgm
    ///      v28-2 改用 CTE UNION 拆分 + 三表 GIN trgm, P95 从 1827ms → 305ms (6x)
    /// 索引清单 (5 个新增, 与 017_add_trgm_indexes.sql 互补):
    ///   products: product_name_1, product_name_2, mr_1, remark
    ///   cross_references: oem_2
    /// 5 个已存在索引 (017_add_trgm_indexes.sql):
    ///   products.oem_2, cross_references.{oem_brand, oem_no_3}, machine_applications.{machine_brand, machine_model}
    /// 注意: EF Core CreateIndex 不直接支持 GIN trgm_ops, 用 Sql() 原生 SQL
    /// </summary>
    public partial class AddGinTrgmIndexesForSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 启用 pg_trgm 扩展 (IF NOT EXISTS 兜底, 017 已启用则跳过)
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // products 4 个新 GIN trgm 索引 (oem_2 已由 017_add_trgm_indexes.sql 创建)
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_products_product_name_1_trgm
                    ON products USING gin (product_name_1 gin_trgm_ops);");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_products_product_name_2_trgm
                    ON products USING gin (product_name_2 gin_trgm_ops);");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_products_mr_1_trgm
                    ON products USING gin (mr_1 gin_trgm_ops);");
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_products_remark_trgm
                    ON products USING gin (remark gin_trgm_ops);");

            // cross_references 1 个新 GIN trgm 索引 (oem_brand, oem_no_3 已由 017 创建)
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_xrefs_oem_2_trgm
                    ON cross_references USING gin (oem_2 gin_trgm_ops);");

            // ANALYZE 更新统计信息, 让 PG 优化器能选 GIN trgm 索引
            migrationBuilder.Sql("ANALYZE products;");
            migrationBuilder.Sql("ANALYZE cross_references;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_product_name_1_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_product_name_2_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_mr_1_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_products_remark_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_xrefs_oem_2_trgm;");
        }
    }
}
