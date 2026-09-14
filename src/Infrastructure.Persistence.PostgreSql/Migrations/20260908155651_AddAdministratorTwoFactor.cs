using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministratorTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "two_factor_credential_id",
                schema: "public",
                table: "authentication_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "two_factor_verified_at",
                schema: "public",
                table: "authentication_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "member_two_factors",
                schema: "public",
                columns: table => new
                {
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    protected_secret = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    enabled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_accepted_time_step = table.Column<long>(type: "bigint", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verification_window_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verification_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_two_factors", x => x.member_id);
                    table.CheckConstraint("ck_member_two_factors_attempts_nonnegative", "failed_attempts >= 0 AND verification_count >= 0");
                    table.CheckConstraint("ck_member_two_factors_credential_consistent", "(credential_id IS NULL AND protected_secret IS NULL AND enabled_at IS NULL AND last_accepted_time_step IS NULL) OR (credential_id IS NOT NULL AND protected_secret IS NOT NULL AND enabled_at IS NOT NULL AND last_accepted_time_step IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_member_two_factors_asp_net_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "two_factor_challenges",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    required_action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invalidated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expected_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    security_stamp_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    is_persistent = table.Column<bool>(type: "boolean", nullable: false),
                    previous_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    management_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    google_flow_id = table.Column<Guid>(type: "uuid", nullable: true),
                    protected_google_context = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: true),
                    pending_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_protected_secret = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    reserved_recovery_code_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    result_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_access_token_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_two_factor_challenges", x => x.id);
                    table.CheckConstraint("ck_two_factor_challenges_absolute_expiration", "expires_at = created_at + interval '5 minutes'");
                    table.CheckConstraint("ck_two_factor_challenges_action_valid", "required_action IN ('Verify', 'Enroll', 'Replace', 'Complete')");
                    table.CheckConstraint("ck_two_factor_challenges_candidate_consistent", "(pending_credential_id IS NULL) = (pending_protected_secret IS NULL)");
                    table.CheckConstraint("ck_two_factor_challenges_hash_lengths", "octet_length(flow_hash) = 32 AND octet_length(security_stamp_hash) = 32");
                    table.CheckConstraint("ck_two_factor_challenges_purpose_valid", "purpose IN ('SignIn', 'ReplaceAuthenticator', 'RegenerateRecoveryCodes')");
                    table.CheckConstraint("ck_two_factor_challenges_receipt_consistent", "(result_session_id IS NULL) = (result_access_token_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_two_factor_challenges_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "two_factor_recovery_codes",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    reserved_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reserved_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_two_factor_recovery_codes", x => x.id);
                    table.CheckConstraint("ck_two_factor_recovery_codes_consumption_reserved", "consumed_at IS NULL OR reserved_challenge_id IS NOT NULL");
                    table.CheckConstraint("ck_two_factor_recovery_codes_hash_length", "octet_length(code_hash) = 32");
                    table.CheckConstraint("ck_two_factor_recovery_codes_reservation_consistent", "(reserved_challenge_id IS NULL) = (reserved_until IS NULL)");
                    table.ForeignKey(
                        name: "fk_two_factor_recovery_codes_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_authentication_sessions_two_factor_proof_consistent",
                schema: "public",
                table: "authentication_sessions",
                sql: "(two_factor_credential_id IS NULL) = (two_factor_verified_at IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_two_factor_challenges_expires_at_id",
                schema: "public",
                table: "two_factor_challenges",
                columns: new[] { "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_two_factor_challenges_flow_hash",
                schema: "public",
                table: "two_factor_challenges",
                column: "flow_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_two_factor_challenges_google_flow_id",
                schema: "public",
                table: "two_factor_challenges",
                column: "google_flow_id",
                unique: true,
                filter: "google_flow_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_two_factor_challenges_member_id",
                schema: "public",
                table: "two_factor_challenges",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_two_factor_recovery_codes_member_id_code_hash",
                schema: "public",
                table: "two_factor_recovery_codes",
                columns: new[] { "member_id", "code_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_two_factors",
                schema: "public");

            migrationBuilder.DropTable(
                name: "two_factor_challenges",
                schema: "public");

            migrationBuilder.DropTable(
                name: "two_factor_recovery_codes",
                schema: "public");

            migrationBuilder.DropCheckConstraint(
                name: "ck_authentication_sessions_two_factor_proof_consistent",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.DropColumn(
                name: "two_factor_credential_id",
                schema: "public",
                table: "authentication_sessions");

            migrationBuilder.DropColumn(
                name: "two_factor_verified_at",
                schema: "public",
                table: "authentication_sessions");
        }
    }
}
