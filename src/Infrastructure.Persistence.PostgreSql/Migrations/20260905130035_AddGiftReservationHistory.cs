using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftReservationHistory : Migration
    {
        private static readonly string[] _memberActivityColumns =
        [
            "member_id",
            "last_activity_at",
            "id"
        ];
        private static readonly bool[] _memberActivityDescending =
        [
            false,
            true,
            true
        ];
        private static readonly string[] _memberStatusActivityColumns =
        [
            "member_id",
            "status",
            "last_activity_at",
            "id"
        ];
        private static readonly bool[] _memberStatusActivityDescending =
        [
            false,
            false,
            true,
            true
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gift_reservation_histories",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wishlist_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    wish_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wish_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gift_reservation_histories", x => x.id);
                    table.CheckConstraint("ck_gift_reservation_histories_lifecycle_consistent", "last_activity_at >= created_at AND ((status = 'Active' AND ended_at IS NULL) OR (status IN ('Cancelled', 'Unavailable') AND ended_at = last_activity_at))");
                    table.CheckConstraint("ck_gift_reservation_histories_quantity_valid", "quantity BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_gift_reservation_histories_status_valid", "status IN ('Active', 'Cancelled', 'Unavailable')");
                    table.CheckConstraint("ck_gift_reservation_histories_wish_name_valid", "char_length(btrim(wish_name)) > 0 AND wish_name !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_gift_reservation_histories_wishlist_name_valid", "char_length(btrim(wishlist_name)) > 0 AND wishlist_name !~ '[[:cntrl:]]'");
                    table.ForeignKey(
                        name: "fk_gift_reservation_histories_users_member_id",
                        column: x => x.member_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gift_reservation_histories_member_activity",
                schema: "public",
                table: "gift_reservation_histories",
                columns: _memberActivityColumns,
                descending: _memberActivityDescending);

            migrationBuilder.CreateIndex(
                name: "ix_gift_reservation_histories_member_status_activity",
                schema: "public",
                table: "gift_reservation_histories",
                columns: _memberStatusActivityColumns,
                descending: _memberStatusActivityDescending);

            migrationBuilder.Sql(
                """
                INSERT INTO public.gift_reservation_histories (
                    id,
                    member_id,
                    wishlist_id,
                    wishlist_name,
                    wish_id,
                    wish_name,
                    quantity,
                    status,
                    created_at,
                    last_activity_at,
                    ended_at)
                SELECT
                    reservation.id,
                    participant.member_id,
                    reservation.wishlist_id,
                    wishlist.name,
                    reservation.wish_id,
                    wish.name,
                    reservation.quantity,
                    'Active',
                    reservation.created_at,
                    COALESCE(reservation.updated_at, reservation.created_at),
                    NULL
                FROM public.gift_reservations AS reservation
                INNER JOIN public.wishlist_participants AS participant
                    ON participant.id = reservation.wishlist_participant_id
                INNER JOIN public.wishlists AS wishlist
                    ON wishlist.id = reservation.wishlist_id
                INNER JOIN public.wishes AS wish
                    ON wish.id = reservation.wish_id
                    AND wish.wishlist_id = reservation.wishlist_id
                WHERE participant.member_id IS NOT NULL;

                COMMENT ON TABLE public.gift_reservation_histories IS
                    'Member reservation lifecycles retained until the owning member account is deleted.';

                CREATE FUNCTION public.mark_gift_reservation_histories_unavailable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_TABLE_NAME = 'wishes' THEN
                        UPDATE public.gift_reservation_histories
                        SET status = 'Unavailable',
                            last_activity_at = GREATEST(statement_timestamp(), created_at),
                            ended_at = GREATEST(statement_timestamp(), created_at)
                        WHERE wish_id = OLD.id
                            AND wishlist_id = OLD.wishlist_id
                            AND status = 'Active';
                    ELSE
                        UPDATE public.gift_reservation_histories
                        SET status = 'Unavailable',
                            last_activity_at = GREATEST(statement_timestamp(), created_at),
                            ended_at = GREATEST(statement_timestamp(), created_at)
                        WHERE wishlist_id = OLD.id
                            AND status = 'Active';
                    END IF;

                    RETURN OLD;
                END;
                $function$;

                CREATE TRIGGER tr_wishes_mark_reservation_history_unavailable
                BEFORE DELETE ON public.wishes
                FOR EACH ROW
                EXECUTE FUNCTION public.mark_gift_reservation_histories_unavailable();

                CREATE TRIGGER tr_wishlists_mark_reservation_history_unavailable
                BEFORE DELETE ON public.wishlists
                FOR EACH ROW
                EXECUTE FUNCTION public.mark_gift_reservation_histories_unavailable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_wishes_mark_reservation_history_unavailable
                    ON public.wishes;
                DROP TRIGGER IF EXISTS tr_wishlists_mark_reservation_history_unavailable
                    ON public.wishlists;
                DROP FUNCTION IF EXISTS public.mark_gift_reservation_histories_unavailable();
                """);

            migrationBuilder.DropTable(
                name: "gift_reservation_histories",
                schema: "public");
        }
    }
}
