//go:build integration

package httpapi_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/admission"
	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestImageAdmission_WhenQueueIsFull_RejectsBeforeBodyBindingAndPreservesOtherRequests(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "admission-member@example.test")
	enqueued := make(chan struct{}, 2)
	f.api.ImageAdmission = admission.New(func(ctx context.Context, _ time.Duration) (context.Context, context.CancelFunc) {
		enqueued <- struct{}{}
		return context.WithCancel(ctx)
	})
	active, _, _ := f.api.ImageAdmission.Acquire(t.Context())
	defer active()
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	completed := make(chan error, 2)
	for index := 0; index < 2; index++ {
		go func() { _, _, err := f.api.ImageAdmission.Acquire(ctx); completed <- err }()
		<-enqueued
	}
	// Act / Assert
	for _, request := range []struct{ method, path string }{
		{"PUT", "/api/v1/members/current/profile/image"},
		{"PUT", "/api/v1/wishlists/01950000-0000-7000-8000-000000000001/wishes/01950000-0000-7000-8000-000000000002/image"},
		{"POST", "/api/v1/wishlists/01950000-0000-7000-8000-000000000001/wish-import-previews"},
		{"POST", "/api/v1/wishlists/01950000-0000-7000-8000-000000000001/wishes/copies"},
	} {
		var failure domain.ErrorResponse
		rejected := f.send(t, request.method, request.path, "not JSON", nil)
		if rejected.Header.Get("Retry-After") != "60" {
			t.Fatal("admission retry header differs")
		}
		requireStatus(t, rejected, 429, &failure)
		if failure.ErrorCode == nil || *failure.ErrorCode != domain.RequestRateLimitExceeded {
			t.Fatal("body was bound before admission")
		}
	}
	requireStatus(t, f.send(t, "GET", "/security/csrf-token", "", nil), 200, nil)
	anonymous := f.visitor(t)
	requireStatus(t, anonymous.send(t, "PUT", "/api/v1/members/current/profile/image", "invalid", nil), 401, nil)
	cancel()
	for index := 0; index < 2; index++ {
		if err := <-completed; !errors.Is(err, context.Canceled) {
			t.Fatal("waiter cancellation lost")
		}
	}
}
