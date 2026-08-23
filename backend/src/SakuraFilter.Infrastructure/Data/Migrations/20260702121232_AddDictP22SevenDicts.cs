using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDictP22SevenDicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dict_engine",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    engine_brand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    engine_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dict_engine", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dict_machine",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    machine_brand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    machine_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    machine_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dict_machine", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dict_media",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    media_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    media_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dict_media", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dict_oem_no3",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    oem_no_3 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dict_oem_no3", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dict_product_name1",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_name_1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dict_product_name1", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dict_product_name2",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_name_2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dict_product_name2", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dict_type",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dict_type", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_dict_engine_active",
                table: "dict_engine",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dict_engine_engine_brand_engine_type",
                table: "dict_engine",
                columns: new[] { "engine_brand", "engine_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dict_machine_active",
                table: "dict_machine",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dict_machine_machine_brand_machine_model_machine_name",
                table: "dict_machine",
                columns: new[] { "machine_brand", "machine_model", "machine_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dict_media_active",
                table: "dict_media",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dict_media_media_name_media_model",
                table: "dict_media",
                columns: new[] { "media_name", "media_model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dict_oem_no3_active",
                table: "dict_oem_no3",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dict_oem_no3_oem_no_3",
                table: "dict_oem_no3",
                column: "oem_no_3",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dict_product_name1_active",
                table: "dict_product_name1",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dict_product_name1_product_name_1",
                table: "dict_product_name1",
                column: "product_name_1",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dict_product_name2_active",
                table: "dict_product_name2",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dict_product_name2_product_name_2",
                table: "dict_product_name2",
                column: "product_name_2",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_dict_type_active",
                table: "dict_type",
                columns: new[] { "deleted_at", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dict_type_type",
                table: "dict_type",
                column: "type",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dict_engine");

            migrationBuilder.DropTable(
                name: "dict_machine");

            migrationBuilder.DropTable(
                name: "dict_media");

            migrationBuilder.DropTable(
                name: "dict_oem_no3");

            migrationBuilder.DropTable(
                name: "dict_product_name1");

            migrationBuilder.DropTable(
                name: "dict_product_name2");

            migrationBuilder.DropTable(
                name: "dict_type");
        }
    }
}
