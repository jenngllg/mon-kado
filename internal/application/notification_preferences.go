package application

import (
	"context"
	"errors"
	"slices"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/jackc/pgx/v5"
)

// NotificationPreferences reads member-private settings without creating data on a GET.
// The virtual default resource uses version zero until its first explicit save.
func (s *Service) NotificationPreferences(ctx context.Context, member string) (domain.NotificationPreferencesResponse, uint32, error) {
	var disabled []string
	var version uint32
	err := s.Store.Pool.QueryRow(ctx, `SELECT disabled_kinds,xmin::text::bigint FROM member_notification_preferences WHERE member_id=$1`, member).Scan(&disabled, &version)
	if errors.Is(err, pgx.ErrNoRows) {
		err = nil
	}
	return notificationPreferences(disabled), version, err
}

// SaveNotificationPreferences atomically saves a complete, validated preference set.
// Conditional insertion also protects two simultaneous first saves of the virtual resource.
func (s *Service) SaveNotificationPreferences(ctx context.Context, member string, input domain.UpdateNotificationPreferencesRequest, expected uint32) (domain.NotificationPreferencesResponse, uint32, error) {
	disabled := []string{}
	for _, choice := range []struct {
		kind    string
		enabled *bool
	}{
		{"wishesAdded", input.WishesAdded}, {"groupJoined", input.GroupJoined},
		{"groupInvitation", input.GroupInvitation}, {"occasionReminder", input.OccasionReminder},
		{"sharingReminder", input.SharingReminder}, {"groupWishlistShared", input.GroupWishlistShared},
		{"reservationUnavailable", input.ReservationUnavailable}, {"wishReserved", input.WishReserved},
		{"suggestionAdded", input.SuggestionAdded},
	} {
		if !*choice.enabled {
			disabled = append(disabled, choice.kind)
		}
	}
	var version uint32
	var err error
	if expected == 0 {
		err = s.Store.Pool.QueryRow(ctx, `INSERT INTO member_notification_preferences(member_id,disabled_kinds,created_at) VALUES($1,$2,$3) ON CONFLICT(member_id) DO NOTHING RETURNING xmin::text::bigint`, member, disabled, s.Now().UTC()).Scan(&version)
	} else {
		err = s.Store.Pool.QueryRow(ctx, `UPDATE member_notification_preferences SET disabled_kinds=$2,updated_at=$3 WHERE member_id=$1 AND xmin::text::bigint=$4 RETURNING xmin::text::bigint`, member, disabled, s.Now().UTC(), expected).Scan(&version)
	}
	if errors.Is(err, pgx.ErrNoRows) {
		err = stale(domain.NotificationPreferencesVersionConflict)
	}
	return notificationPreferences(disabled), version, err
}

func notificationPreferences(disabled []string) domain.NotificationPreferencesResponse {
	return domain.NotificationPreferencesResponse{
		WishesAdded: !slices.Contains(disabled, "wishesAdded"), GroupJoined: !slices.Contains(disabled, "groupJoined"),
		GroupInvitation: !slices.Contains(disabled, "groupInvitation"), OccasionReminder: !slices.Contains(disabled, "occasionReminder"),
		SharingReminder: !slices.Contains(disabled, "sharingReminder"), GroupWishlistShared: !slices.Contains(disabled, "groupWishlistShared"),
		ReservationUnavailable: !slices.Contains(disabled, "reservationUnavailable"), WishReserved: !slices.Contains(disabled, "wishReserved"),
		SuggestionAdded: !slices.Contains(disabled, "suggestionAdded"),
	}
}
