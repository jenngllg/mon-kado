using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wish_position_sequences",
                schema: "public",
                columns: table => new
                {
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_position = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wish_position_sequences", x => x.wishlist_id);
                    table.CheckConstraint("ck_wish_position_sequences_next_position_valid", "next_position > 0");
                    table.ForeignKey(
                        name: "fk_wish_position_sequences_wishlists_wishlist_id",
                        column: x => x.wishlist_id,
                        principalSchema: "public",
                        principalTable: "wishlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wishes",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    position = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishes", x => x.id);
                    table.CheckConstraint("ck_wishes_name_valid", "char_length(btrim(name)) > 0 AND name !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_wishes_position_valid", "position > 0");
                    table.CheckConstraint("ck_wishes_price_valid", "price IS NULL OR price > 0");
                    table.CheckConstraint("ck_wishes_timestamps_consistent", "updated_at IS NULL OR updated_at >= created_at");
                    table.CheckConstraint("ck_wishes_url_valid", "url IS NULL OR (url ~* '^https?://[^[:space:]]+$' AND position('@' in split_part(split_part(split_part(url, '/', 3), '?', 1), '#', 1)) = 0)");
                    table.ForeignKey(
                        name: "fk_wishes_wishlists_wishlist_id",
                        column: x => x.wishlist_id,
                        principalSchema: "public",
                        principalTable: "wishlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_wishes_wishlist_position",
                schema: "public",
                table: "wishes",
                columns: new[] { "wishlist_id", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wishes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "wish_position_sequences",
                schema: "public");
        }
    }
}
