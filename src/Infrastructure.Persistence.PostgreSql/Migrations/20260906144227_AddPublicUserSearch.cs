using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicUserSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;");
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");
        }
    }
}
