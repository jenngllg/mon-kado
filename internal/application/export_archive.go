package application

import (
	"archive/zip"
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/json"
	"errors"
	"github.com/JennGllg/mon-kado/internal/images"
	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"
	"io"
	"os"
	"path/filepath"
	"time"
)

// ErrExportTooLarge identifies the permanent size limit without exposing private contents.
var ErrExportTooLarge = errors.New("personal data archive exceeds the size limit")

type exportWriter struct {
	Context   context.Context
	Writer    io.Writer
	Remaining int64
}

func (w *exportWriter) Write(data []byte) (int, error) {
	if err := w.Context.Err(); err != nil {
		return 0, err
	}
	if int64(len(data)) > w.Remaining {
		return 0, ErrExportTooLarge
	}
	n, err := w.Writer.Write(data)
	w.Remaining -= int64(n)
	return n, err
}

type exportImage struct {
	ID, Path string
	Hash     []byte
}

// GenerateExport streams one consistent snapshot and verified immutable images to a private staged ZIP.
func (s *Service) GenerateExport(ctx context.Context, user, archiveID string) (time.Time, int64, error) {
	var snapshot time.Time
	if parsed, err := uuid.Parse(archiveID); err != nil || parsed == uuid.Nil || parsed.String() != archiveID {
		return snapshot, 0, errors.New("invalid archive identifier")
	}
	if err := os.MkdirAll(s.Config.ExportPath, 0700); err != nil {
		return snapshot, 0, err
	}
	root, err := os.Lstat(s.Config.ExportPath)
	if err != nil || !root.IsDir() || root.Mode()&os.ModeSymlink != 0 {
		return snapshot, 0, errors.New("export storage root is invalid")
	}
	path := filepath.Join(s.Config.ExportPath, archiveID+".pending")
	file, err := os.OpenFile(path, os.O_CREATE|os.O_EXCL|os.O_RDWR, 0600)
	if err != nil {
		return snapshot, 0, err
	}
	defer file.Close()
	published := false
	defer func() {
		if !published {
			os.Remove(path)
		}
	}()
	manifest, err := os.CreateTemp(s.Config.ExportPath, "manifest-*")
	if err != nil {
		return snapshot, 0, err
	}
	defer func() { manifest.Close(); os.Remove(manifest.Name()) }()
	archive := zip.NewWriter(&exportWriter{Context: ctx, Writer: file, Remaining: s.Config.Jobs().ExportMaximumBytes})
	destination, err := archive.Create("data.json")
	if err != nil {
		return snapshot, 0, err
	}
	destination = &exportWriter{Context: ctx, Writer: destination, Remaining: s.Config.Jobs().ExportMaximumBytes}
	tx, err := s.Store.Pool.BeginTx(ctx, pgx.TxOptions{IsoLevel: pgx.RepeatableRead, AccessMode: pgx.ReadOnly})
	if err != nil {
		return snapshot, 0, err
	}
	defer tx.Rollback(context.WithoutCancel(ctx))
	snapshot = s.Now().UTC()
	if _, err = io.WriteString(destination, `{"schemaVersion":1,"snapshotAt":`); err != nil {
		return snapshot, 0, err
	}
	data, _ := json.Marshal(snapshot)
	if _, err = destination.Write(data); err != nil {
		return snapshot, 0, err
	}
	if _, err = io.WriteString(destination, `,"account":`); err != nil {
		return snapshot, 0, err
	}
	var account []byte
	var imageID *string
	var hash []byte
	err = tx.QueryRow(ctx, `SELECT jsonb_build_object('profile',jsonb_build_object('id',id,'email',email,'emailConfirmed',email_confirmed,'displayName',display_name,'displayNameTag',display_name_tag,'isVisibleInMemberSearch',is_visible_in_member_search,'createdAt',created_at,'updatedAt',updated_at,'imagePath',CASE WHEN profile_image_id IS NULL THEN NULL ELSE 'images/profile.webp' END),'authentication',jsonb_build_object('hasPassword',password_hash IS NOT NULL,'phoneNumber',phone_number,'phoneNumberConfirmed',phone_number_confirmed,'twoFactorEnabled',two_factor_enabled,'lockoutEnabled',lockout_enabled,'lockoutEnd',lockout_end,'accessFailedCount',access_failed_count),'roles',coalesce((SELECT jsonb_agg(r.name ORDER BY r.name) FROM roles r JOIN user_roles ur ON ur.role_id=r.id WHERE ur.user_id=u.id),'[]'::jsonb),'externalAccounts',coalesce((SELECT jsonb_agg(jsonb_build_object('provider',login_provider,'providerUserId',provider_key,'providerDisplayName',provider_display_name) ORDER BY login_provider) FROM user_logins WHERE user_id=u.id),'[]'::jsonb)),profile_image_id::text,profile_image_hash FROM users u WHERE id=$1`, user).Scan(&account, &imageID, &hash)
	if err != nil {
		return snapshot, 0, err
	}
	if _, err = destination.Write(account); err != nil {
		return snapshot, 0, err
	}
	imageEncoder := json.NewEncoder(manifest)
	if imageID != nil {
		if err = imageEncoder.Encode(exportImage{ID: *imageID, Path: "images/profile.webp", Hash: hash}); err != nil {
			return snapshot, 0, err
		}
	}
	arraySeparator := ","
	writeArray := func(name, query string, includeImages bool) error {
		if _, err := io.WriteString(destination, arraySeparator+`"`+name+`":[`); err != nil {
			return err
		}
		arraySeparator = ","
		rows, err := tx.Query(ctx, query, user)
		if err != nil {
			return err
		}
		defer rows.Close()
		first := true
		for rows.Next() {
			var row []byte
			var imageID *string
			var wishID string
			var hash []byte
			if includeImages {
				err = rows.Scan(&row, &imageID, &wishID, &hash)
			} else {
				err = rows.Scan(&row)
			}
			if err != nil {
				return err
			}
			if !first {
				if _, err = io.WriteString(destination, ","); err != nil {
					return err
				}
			}
			first = false
			if _, err = destination.Write(row); err != nil {
				return err
			}
			if imageID != nil {
				if err = imageEncoder.Encode(exportImage{ID: *imageID, Path: "images/" + name + "/" + wishID + ".webp", Hash: hash}); err != nil {
					return err
				}
			}
		}
		if err = rows.Err(); err != nil {
			return err
		}
		_, err = io.WriteString(destination, "]")
		return err
	}
	projections := []struct {
		Name, Query string
		Images      bool
	}{
		{"wishlists", `SELECT jsonb_build_object('id',id,'name',name,'occasion',lower(left(occasion,1))||substr(occasion,2),'eventDate',event_date,'message',message,'surpriseMode',surprise_mode,'isArchived',is_archived,'createdAt',created_at,'updatedAt',updated_at,'isSuspended',is_suspended,'suspensionReason',suspension_reason,'suspendedAt',suspended_at) FROM wishlists WHERE owner_id=$1 ORDER BY id`, false},
		{"wishlistSubscriptions", `SELECT jsonb_build_object('id',id,'wishlistId',wishlist_id,'createdAt',created_at,'updatedAt',NULL) FROM wishlist_subscriptions WHERE member_id=$1 ORDER BY id`, false},
		{"sharing", `SELECT jsonb_build_object('id',sl.id,'wishlistId',wishlist_id,'createdAt',sl.created_at,'updatedAt',sl.updated_at) FROM wishlist_share_links sl JOIN wishlists l ON l.id=sl.wishlist_id WHERE l.owner_id=$1 ORDER BY wishlist_id`, false},
		{"wishes", `SELECT jsonb_build_object('id',w.id,'wishlistId',wishlist_id,'name',w.name,'note',note,'color',color,'size',size,'url',url,'price',price,'quantity',quantity,'isFavorite',is_favorite,'isSurprise',is_surprise,'acceptsSecondHand',accepts_second_hand,'position',position,'createdAt',w.created_at,'updatedAt',w.updated_at,'imagePath',CASE WHEN image_id IS NULL THEN NULL ELSE 'images/wishes/'||w.id::text||'.webp' END),image_id::text,w.id::text,image_content_hash FROM wishes w JOIN wishlists l ON l.id=w.wishlist_id WHERE l.owner_id=$1 ORDER BY wishlist_id,position,w.id`, true},
		{"groups", `SELECT jsonb_build_object('id',g.id,'name',g.name,'ownerId',g.owner_id,'joinedAt',m.joined_at,'createdAt',g.created_at,'updatedAt',g.updated_at,'imagePath',CASE WHEN g.owner_id=$1 AND g.profile_image_id IS NOT NULL THEN 'images/groups/'||g.id::text||'.webp' ELSE NULL END),CASE WHEN g.owner_id=$1 THEN g.profile_image_id::text ELSE NULL END,g.id::text,g.profile_image_hash FROM group_members m JOIN private_groups g ON g.id=m.group_id WHERE m.member_id=$1 ORDER BY g.id`, true},
		{"groupInvitations", `SELECT jsonb_build_object('id',id,'groupId',group_id,'createdAt',created_at,'expiresAt',expires_at) FROM group_invitations WHERE member_id=$1 ORDER BY id`, false},
		{"groupSharing", `SELECT jsonb_build_object('groupId',group_id,'wishlistId',wishlist_id,'createdAt',created_at) FROM group_wishlists WHERE owner_id=$1 ORDER BY group_id,wishlist_id`, false},
		{"participations", `SELECT jsonb_build_object('id',id,'wishlistId',wishlist_id,'guestDisplayName',guest_display_name,'createdAt',created_at,'updatedAt',updated_at) FROM wishlist_participants WHERE member_id=$1 ORDER BY id`, false},
		{"reservations", `SELECT jsonb_build_object('id',r.id,'wishlistId',r.wishlist_id,'wishId',wish_id,'wishlistParticipantId',wishlist_participant_id,'quantity',quantity,'createdAt',r.created_at,'updatedAt',r.updated_at) FROM gift_reservations r JOIN wishlist_participants p ON p.id=r.wishlist_participant_id WHERE p.member_id=$1 ORDER BY r.id`, false},
		{"reservationHistory", `SELECT jsonb_build_object('id',id,'wishlistId',wishlist_id,'wishlistName',wishlist_name,'wishId',wish_id,'wishName',wish_name,'quantity',quantity,'status',lower(left(status,1))||substr(status,2),'createdAt',created_at,'lastActivityAt',last_activity_at,'endedAt',ended_at) FROM gift_reservation_histories WHERE member_id=$1 ORDER BY created_at,id`, false},
	}
	for _, projection := range projections {
		if err = writeArray(projection.Name, projection.Query, projection.Images); err != nil {
			return snapshot, 0, err
		}
	}
	if _, err = io.WriteString(destination, `,"accountActivity":{`); err != nil {
		return snapshot, 0, err
	}
	arraySeparator = ""
	for _, projection := range []struct{ Name, Query string }{
		{"sessions", `SELECT jsonb_build_object('id',id,'isPersistent',is_persistent,'createdAt',created_at,'renewedAt',renewed_at,'expiresAt',expires_at,'revokedAt',revoked_at) FROM authentication_sessions WHERE user_id=$1 ORDER BY created_at,id`},
		{"emailChangeRequests", `SELECT jsonb_build_object('id',id,'currentEmail',current_email,'newEmail',new_email,'createdAt',created_at,'expiresAt',expires_at,'confirmedAt',confirmed_at,'revokedAt',revoked_at) FROM member_email_change_requests WHERE user_id=$1 ORDER BY created_at,id`},
		{"deletionRequests", `SELECT jsonb_build_object('id',id,'email',email,'createdAt',created_at,'expiresAt',expires_at) FROM member_account_deletion_requests WHERE member_id=$1 ORDER BY created_at,id`},
		{"notifications", `SELECT jsonb_build_object('id',id,'kind',CASE kind WHEN 'EMAIL_CONFIRMATION' THEN 'emailConfirmation' WHEN 'PASSWORD_RESET' THEN 'passwordReset' WHEN 'EMAIL_CHANGE_CONFIRMATION' THEN 'emailChangeConfirmation' WHEN 'EMAIL_CHANGE_SECURITY_NOTIFICATION' THEN 'emailChangeSecurityNotification' WHEN 'ACCOUNT_DELETION_CONFIRMATION' THEN 'accountDeletionConfirmation' WHEN 'PASSWORD_CHANGED_SECURITY_NOTIFICATION' THEN 'passwordChangedSecurityNotification' WHEN 'PERSONAL_DATA_EXPORT_READY' THEN 'personalDataExportReady' WHEN 'TWO_FACTOR_ENROLLED' THEN 'twoFactorEnrolled' WHEN 'TWO_FACTOR_REPLACED' THEN 'twoFactorReplaced' WHEN 'TWO_FACTOR_RECOVERY_CODE_USED' THEN 'twoFactorRecoveryCodeUsed' ELSE 'twoFactorRecoveryCodesRegenerated' END,'recipientEmail',recipient_email,'createdAt',created_at,'processedAt',processed_at,'attemptCount',attempt_count,'hasDeliveryFailure',last_error IS NOT NULL) FROM authentication_email_outbox WHERE user_id=$1 ORDER BY created_at,id`},
	} {
		if err = writeArray(projection.Name, projection.Query, false); err != nil {
			return snapshot, 0, err
		}
	}
	if _, err = io.WriteString(destination, "}}"); err != nil {
		return snapshot, 0, err
	}
	if err = tx.Commit(ctx); err != nil {
		return snapshot, 0, err
	}
	if _, err = manifest.Seek(0, io.SeekStart); err != nil {
		return snapshot, 0, err
	}
	imageDecoder := json.NewDecoder(manifest)
	for {
		var entry exportImage
		if err = imageDecoder.Decode(&entry); errors.Is(err, io.EOF) {
			break
		}
		if err != nil {
			return snapshot, 0, err
		}
		content, err := images.Read(ctx, s.Config.ImagePath, entry.ID)
		if err != nil {
			return snapshot, 0, err
		}
		actual := sha256.Sum256(content)
		if subtle.ConstantTimeCompare(actual[:], entry.Hash) != 1 {
			return snapshot, 0, errors.New("export image integrity check failed")
		}
		destination, err := archive.CreateHeader(&zip.FileHeader{Name: entry.Path, Method: zip.Store})
		if err != nil {
			return snapshot, 0, err
		}
		if _, err = destination.Write(content); err != nil {
			return snapshot, 0, err
		}
	}
	readme, err := archive.Create("README.txt")
	if err != nil {
		return snapshot, 0, err
	}
	if _, err = io.WriteString(readme, "MonKado personal data export - schema version 1\n\ndata.json contains retained account data at snapshotAt. Dates are UTC. Images are normalized WebP files. No password, credential, token, security stamp or sharing secret is included. Other people's reservations and internal moderation records are excluded. Deleted records cannot be reconstructed. This archive cannot be reimported. Keep it private. For additional access requests, use the support contact in MonKado's privacy notice.\n"); err != nil {
		return snapshot, 0, err
	}
	if err = archive.Close(); err != nil {
		return snapshot, 0, err
	}
	if err = file.Sync(); err != nil {
		return snapshot, 0, err
	}
	info, err := file.Stat()
	if err != nil {
		return snapshot, 0, err
	}
	if err = file.Close(); err != nil {
		return snapshot, 0, err
	}
	if err = os.Rename(path, filepath.Join(s.Config.ExportPath, archiveID+".zip")); err != nil {
		return snapshot, 0, err
	}
	published = true
	return snapshot, info.Size(), nil
}
