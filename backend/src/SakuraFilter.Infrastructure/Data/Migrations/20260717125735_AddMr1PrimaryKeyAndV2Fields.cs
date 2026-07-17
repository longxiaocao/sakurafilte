using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMr1PrimaryKeyAndV2Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ===== Task 0.1.1 补充: spec 要求但 EF Core 无法自动检测的 schema 变更 =====

            // 1. products.mr_1 TYPE varchar(10) (spec Task 0.1.1)
            migrationBuilder.AlterColumn<string>(
                name: "mr_1",
                table: "products",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            // 2. products.oem_no_normalized DROP NOT NULL (spec D3-1: oem_no_normalized 改为可 NULL,不再作主键)
            //    WHY: V2 主键改为 mr_1, oem_no_normalized 降级为普通索引,允许 NULL
            migrationBuilder.AlterColumn<string>(
                name: "oem_no_normalized",
                table: "products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            // 3. products 8 个 numeric 字段精度统一 numeric(10,2) (spec D3-19)
            //    WHY: 原 numeric(8,2) 不够存储大尺寸数据
            migrationBuilder.AlterColumn<decimal>(
                name: "d1_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
            migrationBuilder.AlterColumn<decimal>(
                name: "d2_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
            migrationBuilder.AlterColumn<decimal>(
                name: "d3_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
            migrationBuilder.AlterColumn<decimal>(
                name: "d4_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
            migrationBuilder.AlterColumn<decimal>(
                name: "h1_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
            migrationBuilder.AlterColumn<decimal>(
                name: "h2_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
            migrationBuilder.AlterColumn<decimal>(
                name: "h3_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);
            migrationBuilder.AlterColumn<decimal>(
                name: "h4_mm",
                table: "products",
                type: "numeric(10,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(8,2)",
                oldNullable: true);

            // 4. cross_references.product_id SET NOT NULL (spec Task 0.1.1)
            migrationBuilder.AlterColumn<long>(
                name: "product_id",
                table: "cross_references",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            // 5. cross_references.is_discontinued SET NOT NULL DEFAULT false (spec D3-22)
            //    WHY: 部分唯一索引 WHERE is_discontinued = false 依赖 NOT NULL
            migrationBuilder.AlterColumn<bool>(
                name: "is_discontinued",
                table: "cross_references",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: false);

            // ===== 以下为 EF Core 自动生成的迁移内容 =====

            migrationBuilder.DropIndex(
                name: "ix_products_mr_1",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_oem_no_normalized",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_product_images_product_id_slot",
                table: "product_images");

            migrationBuilder.DropIndex(
                name: "ix_cross_references_oem_brand_oem_no_3",
                table: "cross_references");

            migrationBuilder.AddColumn<string>(
                name: "d1_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "d2_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "d3_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "d4_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "h1_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "h2_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "h3_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "h4_mm_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_role",
                table: "product_images",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "detail");

            migrationBuilder.AddColumn<string>(
                name: "oem_no_3",
                table: "product_images",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "machine_category",
                table: "machine_applications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "others");

            migrationBuilder.AlterColumn<string>(
                name: "oem_no_3",
                table: "cross_references",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "oem_brand",
                table: "cross_references",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_published",
                table: "cross_references",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "machine_type",
                table: "cross_references",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                defaultValue: "others");

            migrationBuilder.AddColumn<string>(
                name: "oem_2",
                table: "cross_references",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "cross_references",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "cross_references",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "partition6_placeholder",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partition6_placeholder", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_products_mr_1_unique",
                table: "products",
                column: "mr_1",
                unique: true,
                filter: "mr_1 IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_products_oem_no_normalized",
                table: "products",
                column: "oem_no_normalized",
                filter: "oem_no_normalized IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_mr_1_format",
                table: "products",
                sql: "mr_1 IS NULL OR mr_1 ~ '^[A-Za-z0-9]{1,10}$'");

            migrationBuilder.CreateIndex(
                name: "uq_product_images_detail_slot",
                table: "product_images",
                columns: new[] { "product_id", "slot" },
                unique: true,
                filter: "image_role = 'detail'");

            migrationBuilder.CreateIndex(
                name: "uq_product_images_primary",
                table: "product_images",
                column: "oem_no_3",
                unique: true,
                filter: "image_role = 'primary' AND oem_no_3 IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "chk_image_role",
                table: "product_images",
                sql: "image_role IN ('primary', 'detail')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_image_role_slot",
                table: "product_images",
                sql: "(image_role = 'primary' AND slot = 1) OR (image_role = 'detail' AND slot BETWEEN 2 AND 6)");

            migrationBuilder.CreateIndex(
                name: "idx_machine_apps_category",
                table: "machine_applications",
                columns: new[] { "machine_category", "machine_brand", "machine_model" });

            migrationBuilder.AddCheckConstraint(
                name: "chk_machine_apps_category",
                table: "machine_applications",
                sql: "machine_category IS NULL OR machine_category IN ('agriculture', 'commercial', 'construction', 'industrial', 'others')");

            migrationBuilder.CreateIndex(
                name: "idx_xrefs_brand_oem3_sort",
                table: "cross_references",
                columns: new[] { "oem_brand", "sort_order", "oem_no_3" },
                filter: "is_discontinued = false AND is_published = true");

            migrationBuilder.CreateIndex(
                name: "uq_xrefs_brand_oem3",
                table: "cross_references",
                columns: new[] { "oem_brand", "oem_no_3" },
                unique: true,
                filter: "is_discontinued = false");

            migrationBuilder.AddCheckConstraint(
                name: "chk_xref_machine_type",
                table: "cross_references",
                sql: "machine_type IS NULL OR machine_type IN ('agriculture', 'commercial', 'construction', 'industrial', 'others')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partition6_placeholder");

            migrationBuilder.DropIndex(
                name: "idx_products_mr_1_unique",
                table: "products");

            migrationBuilder.DropIndex(
                name: "idx_products_oem_no_normalized",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "chk_mr_1_format",
                table: "products");

            migrationBuilder.DropIndex(
                name: "uq_product_images_detail_slot",
                table: "product_images");

            migrationBuilder.DropIndex(
                name: "uq_product_images_primary",
                table: "product_images");

            migrationBuilder.DropCheckConstraint(
                name: "chk_image_role",
                table: "product_images");

            migrationBuilder.DropCheckConstraint(
                name: "chk_image_role_slot",
                table: "product_images");

            migrationBuilder.DropIndex(
                name: "idx_machine_apps_category",
                table: "machine_applications");

            migrationBuilder.DropCheckConstraint(
                name: "chk_machine_apps_category",
                table: "machine_applications");

            migrationBuilder.DropIndex(
                name: "idx_xrefs_brand_oem3_sort",
                table: "cross_references");

            migrationBuilder.DropIndex(
                name: "uq_xrefs_brand_oem3",
                table: "cross_references");

            migrationBuilder.DropCheckConstraint(
                name: "chk_xref_machine_type",
                table: "cross_references");

            migrationBuilder.DropColumn(
                name: "d1_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "d2_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "d3_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "d4_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "h1_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "h2_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "h3_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "h4_mm_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "image_role",
                table: "product_images");

            migrationBuilder.DropColumn(
                name: "oem_no_3",
                table: "product_images");

            migrationBuilder.DropColumn(
                name: "machine_category",
                table: "machine_applications");

            migrationBuilder.DropColumn(
                name: "is_published",
                table: "cross_references");

            migrationBuilder.DropColumn(
                name: "machine_type",
                table: "cross_references");

            migrationBuilder.DropColumn(
                name: "oem_2",
                table: "cross_references");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "cross_references");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "cross_references");

            migrationBuilder.AlterColumn<string>(
                name: "oem_no_3",
                table: "cross_references",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "oem_brand",
                table: "cross_references",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "ix_products_mr_1",
                table: "products",
                column: "mr_1");

            migrationBuilder.CreateIndex(
                name: "ix_products_oem_no_normalized",
                table: "products",
                column: "oem_no_normalized",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_images_product_id_slot",
                table: "product_images",
                columns: new[] { "product_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cross_references_oem_brand_oem_no_3",
                table: "cross_references",
                columns: new[] { "oem_brand", "oem_no_3" });
        }
    }
}
