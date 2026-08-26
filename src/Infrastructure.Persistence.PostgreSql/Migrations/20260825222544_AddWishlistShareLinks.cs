using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlistShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wishlist_share_links",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", fixedLength: true, maxLength: 32, nullable: false),
                    protected_secret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_share_links", x => x.id);
                    table.CheckConstraint("ck_wishlist_share_links_secret_hash_length", "octet_length(secret_hash) = 32");
                    table.CheckConstraint("ck_wishlist_share_links_timestamps_consistent", "updated_at IS NULL OR updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_wishlist_share_links_wishlists_wishlist_id",
                        column: x => x.wishlist_id,
                        principalSchema: "public",
                        principalTable: "wishlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_share_links_secret_hash",
                schema: "public",
                table: "wishlist_share_links",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_share_links_wishlist_id",
                schema: "public",
                table: "wishlist_share_links",
                column: "wishlist_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wishlist_share_links",
                schema: "public");
        }
    }
}
