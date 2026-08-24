using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleExternalLogins : Migration
    {
        private static readonly string[] _googleLoginUniqueIndexColumns =
        [
            "user_id",
            "login_provider"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.Sql(
                """
                UPDATE public.authentication_email_outbox AS message
                SET security_stamp_snapshot =
                    COALESCE(member.security_stamp, '__MIGRATED_WITHOUT_SECURITY_STAMP__')
                FROM public.users AS member
                WHERE message.user_id = member.id
                  AND message.kind = 'EMAIL_CHANGE_CONFIRMATION';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NULL AND security_stamp_snapshot IS NULL) OR (kind = 'EMAIL_CHANGE_CONFIRMATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'EMAIL_CHANGE_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'PASSWORD_RESET' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL)");

            migrationBuilder.DropIndex(
                name: "ix_user_logins_user_id",
                schema: "public",
                table: "user_logins");

            migrationBuilder.AlterColumn<string>(
                name: "provider_key",
                schema: "public",
                table: "user_logins",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.CreateIndex(
                name: "ux_user_logins_user_id_login_provider",
                schema: "public",
                table: "user_logins",
                columns: _googleLoginUniqueIndexColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM public.user_logins
                        WHERE length(provider_key) > 128)
                    THEN
                        RAISE EXCEPTION
                            'Cannot roll back AddGoogleExternalLogins while user_logins.provider_key contains values longer than 128 characters.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox");

            migrationBuilder.Sql(
                """
                UPDATE public.authentication_email_outbox
                SET security_stamp_snapshot = NULL
                WHERE kind = 'EMAIL_CHANGE_CONFIRMATION';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_email_change_fields_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'EMAIL_CONFIRMATION' AND member_email_change_request_id IS NULL AND recipient_email IS NULL AND security_stamp_snapshot IS NULL) OR (kind IN ('EMAIL_CHANGE_CONFIRMATION', 'EMAIL_CHANGE_SECURITY_NOTIFICATION') AND member_email_change_request_id IS NOT NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL) OR (kind = 'PASSWORD_RESET' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NOT NULL) OR (kind = 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' AND member_email_change_request_id IS NULL AND recipient_email IS NOT NULL AND security_stamp_snapshot IS NULL)");

            migrationBuilder.DropIndex(
                name: "ux_user_logins_user_id_login_provider",
                schema: "public",
                table: "user_logins");

            migrationBuilder.AlterColumn<string>(
                name: "provider_key",
                schema: "public",
                table: "user_logins",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "public",
                table: "user_logins",
                column: "user_id");
        }
    }
}
