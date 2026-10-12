package application

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/jackc/pgx/v5"
	"strings"
	"time"
)

// ReportedWishlistSummary contains administrative summary data for matching reports.
type ReportedWishlistSummary struct {
	WishlistId       string    `json:"wishlistId"`
	Name             string    `json:"name"`
	OwnerId          string    `json:"ownerId"`
	OwnerDisplayName string    `json:"ownerDisplayName"`
	IsSuspended      bool      `json:"isSuspended"`
	ReportCount      int       `json:"reportCount"`
	LastReportedAt   time.Time `json:"lastReportedAt"`
}

// ReportedWishlists filters reports before aggregating their owning lists.
func (s *Service) ReportedWishlists(ctx context.Context, reason, status string, suspended *bool, page, size int) (domain.PaginatedResponse[ReportedWishlistSummary], error) {
	items := []ReportedWishlistSummary{}
	filter := ` FROM wishlist_reports r JOIN wishlists l ON l.id=r.wishlist_id JOIN users u ON u.id=l.owner_id WHERE ($1='' OR r.reason=$1) AND ($2='' OR r.status=$2) AND ($3::boolean IS NULL OR l.is_suspended=$3)`
	var total int
	err := s.Store.Pool.QueryRow(ctx, `SELECT count(DISTINCT l.id)`+filter, upperFirst(reason), upperFirst(status), suspended).Scan(&total)
	if err != nil || int64(page) > max(1, (int64(total)+int64(size)-1)/int64(size)) {
		return domain.Page(items, page, size, total), err
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT l.id::text,l.name,l.owner_id::text,u.display_name,l.is_suspended,count(*),max(r.created_at)`+filter+` GROUP BY l.id,u.display_name ORDER BY max(r.created_at) DESC,l.id LIMIT $4 OFFSET $5`, upperFirst(reason), upperFirst(status), suspended, size, (int64(page)-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		var item ReportedWishlistSummary
		if err = rows.Scan(&item.WishlistId, &item.Name, &item.OwnerId, &item.OwnerDisplayName, &item.IsSuspended, &item.ReportCount, &item.LastReportedAt); err != nil {
			return domain.Page(items, page, size, total), err
		}
		items = append(items, item)
	}
	return domain.Page(items, page, size, total), rows.Err()
}

func (s *Service) reportedExists(ctx context.Context, list string) error {
	var found bool
	if err := s.Store.Pool.QueryRow(ctx, `SELECT EXISTS(SELECT 1 FROM wishlist_reports WHERE wishlist_id=$1)`, list).Scan(&found); err != nil {
		return err
	}
	if !found {
		return missing(domain.WishlistNotFound)
	}
	return nil
}

// ReportedWishlist returns the current moderated contents only for a reported list.
func (s *Service) ReportedWishlist(ctx context.Context, list string) (domain.ReportedWishlistResponse, error) {
	var result domain.ReportedWishlistResponse
	result.Wishes = []domain.ReportedWishResponse{}
	if err := s.reportedExists(ctx, list); err != nil {
		return result, err
	}
	err := s.Store.Pool.QueryRow(ctx, `SELECT l.id::text,l.name,l.owner_id::text,u.display_name,l.occasion,l.event_date::text,l.message,l.created_at,l.updated_at,l.is_suspended,l.suspension_reason,l.suspended_at FROM wishlists l JOIN users u ON u.id=l.owner_id WHERE l.id=$1`, list).Scan(&result.WishlistId, &result.Name, &result.OwnerId, &result.OwnerDisplayName, &result.Occasion, &result.EventDate, &result.Message, &result.CreatedAt, &result.UpdatedAt, &result.IsSuspended, &result.SuspensionReason, &result.SuspendedAt)
	if err != nil {
		return result, err
	}
	result.Occasion = lowerFirst(result.Occasion)
	rows, err := s.Store.Pool.Query(ctx, `SELECT `+wishColumns+` FROM wishes WHERE wishlist_id=$1 ORDER BY position,id`, list)
	if err != nil {
		return result, err
	}
	defer rows.Close()
	for rows.Next() {
		wish, _, err := s.scanWish(result.OwnerId, rows)
		if err != nil {
			return result, err
		}
		if wish.ImageUrl != nil {
			*wish.ImageUrl = strings.Replace(*wish.ImageUrl, "/api/v1/wishlists/", "/api/v1/admin/reported-wishlists/", 1)
		}
		result.Wishes = append(result.Wishes, domain.ReportedWishResponse{Id: wish.Id, Name: wish.Name, Note: wish.Note, Color: wish.Color, Size: wish.Size, Url: wish.Url, Price: wish.Price, Quantity: wish.Quantity, Position: wish.Position, CreatedAt: wish.CreatedAt, UpdatedAt: wish.UpdatedAt, ImageUrl: wish.ImageUrl})
	}
	return result, rows.Err()
}

const reportColumns = `id::text,reason,details,created_at,status,review_note,reviewed_at,reviewed_by_administrator_id::text,xmin::text::bigint`

func scanReport(row pgx.Row) (domain.WishlistReportDetails, uint32, error) {
	var r domain.WishlistReportDetails
	var version uint32
	err := row.Scan(&r.Id, &r.Reason, &r.Details, &r.CreatedAt, &r.Status, &r.ReviewNote, &r.ReviewedAt, &r.ReviewedByAdministratorId, &version)
	r.Reason = lowerFirst(r.Reason)
	r.Status = lowerFirst(r.Status)
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.WishlistReportNotFound)
	}
	return r, version, err
}

// Reports returns matching reports using stable chronological pagination.
func (s *Service) Reports(ctx context.Context, list, reason, status string, page, size int) (domain.PaginatedResponse[domain.WishlistReportDetails], error) {
	items := []domain.WishlistReportDetails{}
	if err := s.reportedExists(ctx, list); err != nil {
		return domain.Page(items, page, size, 0), err
	}
	var total int
	filter := ` FROM wishlist_reports WHERE wishlist_id=$1 AND ($2='' OR reason=$2) AND ($3='' OR status=$3)`
	err := s.Store.Pool.QueryRow(ctx, `SELECT count(*)`+filter, list, upperFirst(reason), upperFirst(status)).Scan(&total)
	if err != nil || int64(page) > max(1, (int64(total)+int64(size)-1)/int64(size)) {
		return domain.Page(items, page, size, total), err
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT `+reportColumns+filter+` ORDER BY created_at DESC,id LIMIT $4 OFFSET $5`, list, upperFirst(reason), upperFirst(status), size, (int64(page)-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		item, _, err := scanReport(rows)
		if err != nil {
			return domain.Page(items, page, size, total), err
		}
		items = append(items, item)
	}
	return domain.Page(items, page, size, total), rows.Err()
}

// Report reads a resource with both its parent and identifier.
func (s *Service) Report(ctx context.Context, list, id string) (domain.WishlistReportDetails, uint32, error) {
	return scanReport(s.Store.Pool.QueryRow(ctx, `SELECT `+reportColumns+` FROM wishlist_reports WHERE wishlist_id=$1 AND id=$2`, list, id))
}

// ReviewReport records each transition atomically with its optimistic update.
func (s *Service) ReviewReport(ctx context.Context, p Principal, list, id string, input domain.UpdateWishlistReportReviewRequest, expected uint32) (domain.WishlistReportDetails, uint32, error) {
	var result domain.WishlistReportDetails
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, p); err != nil {
			return err
		}
		previous, current, err := scanReport(tx.QueryRow(ctx, `SELECT `+reportColumns+` FROM wishlist_reports WHERE wishlist_id=$1 AND id=$2 FOR UPDATE`, list, id))
		if err != nil {
			return err
		}
		if current != expected {
			return stale(domain.WishlistReportVersionConflict)
		}
		status := upperFirst(text(input.Status))
		note := optional(input.ReviewNote)
		now := s.Now().UTC()
		if _, err = tx.Exec(ctx, `INSERT INTO wishlist_report_review_events(id,report_id,sequence,previous_status,status,note,administrator_id,occurred_at) SELECT $1,$2,coalesce(max(sequence),0)+1,$3,$4,$5,$6,$7 FROM wishlist_report_review_events WHERE report_id=$2`, security.NewID(), id, upperFirst(previous.Status), status, note, p.MemberID, now); err != nil {
			return err
		}
		result, version, err = scanReport(tx.QueryRow(ctx, `UPDATE wishlist_reports SET status=$3,review_note=$4,reviewed_at=$5,reviewed_by_administrator_id=$6,updated_at=$5 WHERE wishlist_id=$1 AND id=$2 RETURNING `+reportColumns, list, id, status, note, now, p.MemberID))
		return err
	})
	return result, version, err
}

