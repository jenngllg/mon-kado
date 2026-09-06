using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberAccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.AddColumn<Guid>(
                name: "member_account_deletion_request_id",
                schema: "public",
                table: "authentication_email_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "member_account_deletion_requests",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_account_deletion_requests", x => x.id);
                    table.CheckConstraint("ck_member_account_deletion_requests_expiration", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_member_account_deletion_requests_asp_net_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_deletion_request_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'ACCOUNT_DELETION_CONFIRMATION') = (member_account_deletion_request_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NULL AND security_stamp_snapshot IS NULL) OR (kind = 'EMAIL_CHANGE_CONFIRMATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'EMAIL_CHANGE_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'PASSWORD_RESET' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'ACCOUNT_DELETION_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION', 'PASSWORD_RESET', 'PASSWORD_CHANGED_SECURITY_NOTIFICATION', 'ACCOUNT_DELETION_CONFIRMATION')");

            migrationBuilder.CreateIndex(
                name: "ix_member_account_deletion_requests_expires_at",
                schema: "public",
                table: "member_account_deletion_requests",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_member_account_deletion_requests_member_id",
                schema: "public",
                table: "member_account_deletion_requests",
                column: "member_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deletion confirmations cannot remain deliverable after reverting their supporting schema.
            migrationBuilder.Sql("DELETE FROM public.authentication_email_outbox WHERE kind = 'ACCOUNT_DELETION_CONFIRMATION';");
            migrationBuilder.DropTable(
                name: "member_account_deletion_requests",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_deletion_request_consistent",
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
                name: "member_account_deletion_request_id",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NULL AND security_stamp_snapshot IS NULL) OR (kind = 'EMAIL_CHANGE_CONFIRMATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'EMAIL_CHANGE_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'PASSWORD_RESET' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION', 'PASSWORD_RESET', 'PASSWORD_CHANGED_SECURITY_NOTIFICATION')");
        }
    }
}
