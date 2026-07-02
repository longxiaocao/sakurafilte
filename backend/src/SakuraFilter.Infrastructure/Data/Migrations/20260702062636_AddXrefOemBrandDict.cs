using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddXrefOemBrandDict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "xref_oem_brand",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xref_oem_brand", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_xref_oem_brand_active",
                table: "xref_oem_brand",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_xref_oem_brand_brand",
                table: "xref_oem_brand",
                column: "brand",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xref_oem_brand");
        }
    }
}
