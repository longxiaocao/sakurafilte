using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexOnOemNoNormalized : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_oem_no_normalized",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "ix_products_oem_no_normalized",
                table: "products",
                column: "oem_no_normalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_oem_no_normalized",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "ix_products_oem_no_normalized",
                table: "products",
                column: "oem_no_normalized");
        }
    }
}
