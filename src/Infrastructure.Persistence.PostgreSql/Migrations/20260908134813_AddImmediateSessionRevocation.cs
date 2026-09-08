using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddImmediateSessionRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrative_session_revocation_events",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_administrative_session_revocation_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_administrative_session_revocation_events_asp_net_users_admini",
                        column: x => x.administrator_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_administrative_session_revocation_events_asp_net_users_member",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "authentication_access_tokens",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authentication_access_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_authentication_access_tokens_authentication_sessions_sessio",
                        column: x => x.session_id,
                        principalSchema: "public",
                        principalTable: "authentication_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_administrative_session_revocation_events_administrator_id",
                schema: "public",
                table: "administrative_session_revocation_events",
                column: "administrator_id");

            migrationBuilder.CreateIndex(
                name: "ix_administrative_session_revocation_events_created_at_id",
                schema: "public",
                table: "administrative_session_revocation_events",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_administrative_session_revocation_events_member_id",
                schema: "public",
                table: "administrative_session_revocation_events",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_authentication_access_tokens_expires_at",
                schema: "public",
                table: "authentication_access_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_authentication_access_tokens_session_id",
                schema: "public",
                table: "authentication_access_tokens",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "administrative_session_revocation_events",
                schema: "public");

            migrationBuilder.DropTable(
                name: "authentication_access_tokens",
                schema: "public");
        }
    }
}
