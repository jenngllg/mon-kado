using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quantity",
                schema: "public",
                table: "wishes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                "ALTER TABLE public.wishes ALTER COLUMN quantity DROP DEFAULT;");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_wishlist_participants_wishlist_id_id",
                schema: "public",
                table: "wishlist_participants",
                columns: new[] { "wishlist_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_wishes_wishlist_id_id",
                schema: "public",
                table: "wishes",
                columns: new[] { "wishlist_id", "id" });

            migrationBuilder.CreateTable(
                name: "gift_reservations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_reservations", x => x.id);
                    table.CheckConstraint("ck_gift_reservations_quantity_valid", "quantity BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_gift_reservations_timestamps_consistent", "updated_at IS NULL OR updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_gift_reservations_participants_wishlist_id_participant_id",
                        columns: x => new { x.wishlist_id, x.wishlist_participant_id },
                        principalSchema: "public",
                        principalTable: "wishlist_participants",
                        principalColumns: new[] { "wishlist_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_gift_reservations_wishes_wishlist_id_wish_id",
                        columns: x => new { x.wishlist_id, x.wish_id },
                        principalSchema: "public",
                        principalTable: "wishes",
                        principalColumns: new[] { "wishlist_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_wishes_quantity_valid",
                schema: "public",
                table: "wishes",
                sql: "quantity BETWEEN 1 AND 100");

            migrationBuilder.CreateIndex(
                name: "ix_gift_reservations_wishlist_id_wish_id",
                schema: "public",
                table: "gift_reservations",
                columns: new[] { "wishlist_id", "wish_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gift_reservations_wishlist_id_wishlist_participant_id",
                schema: "public",
                table: "gift_reservations",
                columns: new[] { "wishlist_id", "wishlist_participant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_gift_reservations_participant_wish",
                schema: "public",
                table: "gift_reservations",
                columns: new[] { "wishlist_participant_id", "wish_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gift_reservations",
                schema: "public");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_wishlist_participants_wishlist_id_id",
                schema: "public",
                table: "wishlist_participants");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_wishes_wishlist_id_id",
                schema: "public",
                table: "wishes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wishes_quantity_valid",
                schema: "public",
                table: "wishes");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "public",
                table: "wishes");
        }
    }
}
