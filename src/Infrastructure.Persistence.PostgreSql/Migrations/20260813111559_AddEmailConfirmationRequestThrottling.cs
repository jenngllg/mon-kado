using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations;

/// <inheritdoc />
public partial class AddEmailConfirmationRequestThrottling : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_authentication_email_outbox_user_kind_created_at",
            schema: "public",
            table: "authentication_email_outbox",
            columns: new[] { "user_id", "kind", "created_at" },
            descending: new[] { false, false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_authentication_email_outbox_user_kind_created_at",
            schema: "public",
            table: "authentication_email_outbox");
    }
}
