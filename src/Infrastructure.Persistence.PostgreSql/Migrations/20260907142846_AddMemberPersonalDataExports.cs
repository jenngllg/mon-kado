using Microsoft.EntityFrameworkCore.Migrations;

using System;

#nullable disable
namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc/>
    public partial class AddMemberPersonalDataExports : Migration
    {
        /// <inheritdoc/>
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
            migrationBuilder.AddColumn<Guid>(
                name: "member_data_export_id",
                schema: "public",
                table: "authentication_email_outbox",
                type: "uuid",
                nullable: true);
            migrationBuilder.CreateTable(
                name: "member_data_exports",
                schema: "public",
                columns: table => new {
                id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                member_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                status = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false),
                created_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                available_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: false),
                attempt_count = table.Column<int>(
                    type: "integer",
                    nullable: false),
                lease_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                locked_until = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                archive_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),
                snapshot_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                ready_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                expires_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true),
                size_in_bytes = table.Column<long>(
                    type: "bigint",
                    nullable: true),
                failure = table.Column<string>(
                    type: "character varying(30)",
                    maxLength: 30,
                    nullable: true),
                files_cleaned_at = table.Column<DateTime>(
                    type: "timestamp with time zone",
                    nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "pk_member_data_exports",
                        x => x.id);
                    table.CheckConstraint(
                        "ck_member_data_exports_archive",
                        "(status IN ('Ready', 'Expired') AND archive_id IS NOT NULL AND snapshot_at IS NOT NULL AND ready_at IS NOT NULL AND expires_at IS NOT NULL AND size_in_bytes IS NOT NULL) OR (status NOT IN ('Ready', 'Expired') AND archive_id IS NULL AND snapshot_at IS NULL AND ready_at IS NULL AND expires_at IS NULL AND size_in_bytes IS NULL)");
                    table.CheckConstraint(
                        "ck_member_data_exports_attempts",
                        "attempt_count >= 0");
                    table.CheckConstraint(
                        "ck_member_data_exports_dates",
                        "available_at >= created_at AND (expires_at IS NULL OR expires_at > ready_at) AND (size_in_bytes IS NULL OR size_in_bytes > 0)");
                    table.CheckConstraint(
                        "ck_member_data_exports_failure",
                        "(status = 'Failed' AND failure IS NOT NULL AND failure IN ('GenerationFailed', 'TooLarge')) OR (status <> 'Failed' AND failure IS NULL)");
                    table.CheckConstraint(
                        "ck_member_data_exports_lease",
                        "(status = 'Processing' AND lease_id IS NOT NULL AND locked_until IS NOT NULL) OR (status <> 'Processing' AND lease_id IS NULL AND locked_until IS NULL)");
                    table.CheckConstraint(
                        "ck_member_data_exports_status",
                        "status IN ('Queued', 'Processing', 'Ready', 'Failed', 'Expired')");
                    table.ForeignKey(
                        name: "fk_member_data_exports_asp_net_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });
            migrationBuilder.CreateIndex(
                name: "ix_authentication_email_outbox_member_data_export_id",
                schema: "public",
                table: "authentication_email_outbox",
                column: "member_data_export_id",
                unique: true,
                filter: "member_data_export_id IS NOT NULL");
            migrationBuilder.CreateIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
                schema: "public",
                table: "authentication_email_outbox",
                columns: new[] {
                    "user_id",
                    "kind"
                },
                unique: true,
                filter: "processed_at IS NULL AND kind NOT IN ('PASSWORD_CHANGED_SECURITY_NOTIFICATION', 'PERSONAL_DATA_EXPORT_READY')");
            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_email_outbox_data_export_consistent",
                schema: "public",
                table: "authentication_email_outbox",
                sql: "(kind = 'PERSONAL_DATA_EXPORT_READY') = (member_data_export_id IS NOT NULL)");
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
            migrationBuilder.CreateIndex(
                name: "ix_member_data_exports_available_at_created_at_id",
                schema: "public",
                table: "member_data_exports",
                columns: new[] {
                    "available_at",
                    "created_at",
                    "id"
                },
                filter: "status IN ('Queued', 'Processing')");
            migrationBuilder.CreateIndex(
                name: "ix_member_data_exports_expires_at",
                schema: "public",
                table: "member_data_exports",
                column: "expires_at",
                filter: "status = 'Ready'");
            migrationBuilder.CreateIndex(
                name: "ix_member_data_exports_member_id_created_at_id",
                schema: "public",
                table: "member_data_exports",
                columns: new[] {
                    "member_id",
                    "created_at",
                    "id"
                },
                descending: new[] {
                    false,
                    true,
                    true
                });
            migrationBuilder.CreateIndex(
                name: "ux_member_data_exports_active_member",
                schema: "public",
                table: "member_data_exports",
                column: "member_id",
                unique: true,
                filter: "member_id IS NOT NULL AND status IN ('Queued', 'Processing', 'Ready')");
            migrationBuilder.AddForeignKey(
                name: "fk_authentication_email_outbox_member_data_exports_member_data",
                schema: "public",
                table: "authentication_email_outbox",
                column: "member_data_export_id",
                principalSchema: "public",
                principalTable: "member_data_exports",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM public.authentication_email_outbox WHERE kind = 'PERSONAL_DATA_EXPORT_READY';");
            migrationBuilder.DropForeignKey(
                name: "fk_authentication_email_outbox_member_data_exports_member_data",
                schema: "public",
                table: "authentication_email_outbox");
            migrationBuilder.DropTable(
                name: "member_data_exports",
                schema: "public");
            migrationBuilder.DropIndex(
                name: "ix_authentication_email_outbox_member_data_export_id",
                schema: "public",
                table: "authentication_email_outbox");
            migrationBuilder.DropIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
                schema: "public",
                table: "authentication_email_outbox");
            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_email_outbox_data_export_consistent",
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
                name: "member_data_export_id",
                schema: "public",
                table: "authentication_email_outbox");
            migrationBuilder.CreateIndex(
                name: "ux_authentication_email_outbox_pending_user_kind",
                schema: "public",
                table: "authentication_email_outbox",
                columns: new[] {
                    "user_id",
                    "kind"
                },
                unique: true,
                filter: "processed_at IS NULL AND kind <> 'PASSWORD_CHANGED_SECURITY_NOTIFICATION'");
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
        }
    }
}
