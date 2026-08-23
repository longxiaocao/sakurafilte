using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "machine_category",
                table: "dict_machine",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "others");

            migrationBuilder.CreateIndex(
                name: "idx_dict_machine_category",
                table: "dict_machine",
                columns: new[] { "deleted_at", "machine_category" },
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_dict_machine_category",
                table: "dict_machine");

            migrationBuilder.DropColumn(
                name: "machine_category",
                table: "dict_machine");
        }
    }
}
