using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationEmailRetentionCleanupIndex : Migration
    {
        private static readonly string[] _processedCleanupColumns =
        [
            "processed_at",
            "id"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_authentication_email_outbox_processed_cleanup",
                schema: "public",
                table: "authentication_email_outbox",
                columns: _processedCleanupColumns,
                filter: "processed_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_authentication_email_outbox_processed_cleanup",
                schema: "public",
                table: "authentication_email_outbox");
        }
    }
}
