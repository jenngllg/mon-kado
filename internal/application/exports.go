package application

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"os"
	"path/filepath"
	"time"
)

const exportColumns = `id::text,status,created_at,snapshot_at,ready_at,expires_at,size_in_bytes,failure`

func (s *Service) scanExport(row pgx.Row) (domain.PersonalDataExportResponse, error) {
	var result domain.PersonalDataExportResponse
	var failure *string
	err := row.Scan(&result.Id, &result.Status, &result.CreatedAt, &result.SnapshotAt, &result.ReadyAt, &result.ExpiresAt, &result.SizeInBytes, &failure)
	result.Status = lowerFirst(result.Status)
	if result.ExpiresAt != nil && !result.ExpiresAt.After(s.Now()) {
		result.Status = "expired"
	}
	if failure != nil {
		code := domain.MemberDataExportGenerationFailed
		if *failure == "TooLarge" {
			code = domain.MemberDataExportTooLarge
		}
		result.ErrorCode = &code
	}
	if errors.Is(err, pgx.ErrNoRows) {
		err = missing(domain.MemberDataExportNotFound)
	}
	return result, err
}

func (s *Service) requestExport(ctx context.Context, tx pgx.Tx, user string) (domain.PersonalDataExportResponse, error) {
	var confirmed bool
	if err := tx.QueryRow(ctx, `SELECT email_confirmed FROM users WHERE id=$1 FOR UPDATE`, user).Scan(&confirmed); err != nil {
		if errors.Is(err, pgx.ErrNoRows) {
			return domain.PersonalDataExportResponse{}, missing(domain.MemberDataExportNotFound)
		}
		return domain.PersonalDataExportResponse{}, err
	}
	if !confirmed {
		return domain.PersonalDataExportResponse{}, unauthorized()
	}
	now := s.Now().UTC()
	if _, err := tx.Exec(ctx, `UPDATE member_data_exports SET status='Expired' WHERE member_id=$1 AND status='Ready' AND expires_at<=$2`, user, now); err != nil {
		return domain.PersonalDataExportResponse{}, err
	}
	active, err := s.scanExport(tx.QueryRow(ctx, `SELECT `+exportColumns+` FROM member_data_exports WHERE member_id=$1 AND status IN ('Queued','Processing','Ready')`, user))
	if err == nil {
		return active, nil
	}
	var business *domain.Error
	if !errors.As(err, &business) {
		return active, err
	}
	var count int
	if err = tx.QueryRow(ctx, `SELECT count(*) FROM member_data_exports WHERE member_id=$1 AND created_at>$2`, user, now.Add(-24*time.Hour)).Scan(&count); err != nil {
		return active, err
	}
	if count >= 3 {
		return active, domain.Failure(429, domain.MemberDataExportRateLimited, "Too many export requests in the last 24 hours.")
	}
	return s.scanExport(tx.QueryRow(ctx, `INSERT INTO member_data_exports(id,member_id,status,created_at,available_at,attempt_count) VALUES($1,$2,'Queued',$3,$3,0) RETURNING `+exportColumns, security.NewID(), user, now))
}

// RequestExport reuses active work under an account lock and audits administrative requests.
func (s *Service) RequestExport(ctx context.Context, user string, p Principal, reference *string) (domain.PersonalDataExportResponse, error) {
	var result domain.PersonalDataExportResponse
	err := s.Store.Transaction(ctx, func(tx pgx.Tx) error {
		if reference != nil {
			if err := s.authorizeAdministratorTx(ctx, tx, p); err != nil {
				return err
			}
		}
		var err error
		result, err = s.requestExport(ctx, tx, user)
		if err != nil {
			return err
		}
		if reference != nil {
			_, err = tx.Exec(ctx, `INSERT INTO administrative_data_export_events(id,administrator_id,member_id,export_id,action,request_reference,created_at) VALUES($1,$2,$3,$4,'Requested',$5,$6)`, security.NewID(), p.MemberID, user, result.Id, *reference, s.Now().UTC())
		}
		return err
	})
	return result, err
}

// Export exposes an owned request or the latest request without archive access material.
func (s *Service) Export(ctx context.Context, user, id string) (domain.PersonalDataExportResponse, error) {
	if id == "" {
		return s.scanExport(s.Store.Pool.QueryRow(ctx, `SELECT `+exportColumns+` FROM member_data_exports WHERE member_id=$1 ORDER BY created_at DESC,id DESC LIMIT 1`, user))
	}
	return s.scanExport(s.Store.Pool.QueryRow(ctx, `SELECT `+exportColumns+` FROM member_data_exports WHERE member_id=$1 AND id=$2`, user, id))
}

// ExportArchive opens a private file only after verifying ownership, publication and expiration.
func (s *Service) ExportArchive(ctx context.Context, user, id string, p Principal, administrative bool) (*os.File, int64, error) {
	result, err := s.Export(ctx, user, id)
	if err != nil {
		return nil, 0, err
	}
	if result.Status == "expired" {
		return nil, 0, missing(domain.MemberDataExportNotFound)
	}
	if result.Status != "ready" {
		return nil, 0, domain.Failure(409, domain.MemberDataExportNotReady, "The archive is not ready.")
	}
	var archive string
	if err = s.Store.Pool.QueryRow(ctx, `SELECT archive_id::text FROM member_data_exports WHERE member_id=$1 AND id=$2`, user, id).Scan(&archive); err != nil {
		return nil, 0, err
	}
	if _, err = uuid.Parse(archive); err != nil {
		return nil, 0, err
	}
	path := filepath.Join(s.Config.ExportPath, archive+".zip")
	info, err := os.Lstat(path)
	if err != nil || !info.Mode().IsRegular() || result.SizeInBytes == nil || info.Size() != *result.SizeInBytes {
		return nil, 0, domain.Failure(503, domain.TechnicalDependencyUnavailable, "The private archive storage is unavailable.")
	}
	file, err := os.Open(path)
	if err != nil {
		return nil, 0, err
	}
	if administrative {
		err = s.Store.Transaction(ctx, func(tx pgx.Tx) error {
			if err := s.authorizeAdministratorTx(ctx, tx, p); err != nil {
				return err
			}
			var reference string
			err := tx.QueryRow(ctx, `SELECT request_reference FROM administrative_data_export_events WHERE member_id=$1 AND export_id=$2 AND action='Requested' ORDER BY created_at DESC LIMIT 1`, user, id).Scan(&reference)
			if errors.Is(err, pgx.ErrNoRows) {
				reference = "Administrative access"
				err = nil
			}
			if err != nil {
				return err
			}
			_, err = tx.Exec(ctx, `INSERT INTO administrative_data_export_events(id,administrator_id,member_id,export_id,action,request_reference,created_at) VALUES($1,$2,$3,$4,'DownloadStarted',$5,$6)`, security.NewID(), p.MemberID, user, id, reference, s.Now().UTC())
			return err
		})
		if err != nil {
			file.Close()
			return nil, 0, err
		}
	}
	return file, info.Size(), nil
}
