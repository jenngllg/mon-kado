package worker

import (
	"context"
	"errors"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/telemetry"
	"github.com/jackc/pgx/v5"
	"os"
	"path/filepath"
	"time"
)

func (w *Worker) generateExports(ctx context.Context) error {
	for i := 0; i < w.Service.Config.Jobs().ExportBatchSize; i++ {
		now, lease := w.Service.Now().UTC(), security.NewID()
		var id, user string
		var attempts int
		err := w.Service.Store.Pool.QueryRow(ctx, `UPDATE member_data_exports SET status='Processing',lease_id=$2,locked_until=$3,attempt_count=attempt_count+1 WHERE id=(SELECT id FROM member_data_exports WHERE member_id IS NOT NULL AND attempt_count<$4 AND available_at<=$1 AND (status='Queued' OR status='Processing' AND locked_until<=$1) ORDER BY created_at,id FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id::text,member_id::text,attempt_count`, now, lease, now.Add(w.Service.Config.Jobs().LeaseDuration), w.Service.Config.Jobs().ExportMaximumAttempts).Scan(&id, &user, &attempts)
		if errors.Is(err, pgx.ErrNoRows) {
			break
		}
		if err != nil {
			return err
		}
		attempt, cancel := context.WithTimeout(ctx, w.Service.Config.Jobs().ExportTimeout)
		done := make(chan error, 1)
		go func() {
			ticker := time.NewTicker(w.Service.Config.Jobs().RenewalInterval)
			defer ticker.Stop()
			for {
				select {
				case <-attempt.Done():
					done <- nil
					return
				case <-ticker.C:
					command, err := w.Service.Store.Pool.Exec(attempt, `UPDATE member_data_exports SET locked_until=$3 WHERE id=$1 AND lease_id=$2 AND status='Processing' AND member_id=$4`, id, lease, w.Service.Now().UTC().Add(w.Service.Config.Jobs().LeaseDuration), user)
					if err != nil || command.RowsAffected() != 1 {
						cancel()
						if err == nil {
							err = errors.New("export lease lost")
						}
						done <- err
						return
					}
				}
			}
		}()
		archiveID := security.NewID()
		snapshot, size, generationErr := w.Service.GenerateExport(attempt, user, archiveID)
		cancel()
		if renewalErr := <-done; renewalErr != nil {
			generationErr = renewalErr
		}
		if ctx.Err() != nil {
			os.Remove(filepath.Join(w.Service.Config.ExportPath, archiveID+".zip"))
			return ctx.Err()
		}
		if generationErr == nil {
			published := false
			err = w.Service.Store.Transaction(ctx, func(tx pgx.Tx) error {
				ready := w.Service.Now().UTC()
				command, err := tx.Exec(ctx, `UPDATE member_data_exports SET status='Ready',archive_id=$3,snapshot_at=$4,ready_at=$5,expires_at=$6,size_in_bytes=$7,lease_id=NULL,locked_until=NULL WHERE id=$1 AND lease_id=$2 AND member_id=$8 AND status='Processing'`, id, lease, archiveID, snapshot, ready, ready.Add(w.Service.Config.Jobs().ExportLifetime), size, user)
				if err != nil {
					return err
				}
				if command.RowsAffected() == 0 {
					return nil
				}
				_, err = tx.Exec(ctx, `INSERT INTO authentication_email_outbox(id,user_id,kind,created_at,available_at,attempt_count,member_data_export_id) VALUES($1,$2,'PERSONAL_DATA_EXPORT_READY',$3,$3,0,$4)`, security.NewID(), user, ready, id)
				published = err == nil
				return err
			})
			if err != nil || !published {
				var referenced bool
				verification, verificationErr := w.Service.Store.Pool.Query(ctx, `SELECT 1 FROM member_data_exports WHERE id=$1 AND archive_id=$2 AND status='Ready'`, id, archiveID)
				if verificationErr == nil {
					referenced = verification.Next()
					verificationErr = verification.Err()
					verification.Close()
				}
				// A failed commit acknowledgement does not prove that publication rolled back.
				if verificationErr == nil && !referenced {
					os.Remove(filepath.Join(w.Service.Config.ExportPath, archiveID+".zip"))
				}
			}
			if err != nil {
				return err
			}
			continue
		}
		os.Remove(filepath.Join(w.Service.Config.ExportPath, archiveID+".zip"))
		status := "Queued"
		var failure *string
		if errors.Is(generationErr, application.ErrExportTooLarge) {
			status = "Failed"
			value := "TooLarge"
			failure = &value
		} else if attempts >= w.Service.Config.Jobs().ExportMaximumAttempts {
			status = "Failed"
			value := "GenerationFailed"
			failure = &value
		}
		command, err := w.Service.Store.Pool.Exec(ctx, `UPDATE member_data_exports SET status=$3,failure=$4,lease_id=NULL,locked_until=NULL,available_at=$5 WHERE id=$1 AND lease_id=$2`, id, lease, status, failure, w.Service.Now().UTC().Add(retryDelay(attempts, w.Service.Config.Jobs().FirstRetryDelay, w.Service.Config.Jobs().MaximumRetryDelay)))
		if err != nil {
			return err
		}
		if status == "Failed" {
			w.Telemetry.RecordTerminalFailures(telemetry.PersonalDataExport, command.RowsAffected())
		}
	}
	return w.cleanupExports(ctx)
}

func (w *Worker) cleanupExports(ctx context.Context) error {
	now := w.Service.Now().UTC()
	command, err := w.Service.Store.Pool.Exec(ctx, `UPDATE member_data_exports SET status='Failed',failure='GenerationFailed',lease_id=NULL,locked_until=NULL WHERE status='Processing' AND locked_until<=$1 AND attempt_count>=$2`, now, w.Service.Config.Jobs().ExportMaximumAttempts)
	if err != nil {
		return err
	}
	w.Telemetry.RecordTerminalFailures(telemetry.PersonalDataExport, command.RowsAffected())
	if _, err := w.Service.Store.Pool.Exec(ctx, `UPDATE member_data_exports SET status='Expired' WHERE status='Ready' AND expires_at<=$1`, now); err != nil {
		return err
	}
	rows, err := w.Service.Store.Pool.Query(ctx, `SELECT id::text,archive_id::text FROM member_data_exports WHERE files_cleaned_at IS NULL AND archive_id IS NOT NULL AND (status='Expired' OR member_id IS NULL) ORDER BY created_at LIMIT 100`)
	if err != nil {
		return err
	}
	type item struct{ ID, Archive string }
	items, err := pgx.CollectRows(rows, pgx.RowToStructByPos[item])
	if err != nil {
		return err
	}
	for _, item := range items {
		if err = os.Remove(filepath.Join(w.Service.Config.ExportPath, item.Archive+".zip")); err != nil && !errors.Is(err, os.ErrNotExist) {
			return err
		}
		if _, err = w.Service.Store.Pool.Exec(ctx, `UPDATE member_data_exports SET files_cleaned_at=$2 WHERE id=$1`, item.ID, now); err != nil {
			return err
		}
	}
	return nil
}
