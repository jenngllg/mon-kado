using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberPasswordChanges : Migration
    {
        private static readonly string[] _pendingUserKindIndexColumns =
        [
            "user_id",
            "kind"
        ];

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
                columns: _pendingUserKindIndexColumns,
                unique: true,
                filter: "processed_at IS NULL AND kind <> 'PASSWORD_CHANGED_SECURITY_NOTIFICATION'");

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

            migrationBuilder.Sql(
                "DELETE FROM public.authentication_email_outbox " +
                "WHERE kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION'");

            migrationBuilder.CreateIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
                schema: "public",
                table: "authentication_email_outbox",
                columns: _pendingUserKindIndexColumns,
                unique: true,
                filter: "processed_at IS NULL");

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
        }
    }
}
