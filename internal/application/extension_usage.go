package application

import (
	"context"

	"github.com/jackc/pgx/v5"
)

// Keep usage separate from users so recording an addition does not invalidate profile ETags.
func recordExtensionAddition(ctx context.Context, tx pgx.Tx, member string, extension *bool) error {
	if extension == nil || !*extension {
		return nil
	}
	_, err := tx.Exec(ctx, `INSERT INTO member_extension_usage(member_id,has_added) VALUES($1,true)
		ON CONFLICT(member_id) DO UPDATE SET has_added=true WHERE member_extension_usage.has_added IS DISTINCT FROM true`, member)
	return err
}
