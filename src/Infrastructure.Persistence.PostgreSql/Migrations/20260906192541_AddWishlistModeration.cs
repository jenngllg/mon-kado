using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlistModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_suspended",
                schema: "public",
                table: "wishlists",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "suspended_at",
                schema: "public",
                table: "wishlists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_reason",
                schema: "public",
                table: "wishlists",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "wishlist_moderation_events",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_moderation_events", x => x.id);
                    table.CheckConstraint("ck_wishlist_moderation_events_action", "action IN ('Suspended', 'ReasonUpdated', 'Reactivated')");
                    table.CheckConstraint("ck_wishlist_moderation_events_reason", "(action = 'Reactivated' AND reason IS NULL) OR (action IN ('Suspended', 'ReasonUpdated') AND reason IS NOT NULL AND char_length(btrim(reason)) > 0)");
                    table.CheckConstraint("ck_wishlist_moderation_events_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_wishlist_moderation_events_users_administrator_id",
                        column: x => x.administrator_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_wishlist_moderation_events_wishlists_wishlist_id",
                        column: x => x.wishlist_id,
                        principalSchema: "public",
                        principalTable: "wishlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wishlist_moderation_email_outbox",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_moderation_email_outbox", x => x.id);
                    table.CheckConstraint("ck_wishlist_moderation_email_attempts", "attempt_count >= 0");
                    table.CheckConstraint("ck_wishlist_moderation_email_dates", "available_at >= created_at AND (processed_at IS NULL OR processed_at >= created_at)");
                    table.CheckConstraint("ck_wishlist_moderation_email_lease", "(lease_id IS NULL) = (locked_until IS NULL)");
                    table.ForeignKey(
                        name: "fk_wishlist_moderation_email_outbox_wishlist_moderation_events",
                        column: x => x.id,
                        principalSchema: "public",
                        principalTable: "wishlist_moderation_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "roles",
                columns: new[] { "id", "concurrency_stamp", "name", "normalized_name" },
                values: new object[] { new Guid("019ec170-1570-7000-8000-000000000001"), "019ec170-1570-7000-8000-000000000002", "Admin", "ADMIN" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_wishlists_suspension_consistent",
                schema: "public",
                table: "wishlists",
                sql: "(NOT is_suspended AND suspension_reason IS NULL AND suspended_at IS NULL) OR (is_suspended AND suspension_reason IS NOT NULL AND char_length(btrim(suspension_reason)) > 0 AND suspended_at IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_moderation_email_outbox_available_at_id",
                schema: "public",
                table: "wishlist_moderation_email_outbox",
                columns: new[] { "available_at", "id" },
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_moderation_email_outbox_processed_at_id",
                schema: "public",
                table: "wishlist_moderation_email_outbox",
                columns: new[] { "processed_at", "id" },
                filter: "processed_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_moderation_events_administrator_id",
                schema: "public",
                table: "wishlist_moderation_events",
                column: "administrator_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_moderation_events_wishlist_id_sequence",
                schema: "public",
                table: "wishlist_moderation_events",
                columns: new[] { "wishlist_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wishlist_moderation_email_outbox",
                schema: "public");

            migrationBuilder.DropTable(
                name: "wishlist_moderation_events",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wishlists_suspension_consistent",
                schema: "public",
                table: "wishlists");

            migrationBuilder.DeleteData(
                schema: "public",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("019ec170-1570-7000-8000-000000000001"));

            migrationBuilder.DropColumn(
                name: "is_suspended",
                schema: "public",
                table: "wishlists");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                schema: "public",
                table: "wishlists");

            migrationBuilder.DropColumn(
                name: "suspension_reason",
                schema: "public",
                table: "wishlists");
        }
    }
}
