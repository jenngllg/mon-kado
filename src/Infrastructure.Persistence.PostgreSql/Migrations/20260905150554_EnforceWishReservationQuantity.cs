using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class EnforceWishReservationQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE public.wishes AS wish
                SET quantity = totals.reserved_quantity
                FROM (
                    SELECT
                        reservation.wishlist_id,
                        reservation.wish_id,
                        SUM(reservation.quantity)::integer AS reserved_quantity
                    FROM public.gift_reservations AS reservation
                    GROUP BY
                        reservation.wishlist_id,
                        reservation.wish_id
                ) AS totals
                WHERE wish.wishlist_id = totals.wishlist_id
                    AND wish.id = totals.wish_id
                    AND wish.quantity < totals.reserved_quantity;

                CREATE FUNCTION public.enforce_wish_quantity_not_below_reserved()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    reserved_quantity integer;
                BEGIN
                    SELECT COALESCE(SUM(reservation.quantity), 0)::integer
                    INTO reserved_quantity
                    FROM public.gift_reservations AS reservation
                    WHERE reservation.wishlist_id = NEW.wishlist_id
                        AND reservation.wish_id = NEW.id;

                    IF NEW.quantity < reserved_quantity THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            CONSTRAINT = 'ck_wishes_quantity_not_below_reserved',
                            MESSAGE = 'Wish quantity cannot be lower than its reserved quantity.';
                    END IF;

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER tr_wishes_enforce_quantity_not_below_reserved
                BEFORE UPDATE OF quantity ON public.wishes
                FOR EACH ROW
                WHEN (NEW.quantity IS DISTINCT FROM OLD.quantity)
                EXECUTE FUNCTION public.enforce_wish_quantity_not_below_reserved();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_wishes_enforce_quantity_not_below_reserved
                    ON public.wishes;
                DROP FUNCTION IF EXISTS public.enforce_wish_quantity_not_below_reserved();
                """);
        }
    }
}
