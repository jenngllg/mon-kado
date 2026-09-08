using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeAuditIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_wishlist_moderation_events_occurred_at_id",
                schema: "public",
                table: "wishlist_moderation_events",
                columns: new[] { "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_administrative_data_export_events_created_at_id",
                schema: "public",
                table: "administrative_data_export_events",
                columns: new[] { "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wishlist_moderation_events_occurred_at_id",
                schema: "public",
                table: "wishlist_moderation_events");

            migrationBuilder.DropIndex(
                name: "ix_administrative_data_export_events_created_at_id",
                schema: "public",
                table: "administrative_data_export_events");
        }
    }
}
