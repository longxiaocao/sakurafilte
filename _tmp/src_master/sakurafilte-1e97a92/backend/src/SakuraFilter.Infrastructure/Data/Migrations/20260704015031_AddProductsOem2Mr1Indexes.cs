using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductsOem2Mr1Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_products_mr_1",
                table: "products",
                column: "mr_1");

            migrationBuilder.CreateIndex(
                name: "ix_products_oem_2",
                table: "products",
                column: "oem_2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_mr_1",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_oem_2",
                table: "products");
        }
    }
}
