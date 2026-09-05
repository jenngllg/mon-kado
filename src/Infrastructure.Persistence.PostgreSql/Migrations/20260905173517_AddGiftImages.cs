using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftImages : Migration
    {
        private static readonly string[] _availableImageDeletionColumns =
        [
            "available_at",
            "created_at"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "image_content_hash",
                schema: "public",
                table: "wishes",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "image_id",
                schema: "public",
                table: "wishes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gift_image_deletion_outbox",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_image_deletion_outbox", x => x.id);
                    table.CheckConstraint("ck_gift_image_deletion_outbox_attempt_count_non_negative", "attempt_count >= 0");
                    table.CheckConstraint("ck_gift_image_deletion_outbox_timestamps_consistent", "available_at >= created_at");
                });

            migrationBuilder.CreateIndex(
                name: "ux_wishes_image_id",
                schema: "public",
                table: "wishes",
                column: "image_id",
                unique: true,
                filter: "image_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wishes_image_fields_consistent",
                schema: "public",
                table: "wishes",
                sql: "(image_id IS NULL AND image_content_hash IS NULL) OR " +
                    "(image_id IS NOT NULL AND image_content_hash IS NOT NULL AND octet_length(image_content_hash) = 32)");

            migrationBuilder.CreateIndex(
                name: "ix_gift_image_deletion_outbox_available",
                schema: "public",
                table: "gift_image_deletion_outbox",
                columns: _availableImageDeletionColumns);

            migrationBuilder.CreateIndex(
                name: "ux_gift_image_deletion_outbox_image_id",
                schema: "public",
                table: "gift_image_deletion_outbox",
                column: "image_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gift_image_deletion_outbox",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ux_wishes_image_id",
                schema: "public",
                table: "wishes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wishes_image_fields_consistent",
                schema: "public",
                table: "wishes");

            migrationBuilder.DropColumn(
                name: "image_content_hash",
                schema: "public",
                table: "wishes");

            migrationBuilder.DropColumn(
                name: "image_id",
                schema: "public",
                table: "wishes");
        }
    }
}
