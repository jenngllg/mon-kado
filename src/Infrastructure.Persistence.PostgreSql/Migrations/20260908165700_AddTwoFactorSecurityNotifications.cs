using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorSecurityNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
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

            migrationBuilder.CreateIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
                schema: "public",
                table: "authentication_email_outbox",
                columns: new[] { "user_id", "kind" },
                unique: true,
                filter: "processed_at IS NULL AND kind NOT IN ('PASSWORD_CHANGED_SECURITY_NOTIFICATION', 'PERSONAL_DATA_EXPORT_READY', 'TWO_FACTOR_ENROLLED', 'TWO_FACTOR_REPLACED', 'TWO_FACTOR_RECOVERY_CODES_REGENERATED', 'TWO_FACTOR_RECOVERY_CODE_USED')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind IN ('EMAIL_CONFIRMATION', 'PERSONAL_DATA_EXPORT_READY') AND member_email_change_request_id IS NULL AND recipient_email IS NULL AND security_stamp_snapshot IS NULL) OR (kind = 'EMAIL_CHANGE_CONFIRMATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'EMAIL_CHANGE_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'PASSWORD_RESET' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind IN ('PASSWORD_CHANGED_SECURITY_NOTIFICATION', 'TWO_FACTOR_ENROLLED', 'TWO_FACTOR_REPLACED', 'TWO_FACTOR_RECOVERY_CODES_REGENERATED', 'TWO_FACTOR_RECOVERY_CODE_USED') AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'ACCOUNT_DELETION_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION', 'PASSWORD_RESET', 'PASSWORD_CHANGED_SECURITY_NOTIFICATION', 'ACCOUNT_DELETION_CONFIRMATION', 'PERSONAL_DATA_EXPORT_READY', 'TWO_FACTOR_ENROLLED', 'TWO_FACTOR_REPLACED', 'TWO_FACTOR_RECOVERY_CODES_REGENERATED', 'TWO_FACTOR_RECOVERY_CODE_USED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
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

            migrationBuilder.CreateIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
                schema: "public",
                table: "authentication_email_outbox",
                columns: new[] { "user_id", "kind" },
                unique: true,
                filter: "processed_at IS NULL AND kind NOT IN ('PASSWORD_CHANGED_SECURITY_NOTIFICATION', 'PERSONAL_DATA_EXPORT_READY')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind IN ('EMAIL_CONFIRMATION', 'PERSONAL_DATA_EXPORT_READY') AND member_email_change_request_id IS NULL AND recipient_email IS NULL AND security_stamp_snapshot IS NULL) OR (kind = 'EMAIL_CHANGE_CONFIRMATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'EMAIL_CHANGE_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'PASSWORD_RESET' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'ACCOUNT_DELETION_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_kind_valid",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "kind IN ('EMAIL_CONFIRMATION', 'EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION', 'PASSWORD_RESET', 'PASSWORD_CHANGED_SECURITY_NOTIFICATION', 'ACCOUNT_DELETION_CONFIRMATION', 'PERSONAL_DATA_EXPORT_READY')");
        }
    }
}
