using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDictOemNo3SortIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_dict_oem_no3_sort",
                table: "dict_oem_no3",
                columns: new[] { "sort_order", "oem_no_3" },
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_dict_oem_no3_sort",
                table: "dict_oem_no3");
        }
    }
}
