package application

import (
	"context"
	"github.com/JennGllg/mon-kado/internal/domain"
	"time"
)

// EmailSender delivers one provider-independent message without transparent POST retries.
type EmailSender interface {
	// Send performs one cancellation-aware provider attempt with a stable outbox identifier.
	Send(ctx context.Context, id string, message EmailMessage) (string, error)
}

// WishImportClient extracts editable suggestions without creating a resource.
type WishImportClient interface {
	// Preview validates every destination and returns bounded best-effort merchant data.
	Preview(context.Context, string) (domain.WishImportPreview, error)
}

// DeliveryError classifies a provider failure without retaining sensitive response content.
type DeliveryError struct {
	Category   string
	Retryable  bool
	RetryAfter time.Duration
}

func (e *DeliveryError) Error() string { return "email delivery failed: " + e.Category }
