using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlistParticipants : Migration
    {
        private static readonly string[] _wishlistGuestColumns =
        [
            "wishlist_id",
            "guest_session_id"
        ];

        private static readonly string[] _wishlistMemberColumns =
        [
            "wishlist_id",
            "member_id"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guest_sessions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guest_sessions", x => x.id);
                    table.CheckConstraint("ck_guest_sessions_secret_hash_length", "octet_length(secret_hash) = 32");
                });

            migrationBuilder.CreateTable(
                name: "wishlist_participants",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_participants", x => x.id);
                    table.CheckConstraint("ck_wishlist_participants_identity", "member_id IS NULL OR guest_session_id IS NULL");
                    table.ForeignKey(
                        name: "fk_wishlist_participants_guest_sessions_guest_session_id",
                        column: x => x.guest_session_id,
                        principalSchema: "public",
                        principalTable: "guest_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_wishlist_participants_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_wishlist_participants_wishlists_wishlist_id",
                        column: x => x.wishlist_id,
                        principalSchema: "public",
                        principalTable: "wishlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guest_sessions_expires_at",
                schema: "public",
                table: "guest_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_participants_guest_session_id",
                schema: "public",
                table: "wishlist_participants",
                column: "guest_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_participants_member_id",
                schema: "public",
                table: "wishlist_participants",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_participants_wishlist_guest_session",
                schema: "public",
                table: "wishlist_participants",
                columns: _wishlistGuestColumns,
                unique: true,
                filter: "guest_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_participants_wishlist_member",
                schema: "public",
                table: "wishlist_participants",
                columns: _wishlistMemberColumns,
                unique: true,
                filter: "member_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wishlist_participants",
                schema: "public");

            migrationBuilder.DropTable(
                name: "guest_sessions",
                schema: "public");
        }
    }
}
