using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlistSurpriseMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "surprise_mode",
                schema: "public",
                table: "wishlists",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "surprise_mode",
                schema: "public",
                table: "wishlists");
        }
    }
}
