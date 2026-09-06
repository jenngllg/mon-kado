using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "profile_image_hash",
                schema: "public",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "profile_image_id",
                schema: "public",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_profile_image_id",
                schema: "public",
                table: "users",
                column: "profile_image_id",
                unique: true,
                filter: "profile_image_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_profile_image_consistent",
                schema: "public",
                table: "users",
                sql: "(profile_image_id IS NULL AND profile_image_hash IS NULL) OR (profile_image_id IS NOT NULL AND profile_image_hash IS NOT NULL AND octet_length(profile_image_hash) = 32)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_users_profile_image_id",
                schema: "public",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_profile_image_consistent",
                schema: "public",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_image_hash",
                schema: "public",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_image_id",
                schema: "public",
                table: "users");
        }
    }
}
