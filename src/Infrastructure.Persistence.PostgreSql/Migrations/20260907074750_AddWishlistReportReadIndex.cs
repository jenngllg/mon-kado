using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlistReportReadIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wishlist_reports_wishlist_id",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_reports_wishlist_id_created_at_id",
                schema: "public",
                table: "wishlist_reports",
                columns: new[] { "wishlist_id", "created_at", "id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wishlist_reports_wishlist_id_created_at_id",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_reports_wishlist_id",
                schema: "public",
                table: "wishlist_reports",
                column: "wishlist_id");
        }
    }
}
