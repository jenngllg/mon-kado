using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceAuthenticationTicketsWithRefreshSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_sessions_ticket_not_empty",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_sessions_timestamps_consistent",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.Sql("DELETE FROM public.authentication_sessions;");

            migrationBuilder.RenameColumn(
                name: "protected_ticket",
                schema: "public",
                table: "authentication_sessions",
                newName: "refresh_token_hash");

            migrationBuilder.AddColumn<bool>(
                name: "is_persistent",
                schema: "public",
                table: "authentication_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "revoked_at",
                schema: "public",
                table: "authentication_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_sessions_refresh_token_hash_length",
                schema: "public",
                table: "authentication_sessions",
                sql: "octet_length(refresh_token_hash) = 32");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_sessions_timestamps_consistent",
                schema: "public",
                table: "authentication_sessions",
                sql: "renewed_at >= created_at AND expires_at > created_at AND expires_at >= renewed_at AND (revoked_at IS NULL OR revoked_at >= created_at)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_sessions_refresh_token_hash_length",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_sessions_timestamps_consistent",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.Sql("DELETE FROM public.authentication_sessions;");

            migrationBuilder.DropColumn(
                name: "is_persistent",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.DropColumn(
                name: "revoked_at",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.RenameColumn(
                name: "refresh_token_hash",
                schema: "public",
                table: "authentication_sessions",
                newName: "protected_ticket");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_sessions_ticket_not_empty",
                schema: "public",
                table: "authentication_sessions",
                sql: "octet_length(protected_ticket) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_sessions_timestamps_consistent",
                schema: "public",
                table: "authentication_sessions",
                sql: "renewed_at >= created_at AND expires_at > created_at AND expires_at >= renewed_at");
        }
    }
}
