package application

import (
	"context"
	"errors"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
)

// GroupSuggestionReservation manages one member's reservation with bounded capacity.
func (s *Service) GroupSuggestionReservation(ctx context.Context, member, group, wishlist, id, method string, quantity int, expected *uint32) (ReservationResult, error) {
	var result ReservationResult
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		share, err := s.suggestionAccess(ctx, tx, member, group, wishlist)
		if err != nil {
			return err
		}
		var wanted int
		var name string
		err = tx.QueryRow(ctx, `SELECT quantity,name FROM group_suggestions WHERE group_id=$1 AND wishlist_id=$2 AND id=$3 FOR UPDATE`, group, wishlist, id).Scan(&wanted, &name)
		if errors.Is(err, pgx.ErrNoRows) {
			return missing(domain.GroupSuggestionNotFound)
		}
		if err != nil {
			return err
		}
		reservation := &result.Reservation
		err = tx.QueryRow(ctx, `SELECT id::text,suggestion_id::text,quantity,created_at,updated_at,xmin::text::bigint FROM group_suggestion_reservations WHERE suggestion_id=$1 AND member_id=$2 FOR UPDATE`, id, member).Scan(&reservation.Id, &reservation.WishId, &reservation.Quantity, &reservation.CreatedAt, &reservation.UpdatedAt, &result.Version)
		exists := err == nil
		if err != nil && !errors.Is(err, pgx.ErrNoRows) {
			return err
		}
		if method != "PUT" && !exists {
			return missing(domain.GiftReservationNotFound)
		}
		if method == "GET" {
			return nil
		}
		if exists && expected == nil {
			return domain.Failure(428, domain.RequestPreconditionRequired, "The If-Match header is required.")
		}
		if expected != nil && (!exists || *expected != result.Version) {
			return stale(domain.GiftReservationVersionConflict)
		}
		now := s.Now().UTC()
		if method == "DELETE" {
			if _, err = tx.Exec(ctx, `UPDATE gift_reservation_histories SET status='Cancelled',ended_at=$2,last_activity_at=$2 WHERE id=$1`, reservation.Id, now); err != nil {
				return err
			}
			_, err = tx.Exec(ctx, `DELETE FROM group_suggestion_reservations WHERE id=$1`, reservation.Id)
			return err
		}
		var other int
		if err = tx.QueryRow(ctx, `SELECT COALESCE(sum(quantity),0) FROM group_suggestion_reservations WHERE suggestion_id=$1 AND member_id<>$2`, id, member).Scan(&other); err != nil {
			return err
		}
		if quantity+other > wanted {
			return domain.Failure(409, domain.GiftReservationQuantityUnavailable, "The requested quantity is no longer available.")
		}
		if !exists {
			reservation.Id = security.NewID()
			err = tx.QueryRow(ctx, `INSERT INTO group_suggestion_reservations(id,suggestion_id,member_id,quantity,created_at) VALUES($1,$2,$3,$4,$5) RETURNING suggestion_id::text,quantity,created_at,updated_at,xmin::text::bigint`, reservation.Id, id, member, quantity, now).Scan(&reservation.WishId, &reservation.Quantity, &reservation.CreatedAt, &reservation.UpdatedAt, &result.Version)
			result.Created = true
		} else {
			err = tx.QueryRow(ctx, `UPDATE group_suggestion_reservations SET quantity=$2,updated_at=$3 WHERE id=$1 RETURNING suggestion_id::text,quantity,created_at,updated_at,xmin::text::bigint`, reservation.Id, quantity, now).Scan(&reservation.WishId, &reservation.Quantity, &reservation.CreatedAt, &reservation.UpdatedAt, &result.Version)
		}
		if err != nil {
			return err
		}
		_, err = tx.Exec(ctx, `INSERT INTO gift_reservation_histories(id,member_id,wishlist_id,wishlist_name,wish_id,wish_name,quantity,status,created_at,last_activity_at,is_group_reservation,author_id,author_display_name,suggestion_group_id) SELECT $1,$2,$3,$4,$5,$6,$7,'Active',$8,$9,true,u.id,u.display_name,$10 FROM users u WHERE u.id=$11 ON CONFLICT(id) DO UPDATE SET quantity=EXCLUDED.quantity,last_activity_at=EXCLUDED.last_activity_at`, reservation.Id, member, wishlist, share.Name, id, name, quantity, reservation.CreatedAt, now, group, share.Owner)
		return err
	})
	return result, err
}
