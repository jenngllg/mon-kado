using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberSearchVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_visible_in_member_search",
                schema: "public",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_visible_in_member_search",
                schema: "public",
                table: "users");
        }
    }
}
