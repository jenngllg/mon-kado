using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddAuthenticationSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "authentication_sessions",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                protected_ticket = table.Column<byte[]>(type: "bytea", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                renewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_authentication_sessions", x => x.id);
                table.CheckConstraint("ck_authentication_sessions_ticket_not_empty", "octet_length(protected_ticket) > 0");
                table.CheckConstraint("ck_authentication_sessions_timestamps_consistent", "renewed_at >= created_at AND expires_at > created_at AND expires_at >= renewed_at");
                table.ForeignKey(
                    name: "fk_authentication_sessions_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_authentication_sessions_expires_at",
            schema: "public",
            table: "authentication_sessions",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "ix_authentication_sessions_user_id",
            schema: "public",
            table: "authentication_sessions",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "authentication_sessions",
            schema: "public");
    }
}
