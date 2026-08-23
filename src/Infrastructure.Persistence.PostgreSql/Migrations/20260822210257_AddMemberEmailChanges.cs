using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberEmailChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.AddColumn<Guid>(
                name: "member_email_change_request_id",
                schema: "public",
                table: "authentication_email_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_email",
                schema: "public",
                table: "authentication_email_outbox",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "member_email_change_requests",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    new_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_new_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_email_change_requests", x => x.id);
                    table.CheckConstraint("ck_member_email_change_requests_emails_different", "current_email <> new_email");
                    table.CheckConstraint("ck_member_email_change_requests_timestamps_consistent", "expires_at > created_at AND (confirmed_at IS NULL OR confirmed_at >= created_at) AND (revoked_at IS NULL OR revoked_at >= created_at) AND NOT (confirmed_at IS NOT NULL AND revoked_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_member_email_change_requests_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_authentication_email_outbox_member_email_change_request_id",
                schema: "public",
                table: "authentication_email_outbox",
                column: "member_email_change_request_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NULL) OR (kind IN ('EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION') AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION')");

            migrationBuilder.CreateIndex(
                name: "ix_member_email_change_requests_expires_at",
                schema: "public",
                table: "member_email_change_requests",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_member_email_change_requests_active_user",
                schema: "public",
                table: "member_email_change_requests",
                column: "user_id",
                unique: true,
                filter: "confirmed_at IS NULL AND revoked_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_authentication_email_outbox_member_email_change_request_id",
                schema: "public",
                table: "authentication_email_outbox",
                column: "member_email_change_request_id",
                principalSchema: "public",
                principalTable: "member_email_change_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM public.authentication_email_outbox
                WHERE kind IN ('EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION');
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_authentication_email_outbox_member_email_change_request_id",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.DropTable(
                name: "member_email_change_requests",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_authentication_email_outbox_member_email_change_request_id",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.DropColumn(
                name: "member_email_change_request_id",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.DropColumn(
                name: "recipient_email",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION')");
        }
    }
}
