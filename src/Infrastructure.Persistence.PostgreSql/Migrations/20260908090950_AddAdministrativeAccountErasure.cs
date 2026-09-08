using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrativeAccountErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "administrative_account_erasure_events",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notification_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_administrative_account_erasure_events", x => x.id);
                    table.CheckConstraint("ck_administrative_account_erasure_events_notification_status", "notification_status IN ('NotApplicable', 'Pending', 'Accepted', 'Failed')");
                    table.ForeignKey(
                        name: "fk_administrative_account_erasure_events_asp_net_users_administr",
                        column: x => x.administrator_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "account_erasure_email_outbox",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_recipient = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_erasure_email_outbox", x => x.id);
                    table.CheckConstraint("ck_account_erasure_email_attempts", "attempt_count >= 0");
                    table.CheckConstraint("ck_account_erasure_email_dates", "expires_at = created_at + interval '24 hours' AND available_at >= created_at");
                    table.CheckConstraint("ck_account_erasure_email_lease", "(lease_id IS NULL) = (locked_until IS NULL)");
                    table.ForeignKey(
                        name: "fk_account_erasure_email_outbox_administrative_account_erasure",
                        column: x => x.id,
                        principalSchema: "public",
                        principalTable: "administrative_account_erasure_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_erasure_email_outbox_available_at_id",
                schema: "public",
                table: "account_erasure_email_outbox",
                columns: new[] { "available_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_account_erasure_email_outbox_expires_at_id",
                schema: "public",
                table: "account_erasure_email_outbox",
                columns: new[] { "expires_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_administrative_account_erasure_events_administrator_id",
                schema: "public",
                table: "administrative_account_erasure_events",
                column: "administrator_id");

            migrationBuilder.CreateIndex(
                name: "ix_administrative_account_erasure_events_created_at_id",
                schema: "public",
                table: "administrative_account_erasure_events",
                columns: new[] { "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_erasure_email_outbox",
                schema: "public");

            migrationBuilder.DropTable(
                name: "administrative_account_erasure_events",
                schema: "public");
        }
    }
}
