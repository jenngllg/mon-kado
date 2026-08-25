using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddWishCollectionOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_wish_position_sequences_next_position_valid",
                schema: "public",
                table: "wish_position_sequences");

            migrationBuilder.AddColumn<int>(
                name: "current_count",
                schema: "public",
                table: "wish_position_sequences",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                INSERT INTO public.wish_position_sequences (
                    wishlist_id,
                    next_position,
                    current_count)
                SELECT
                    wishlist.id,
                    COALESCE(MAX(wish.position), 0),
                    COUNT(wish.id)::integer
                FROM public.wishlists AS wishlist
                LEFT JOIN public.wishes AS wish
                    ON wish.wishlist_id = wishlist.id
                GROUP BY wishlist.id
                ON CONFLICT (wishlist_id)
                DO UPDATE SET current_count = EXCLUDED.current_count;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_wish_position_sequences_current_count_valid",
                schema: "public",
                table: "wish_position_sequences",
                sql: "current_count >= 0 AND current_count <= 1000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wish_position_sequences_next_position_valid",
                schema: "public",
                table: "wish_position_sequences",
                sql: "next_position >= 0");

            migrationBuilder.Sql(
                """
                CREATE FUNCTION public.initialize_wish_position_sequence()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    INSERT INTO public.wish_position_sequences (
                        wishlist_id,
                        next_position,
                        current_count)
                    VALUES (
                        NEW.id,
                        0,
                        0);

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER initialize_wish_position_sequence
                AFTER INSERT ON public.wishlists
                FOR EACH ROW
                EXECUTE FUNCTION public.initialize_wish_position_sequence();
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION public.track_wish_collection_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        UPDATE public.wish_position_sequences
                        SET current_count = current_count + 1
                        WHERE wishlist_id = NEW.wishlist_id
                            AND current_count < 1000;

                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'Wishlist wish limit reached.'
                                USING ERRCODE = '23514',
                                    CONSTRAINT = 'ck_wish_position_sequences_current_count_limit';
                        END IF;

                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        UPDATE public.wish_position_sequences
                        SET current_count = current_count - 1
                        WHERE wishlist_id = OLD.wishlist_id;

                        RETURN OLD;
                    END IF;

                    UPDATE public.wish_position_sequences
                    SET current_count = current_count
                    WHERE wishlist_id = NEW.wishlist_id;

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER track_wish_collection_change
                AFTER INSERT OR UPDATE OR DELETE ON public.wishes
                FOR EACH ROW
                EXECUTE FUNCTION public.track_wish_collection_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER track_wish_collection_change ON public.wishes;
                DROP FUNCTION public.track_wish_collection_change();
                DROP TRIGGER initialize_wish_position_sequence ON public.wishlists;
                DROP FUNCTION public.initialize_wish_position_sequence();

                DELETE FROM public.wish_position_sequences AS sequence
                WHERE sequence.next_position = 0
                    AND NOT EXISTS (
                        SELECT 1
                        FROM public.wishes AS wish
                        WHERE wish.wishlist_id = sequence.wishlist_id);
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_wish_position_sequences_current_count_valid",
                schema: "public",
                table: "wish_position_sequences");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wish_position_sequences_next_position_valid",
                schema: "public",
                table: "wish_position_sequences");

            migrationBuilder.DropColumn(
                name: "current_count",
                schema: "public",
                table: "wish_position_sequences");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wish_position_sequences_next_position_valid",
                schema: "public",
                table: "wish_position_sequences",
                sql: "next_position > 0");
        }
    }
}
