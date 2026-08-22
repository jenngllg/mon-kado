using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberRole : Migration
    {
        private static readonly string[] _roleColumns =
        [
            "id",
            "concurrency_stamp",
            "name",
            "normalized_name"
        ];

        private static readonly object[] _roleValues =
        [
            new Guid("0198d027-51c0-7000-8000-000000000002"),
            "0198d027-51c0-7000-8000-000000000003",
            "Member",
            "MEMBER"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "roles",
                columns: _roleColumns,
                values: _roleValues);

            migrationBuilder.Sql(
                """
                INSERT INTO public.user_roles (user_id, role_id)
                SELECT id, '0198d027-51c0-7000-8000-000000000002'::uuid
                FROM public.users
                ON CONFLICT (user_id, role_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM public.user_roles
                WHERE role_id = '0198d027-51c0-7000-8000-000000000002'::uuid;
                """);

            migrationBuilder.DeleteData(
                schema: "public",
                table: "roles",
                keyColumn: "id",
                keyValue: new Guid("0198d027-51c0-7000-8000-000000000002"));
        }
    }
}