// ReportReviewEvents reads the immutable review history of a report.
func (s *Service) ReportReviewEvents(ctx context.Context, list, id string, page, size int) (domain.PaginatedResponse[domain.WishlistReportReviewEventDetails], error) {
	items := []domain.WishlistReportReviewEventDetails{}
	if _, _, err := s.Report(ctx, list, id); err != nil {
		return domain.Page(items, page, size, 0), err
	}
	var total int
	if err := s.Store.Pool.QueryRow(ctx, `SELECT count(*) FROM wishlist_report_review_events WHERE report_id=$1`, id).Scan(&total); err != nil {
		return domain.Page(items, page, size, total), err
	}
	if int64(page) > max(1, (int64(total)+int64(size)-1)/int64(size)) {
		return domain.Page(items, page, size, total), nil
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT id::text,previous_status,status,note,administrator_id::text,occurred_at FROM wishlist_report_review_events WHERE report_id=$1 ORDER BY sequence DESC LIMIT $2 OFFSET $3`, id, size, (int64(page)-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		var item domain.WishlistReportReviewEventDetails
		if err = rows.Scan(&item.Id, &item.PreviousStatus, &item.Status, &item.Note, &item.AdministratorId, &item.OccurredAt); err != nil {
			return domain.Page(items, page, size, total), err
		}
		item.PreviousStatus = lowerFirst(item.PreviousStatus)
		item.Status = lowerFirst(item.Status)
		items = append(items, item)
	}
	return domain.Page(items, page, size, total), rows.Err()
}

// Moderation returns the administrative state and its list concurrency version.
func (s *Service) Moderation(ctx context.Context, list string) (domain.WishlistModerationResponse, uint32, error) {
	var result domain.WishlistModerationResponse
	var version uint32
	err := s.Store.Pool.QueryRow(ctx, `SELECT id::text,is_suspended,suspension_reason,suspended_at,xmin::text::bigint FROM wishlists WHERE id=$1`, list).Scan(&result.WishlistId, &result.IsSuspended, &result.SuspensionReason, &result.SuspendedAt, &version)
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.WishlistNotFound)
	}
	return result, version, err
}

// UpdateModeration stages a state transition and its notification in the same transaction.
func (s *Service) UpdateModeration(ctx context.Context, p Principal, list string, input domain.UpdateWishlistModerationRequest, expected uint32) (domain.WishlistModerationResponse, uint32, error) {
	var result domain.WishlistModerationResponse
	var version uint32
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if err := s.authorizeAdministratorTx(ctx, tx, p); err != nil {
			return err
		}
		var current uint32
		var suspended bool
		var reason *string
		if err := tx.QueryRow(ctx, `SELECT is_suspended,suspension_reason,xmin::text::bigint FROM wishlists WHERE id=$1 FOR UPDATE`, list).Scan(&suspended, &reason, &current); err != nil {
			if errors.Is(err, pgx.ErrNoRows) {
				return missing(domain.WishlistNotFound)
			}
			return err
		}
		if current != expected {
			return stale(domain.WishlistVersionConflict)
		}
		nextReason := optional(input.Reason)
		action := "Suspended"
		if !*input.IsSuspended {
			action = "Reactivated"
			nextReason = nil
		} else if suspended {
			action = "ReasonUpdated"
		}
		if suspended == *input.IsSuspended && text(reason) == text(nextReason) {
			result = domain.WishlistModerationResponse{WishlistId: list, IsSuspended: suspended, SuspensionReason: reason}
			return tx.QueryRow(ctx, `SELECT suspended_at,xmin::text::bigint FROM wishlists WHERE id=$1`, list).Scan(&result.SuspendedAt, &version)
		}
		now, id := s.Now().UTC(), security.NewID()
		var at *time.Time
		if *input.IsSuspended {
			at = &now
			if suspended {
				if err := tx.QueryRow(ctx, `SELECT suspended_at FROM wishlists WHERE id=$1`, list).Scan(&at); err != nil {
					return err
				}
			}
		}
		if _, err := tx.Exec(ctx, `INSERT INTO wishlist_moderation_events(id,wishlist_id,administrator_id,sequence,action,reason,occurred_at) SELECT $1,$2,$3,coalesce(max(sequence),0)+1,$4,$5,$6 FROM wishlist_moderation_events WHERE wishlist_id=$2`, id, list, p.MemberID, action, nextReason, now); err != nil {
			return err
		}
		if _, err := tx.Exec(ctx, `INSERT INTO wishlist_moderation_email_outbox(id,created_at,available_at,attempt_count) VALUES($1,$2,$2,0)`, id, now); err != nil {
			return err
		}
		return tx.QueryRow(ctx, `UPDATE wishlists SET is_suspended=$2,suspension_reason=$3,suspended_at=$4,updated_at=$5 WHERE id=$1 RETURNING id::text,is_suspended,suspension_reason,suspended_at,xmin::text::bigint`, list, input.IsSuspended, nextReason, at, now).Scan(&result.WishlistId, &result.IsSuspended, &result.SuspensionReason, &result.SuspendedAt, &version)
	})
	return result, version, err
}

// ModerationEvents returns the list's ordered suspension history.
func (s *Service) ModerationEvents(ctx context.Context, list string, page, size int) (domain.PaginatedResponse[domain.WishlistModerationEventResponse], error) {
	items := []domain.WishlistModerationEventResponse{}
	if _, _, err := s.Moderation(ctx, list); err != nil {
		return domain.Page(items, page, size, 0), err
	}
	var total int
	if err := s.Store.Pool.QueryRow(ctx, `SELECT count(*) FROM wishlist_moderation_events WHERE wishlist_id=$1`, list).Scan(&total); err != nil {
		return domain.Page(items, page, size, total), err
	}
	if int64(page) > max(1, (int64(total)+int64(size)-1)/int64(size)) {
		return domain.Page(items, page, size, total), nil
	}
	rows, err := s.Store.Pool.Query(ctx, `SELECT id::text,administrator_id::text,action,reason,occurred_at FROM wishlist_moderation_events WHERE wishlist_id=$1 ORDER BY sequence DESC LIMIT $2 OFFSET $3`, list, size, (int64(page)-1)*int64(size))
	if err != nil {
		return domain.Page(items, page, size, total), err
	}
	defer rows.Close()
	for rows.Next() {
		var item domain.WishlistModerationEventResponse
		if err = rows.Scan(&item.Id, &item.AdministratorId, &item.Action, &item.Reason, &item.OccurredAt); err != nil {
			return domain.Page(items, page, size, total), err
		}
		item.Action = lowerFirst(item.Action)
		items = append(items, item)
	}
	return domain.Page(items, page, size, total), rows.Err()
}
