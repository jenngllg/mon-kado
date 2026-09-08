using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeDataExportEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrative_data_export_events",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    export_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    request_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_administrative_data_export_events", x => x.id);
                    table.CheckConstraint("ck_administrative_data_export_events_action", "action IN ('Requested', 'DownloadStarted')");
                    table.ForeignKey(
                        name: "fk_administrative_data_export_events_asp_net_users_administrator",
                        column: x => x.administrator_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_administrative_data_export_events_asp_net_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_administrative_data_export_events_administrator_id",
                schema: "public",
                table: "administrative_data_export_events",
                column: "administrator_id");

            migrationBuilder.CreateIndex(
                name: "ix_administrative_data_export_events_created_at",
                schema: "public",
                table: "administrative_data_export_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_administrative_data_export_events_member_id_export_id_creat",
                schema: "public",
                table: "administrative_data_export_events",
                columns: new[] { "member_id", "export_id", "created_at", "id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "administrative_data_export_events",
                schema: "public");
        }
    }
}
