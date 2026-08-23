using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberPasswordResets : Migration
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

            migrationBuilder.AddColumn<string>(
                name: "security_stamp_snapshot",
                schema: "public",
                table: "authentication_email_outbox",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NULL AND security_stamp_snapshot IS NULL) OR (kind IN ('EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION') AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'PASSWORD_RESET' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION', 'PASSWORD_RESET', 'PASSWORD_CHANGED_SECURITY_NOTIFICATION')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.Sql(
                "DELETE FROM public.authentication_email_outbox WHERE kind = 'PASSWORD_RESET'");

            migrationBuilder.DropColumn(
                name: "security_stamp_snapshot",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NULL) OR (kind IN ('EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION') AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL) OR (kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION', 'PASSWORD_CHANGED_SECURITY_NOTIFICATION')");
        }
    }
}
