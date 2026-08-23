using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "etl_progress_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_path = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    read_count = table.Column<long>(type: "bigint", nullable: false),
                    inserted_count = table.Column<long>(type: "bigint", nullable: false),
                    updated_count = table.Column<long>(type: "bigint", nullable: false),
                    skipped_count = table.Column<long>(type: "bigint", nullable: false),
                    skipped_missing_oem = table.Column<long>(type: "bigint", nullable: false),
                    skipped_null_field = table.Column<long>(type: "bigint", nullable: false),
                    skipped_duplicate = table.Column<long>(type: "bigint", nullable: false),
                    error_count = table.Column<long>(type: "bigint", nullable: false),
                    indexed_count = table.Column<long>(type: "bigint", nullable: false),
                    index_pending_count = table.Column<long>(type: "bigint", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    duration_sec = table.Column<double>(type: "double precision", nullable: false),
                    alert_sent = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_reason = table.Column<string>(type: "text", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_etl_progress_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    change_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changed_fields = table.Column<string>(type: "jsonb", nullable: true),
                    changed_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    changed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    oem_no_normalized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    oem_no_display = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    remark = table.Column<string>(type: "text", nullable: true),
                    product_name_3 = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    product_name_1 = table.Column<string>(type: "text", nullable: true),
                    product_name_2 = table.Column<string>(type: "text", nullable: true),
                    mr_1 = table.Column<string>(type: "text", nullable: true),
                    oem_2 = table.Column<string>(type: "text", nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    d1_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    d2_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    d3_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    d4_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    h1_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    h2_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    h3_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    h4_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    d7_thread = table.Column<string>(type: "text", nullable: true),
                    d8_thread = table.Column<string>(type: "text", nullable: true),
                    media = table.Column<string>(type: "text", nullable: true),
                    no_check_valves = table.Column<int>(type: "integer", nullable: true),
                    no_bypass_valves = table.Column<int>(type: "integer", nullable: true),
                    media_model = table.Column<string>(type: "text", nullable: true),
                    sealing_material = table.Column<string>(type: "text", nullable: true),
                    efficiency_1 = table.Column<string>(type: "text", nullable: true),
                    efficiency_2 = table.Column<string>(type: "text", nullable: true),
                    bypass_valve_lr = table.Column<decimal>(type: "numeric", nullable: true),
                    bypass_valve_hr = table.Column<decimal>(type: "numeric", nullable: true),
                    bypass_pressure = table.Column<decimal>(type: "numeric", nullable: true),
                    collapse_pressure_bar = table.Column<decimal>(type: "numeric", nullable: true),
                    temp_range = table.Column<string>(type: "text", nullable: true),
                    qty_per_carton = table.Column<int>(type: "integer", nullable: true),
                    weight_kgs = table.Column<decimal>(type: "numeric", nullable: true),
                    carton_length_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    carton_width_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    carton_height_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    master_box_qty = table.Column<int>(type: "integer", nullable: true),
                    master_box_weight_kgs = table.Column<decimal>(type: "numeric", nullable: true),
                    master_box_length_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    master_box_width_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    master_box_height_mm = table.Column<decimal>(type: "numeric", nullable: true),
                    volume_per_carton_m3 = table.Column<decimal>(type: "numeric", nullable: true),
                    image_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    image_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    is_discontinued = table.Column<bool>(type: "boolean", nullable: false),
                    discontinued_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_index_dead_letter",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    original_id = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    moved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    recovery_count = table.Column<int>(type: "integer", nullable: false),
                    last_recovery_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_recovery_error = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recovered_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    recovered_to_pending_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_index_dead_letter", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "search_index_pending",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    next_retry_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_index_pending", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "cross_references",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    product_name_1 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    oem_brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    oem_no_3 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_discontinued = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_references", x => x.id);
                    table.ForeignKey(
                        name: "fk_cross_references_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_applications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    machine_brand = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    machine_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    engine_brand = table.Column<string>(type: "text", nullable: true),
                    engine_type = table.Column<string>(type: "text", nullable: true),
                    engine_energy = table.Column<string>(type: "text", nullable: true),
                    production_date_start = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    production_date_end = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    power = table.Column<string>(type: "text", nullable: true),
                    serial_number_from = table.Column<string>(type: "text", nullable: true),
                    serial_number_to = table.Column<string>(type: "text", nullable: true),
                    car_body_type = table.Column<string>(type: "text", nullable: true),
                    series = table.Column<string>(type: "text", nullable: true),
                    co2_emission_standard = table.Column<string>(type: "text", nullable: true),
                    transmission_type = table.Column<string>(type: "text", nullable: true),
                    engine_displacement = table.Column<string>(type: "text", nullable: true),
                    number_of_cylinders = table.Column<int>(type: "integer", nullable: true),
                    gvwr = table.Column<string>(type: "text", nullable: true),
                    tonnage = table.Column<string>(type: "text", nullable: true),
                    geographic_area = table.Column<string>(type: "text", nullable: true),
                    chassis_type = table.Column<string>(type: "text", nullable: true),
                    engine_model = table.Column<string>(type: "text", nullable: true),
                    cabin_type = table.Column<string>(type: "text", nullable: true),
                    capacity = table.Column<string>(type: "text", nullable: true),
                    engine_serial_number = table.Column<string>(type: "text", nullable: true),
                    is_ongoing = table.Column<bool>(type: "boolean", nullable: false),
                    is_discontinued = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_machine_applications_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    slot = table.Column<short>(type: "smallint", nullable: false),
                    image_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    content_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    uploaded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_images_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cross_references_oem_brand_oem_no_3",
                table: "cross_references",
                columns: new[] { "oem_brand", "oem_no_3" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_references_product_id",
                table: "cross_references",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_etl_progress_log_entity_type_finished_at",
                table: "etl_progress_log",
                columns: new[] { "entity_type", "finished_at" });

            migrationBuilder.CreateIndex(
                name: "ix_etl_progress_log_status",
                table: "etl_progress_log",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_machine_applications_machine_brand_machine_model",
                table: "machine_applications",
                columns: new[] { "machine_brand", "machine_model" });

            migrationBuilder.CreateIndex(
                name: "ix_machine_applications_product_id",
                table: "machine_applications",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_history_product_id_changed_at",
                table: "product_history",
                columns: new[] { "product_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_product_images_product_id",
                table: "product_images",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_images_product_id_slot",
                table: "product_images",
                columns: new[] { "product_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_products_type_d1",
                table: "products",
                columns: new[] { "type", "d1_mm" });

            migrationBuilder.CreateIndex(
                name: "idx_products_type_d2",
                table: "products",
                columns: new[] { "type", "d2_mm" });

            migrationBuilder.CreateIndex(
                name: "idx_products_type_h1",
                table: "products",
                columns: new[] { "type", "h1_mm" });

            migrationBuilder.CreateIndex(
                name: "ix_products_d1_mm",
                table: "products",
                column: "d1_mm");

            migrationBuilder.CreateIndex(
                name: "ix_products_d2_mm",
                table: "products",
                column: "d2_mm");

            migrationBuilder.CreateIndex(
                name: "ix_products_h1_mm",
                table: "products",
                column: "h1_mm");

            migrationBuilder.CreateIndex(
                name: "ix_products_oem_no_display",
                table: "products",
                column: "oem_no_display");

            migrationBuilder.CreateIndex(
                name: "ix_products_oem_no_normalized",
                table: "products",
                column: "oem_no_normalized");

            migrationBuilder.CreateIndex(
                name: "ix_products_type",
                table: "products",
                column: "type");

            migrationBuilder.CreateIndex(
                name: "idx_dead_letter_active_recovery",
                table: "search_index_dead_letter",
                columns: new[] { "status", "recovery_count", "last_recovery_at" },
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_search_index_dead_letter_moved_at",
                table: "search_index_dead_letter",
                column: "moved_at");

            migrationBuilder.CreateIndex(
                name: "ix_search_index_dead_letter_operation",
                table: "search_index_dead_letter",
                column: "operation");

            migrationBuilder.CreateIndex(
                name: "ix_search_index_dead_letter_status",
                table: "search_index_dead_letter",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_pending_retry",
                table: "search_index_pending",
                columns: new[] { "next_retry_at", "retry_count" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cross_references");

            migrationBuilder.DropTable(
                name: "etl_progress_log");

            migrationBuilder.DropTable(
                name: "machine_applications");

            migrationBuilder.DropTable(
                name: "product_history");

            migrationBuilder.DropTable(
                name: "product_images");

            migrationBuilder.DropTable(
                name: "search_index_dead_letter");

            migrationBuilder.DropTable(
                name: "search_index_pending");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
