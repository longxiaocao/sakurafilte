using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRawParameterValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bypass_pressure_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bypass_valve_hr_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bypass_valve_lr_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "collapse_pressure_bar_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "no_bypass_valves_raw",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "no_check_valves_raw",
                table: "products",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bypass_pressure_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "bypass_valve_hr_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "bypass_valve_lr_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "collapse_pressure_bar_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "no_bypass_valves_raw",
                table: "products");

            migrationBuilder.DropColumn(
                name: "no_check_valves_raw",
                table: "products");
        }
    }
}
