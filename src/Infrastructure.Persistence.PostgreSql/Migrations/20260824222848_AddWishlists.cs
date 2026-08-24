using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wishlists",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    occasion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: true),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlists", x => x.id);
                    table.CheckConstraint("ck_wishlists_name_valid", "char_length(btrim(name)) > 0 AND name !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_wishlists_occasion_valid", "occasion IN ('Birthday', 'Christmas', 'Wedding', 'Birth', 'Other')");
                    table.CheckConstraint("ck_wishlists_timestamps_consistent", "updated_at IS NULL OR updated_at >= created_at");
                    table.ForeignKey(
                        name: "fk_wishlists_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_wishlists_owner_normalized_name",
                schema: "public",
                table: "wishlists",
                columns: new[] { "owner_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wishlists",
                schema: "public");
        }
    }
}
