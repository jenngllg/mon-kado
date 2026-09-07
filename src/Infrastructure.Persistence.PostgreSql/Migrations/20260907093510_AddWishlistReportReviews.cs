using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlistReportReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_note",
                schema: "public",
                table: "wishlist_reports",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                schema: "public",
                table: "wishlist_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_administrator_id",
                schema: "public",
                table: "wishlist_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "public",
                table: "wishlist_reports",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "public",
                table: "wishlist_reports",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "wishlist_report_review_events",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    administrator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_report_review_events", x => x.id);
                    table.CheckConstraint("ck_wishlist_report_review_events_sequence", "sequence > 0");
                    table.CheckConstraint("ck_wishlist_report_review_events_status", "status IN ('Pending', 'Upheld', 'Dismissed') AND previous_status IN ('Pending', 'Upheld', 'Dismissed')");
                    table.ForeignKey(
                        name: "fk_wishlist_report_review_events_users_administrator_id",
                        column: x => x.administrator_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_wishlist_report_review_events_wishlist_reports_report_id",
                        column: x => x.report_id,
                        principalSchema: "public",
                        principalTable: "wishlist_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_reports_reviewed_by_administrator_id",
                schema: "public",
                table: "wishlist_reports",
                column: "reviewed_by_administrator_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_reports_wishlist_id_status_created_at_id",
                schema: "public",
                table: "wishlist_reports",
                columns: new[] { "wishlist_id", "status", "created_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.AddCheckConstraint(
                name: "ck_wishlist_reports_review_consistent",
                schema: "public",
                table: "wishlist_reports",
                sql: "(reviewed_at IS NULL AND status = 'Pending' AND review_note IS NULL AND reviewed_by_administrator_id IS NULL) OR (reviewed_at IS NOT NULL AND reviewed_at >= created_at)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wishlist_reports_status",
                schema: "public",
                table: "wishlist_reports",
                sql: "status IN ('Pending', 'Upheld', 'Dismissed')");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_report_review_events_administrator_id",
                schema: "public",
                table: "wishlist_report_review_events",
                column: "administrator_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_report_review_events_report_id_sequence",
                schema: "public",
                table: "wishlist_report_review_events",
                columns: new[] { "report_id", "sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_wishlist_reports_users_reviewed_by_administrator_id",
                schema: "public",
                table: "wishlist_reports",
                column: "reviewed_by_administrator_id",
                principalSchema: "public",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_wishlist_reports_users_reviewed_by_administrator_id",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropTable(
                name: "wishlist_report_review_events",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_wishlist_reports_reviewed_by_administrator_id",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropIndex(
                name: "ix_wishlist_reports_wishlist_id_status_created_at_id",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wishlist_reports_review_consistent",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wishlist_reports_status",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropColumn(
                name: "review_note",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropColumn(
                name: "reviewed_by_administrator_id",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "public",
                table: "wishlist_reports");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "public",
                table: "wishlist_reports");
        }
    }
}
