//go:build integration

package httpapi_test

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/domain"
)

type removedPreview struct{ calls int }

func (f *removedPreview) Preview(context.Context, string) (domain.WishImportPreview, error) {
	f.calls++
	return domain.WishImportPreview{}, nil
}
