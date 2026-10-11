using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishlistSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wishlist_subscriptions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wishlist_subscriptions", x => x.id);
                    table.CheckConstraint("ck_wishlist_subscriptions_secret_hash", "octet_length(share_secret_hash) = 32");
                    table.ForeignKey(
                        name: "fk_wishlist_subscriptions_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wishlist_subscriptions_wishlist_share_links_share_link_id",
                        column: x => x.share_link_id,
                        principalSchema: "public",
                        principalTable: "wishlist_share_links",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_wishlist_subscriptions_wishlists_wishlist_id",
                        column: x => x.wishlist_id,
                        principalSchema: "public",
                        principalTable: "wishlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_subscriptions_share_link_id",
                schema: "public",
                table: "wishlist_subscriptions",
                column: "share_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_wishlist_subscriptions_wishlist_id",
                schema: "public",
                table: "wishlist_subscriptions",
                column: "wishlist_id");

            migrationBuilder.CreateIndex(
                name: "ux_wishlist_subscriptions_member_wishlist",
                schema: "public",
                table: "wishlist_subscriptions",
                columns: new[] { "member_id", "wishlist_id" },
                unique: true);

            // Revocation must also invalidate subscriptions written by concurrent application instances.
            migrationBuilder.Sql("""
                CREATE FUNCTION public.revoke_wishlist_subscriptions_for_share() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.secret_hash IS DISTINCT FROM OLD.secret_hash THEN
                        DELETE FROM public.wishlist_subscriptions WHERE share_link_id = NEW.id;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER revoke_wishlist_subscriptions_for_share
                AFTER UPDATE OF secret_hash ON public.wishlist_share_links
                FOR EACH ROW EXECUTE FUNCTION public.revoke_wishlist_subscriptions_for_share();

                CREATE FUNCTION public.revoke_wishlist_subscriptions_for_state() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.is_archived OR NEW.is_suspended THEN
                        DELETE FROM public.wishlist_subscriptions WHERE wishlist_id = NEW.id;
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER revoke_wishlist_subscriptions_for_state
                AFTER UPDATE OF is_archived, is_suspended ON public.wishlists
                FOR EACH ROW EXECUTE FUNCTION public.revoke_wishlist_subscriptions_for_state();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER revoke_wishlist_subscriptions_for_share ON public.wishlist_share_links;
                DROP FUNCTION public.revoke_wishlist_subscriptions_for_share();
                DROP TRIGGER revoke_wishlist_subscriptions_for_state ON public.wishlists;
                DROP FUNCTION public.revoke_wishlist_subscriptions_for_state();
                """);

            migrationBuilder.DropTable(
                name: "wishlist_subscriptions",
                schema: "public");
        }
    }
}
